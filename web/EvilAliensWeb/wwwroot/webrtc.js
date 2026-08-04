// Online co-op real transport (card 11.4): JS owns the RTCPeerConnection + the room-code
// signaling WebSocket; C# sees only Compat/Net/WebRtcInterop (the webcam.js/eaNet house
// pattern). Inert until eaRtc.host()/join() is called -- a plain boot touches no server
// (the static-site invariant).
//
// Two DataChannels mirror the INetTransport lanes: "s" = stream (unordered, maxRetransmits
// 0 -- may drop/reorder, consumers tolerate both) and "r" = reliable (ordered, guaranteed).
// Payloads cross the C# boundary as base64 (house convention); the wire carries raw bytes.
// A 1-byte 0x00 frame on the reliable channel is a JS-level "bye" (0x00 is reserved: C#
// message types start at 0x01) -- sent on pagehide so a clean tab close drops the peer
// instantly; silent deaths are still caught by the C# stream timeout.
//
// Signaling protocol (server/signal/main.py): {t:host} -> {t:code}; {t:join,code};
// {t:peer} both sides when paired; then {t:sdp}/{t:ice} relayed verbatim; {t:gone} on a
// member leaving; {t:error,reason}. The WS is closed once both channels are open -- the
// server is out of the loop and gameplay is pure P2P.
window.eaRtc = (() => {
    const STUN = [{ urls: 'stun:stun.l.google.com:19302' }, { urls: 'stun:stun1.l.google.com:19302' }];
    const CONNECT_TIMEOUT_MS = 20000;
    let ws = null, pc = null, chS = null, chR = null;
    let isHost = false, connected = false, finished = false, connectTimer = null;
    let overlay = null;

    // Public game browser (card 2001fbd8). A LISTED single-player host keeps this same
    // signaling WS open, beats to hold its room alive, and auto-pongs browser pings; a
    // peer arriving drives the normal host handshake below (= join-in-progress). BROWSING
    // for games uses a SEPARATE socket (bws) in a third role that belongs to no room.
    let beatTimer = null, listing = false, listMeta = null;
    let bws = null, browseTimer = null;
    // Only used when localStorage is unavailable -- see peerId() below.
    let fallbackPeerId = '';
    const pingSentAt = new Map(); // room code -> performance.now() of its last ping
    const BEAT_MS = 30000, BROWSE_REFRESH_MS = 4000;

    // Room thumbnails (card e7404647). The SERVER decides when a shot is taken --
    // it pulls, we answer, and a client that is never pulled sends nothing ever.
    // JPEG is the only place image bytes are compressed: C# hands us raw RGBA it
    // captured off the scene render target (never the canvas -- see NetRoomShot),
    // and on the way back we hand C# raw RGBA again, so no C# code has to know
    // what a JPEG is.
    const SHOT_QUALITY = 0.6;
    const MAX_SHOT_CHARS = 48 * 1024;   // mirrors the server's MAX_SHOT_BYTES
    let shotCanvas = null;              // reused for both encode and decode

    const invoke = (m, ...a) => {
        try { DotNet.invokeMethod('EvilAliensWeb', m, ...a); }
        catch (e) { console.warn('[rtc] ' + m + ' dispatch failed: ' + e.message); }
    };
    const phase = (p, detail) => {
        console.log('[rtc] ' + p + (detail ? ' ' + detail : ''));
        invoke('rtcPhase', String(p), String(detail || ''));
    };
    // failed/closed are terminal and must fire at most once (ws + pc + channels each
    // have close/error handlers that would otherwise stack notifications).
    const fail = (reason) => {
        if (finished) return;
        finished = true;
        phase('failed', reason);
        teardown();
    };
    const b64FromBuf = (buf) => {
        const u8 = new Uint8Array(buf);
        let s = '';
        for (let i = 0; i < u8.length; i++) s += String.fromCharCode(u8[i]);
        return btoa(s);
    };
    const bufFromB64 = (b64) => {
        const s = atob(b64);
        const u8 = new Uint8Array(s.length);
        for (let i = 0; i < s.length; i++) u8[i] = s.charCodeAt(i);
        return u8;
    };

    const sendSignal = (obj) => { if (ws && ws.readyState === WebSocket.OPEN) ws.send(JSON.stringify(obj)); };

    const bye = () => {
        try { if (chR && chR.readyState === 'open') chR.send(new Uint8Array([0])); } catch (e) { }
    };

    const stopBeat = () => { if (beatTimer) { clearInterval(beatTimer); beatTimer = null; } };
    const sendList = () => {
        if (!listMeta) return;
        sendSignal({ t: 'list', level: listMeta.level, difficulty: listMeta.difficulty,
            players: listMeta.players, proto: listMeta.proto, hash: String(window.eaBuildHash || 'dev'),
            // Card e7404647: declare that we understand a {t:'shot'} pull. The
            // server pulls ONLY declaring rooms, which is what lets a new server
            // and an old client coexist -- an old client would answer the pull
            // with nothing, take the server's `bad` reply through fail(), and
            // lose its listing over a feature it does not have.
            shots: 1 });
    };

    // Reused 2D scratch canvas, sized to whatever it is asked for. One canvas for
    // encode and decode: they never run in the same turn, and a per-shot canvas
    // would churn a GPU-backed surface every pull.
    const shotCtx = (w, h) => {
        if (!shotCanvas) shotCanvas = document.createElement('canvas');
        if (shotCanvas.width !== w || shotCanvas.height !== h) {
            shotCanvas.width = w; shotCanvas.height = h;
        }
        return shotCanvas.getContext('2d', { willReadFrequently: true });
    };

    const teardown = () => {
        window.removeEventListener('pagehide', bye);
        stopBeat();
        listing = false;
        if (connectTimer) { clearTimeout(connectTimer); connectTimer = null; }
        if (ws) { try { ws.onclose = null; ws.close(); } catch (e) { } ws = null; }
        const kill = (ch) => { if (ch) { try { ch.onclose = null; ch.close(); } catch (e) { } } };
        kill(chS); kill(chR); chS = chR = null;
        if (pc) { try { pc.onconnectionstatechange = null; pc.close(); } catch (e) { } pc = null; }
        connected = false;
    };

    const wireChannel = (ch, rel) => {
        ch.binaryType = 'arraybuffer';
        ch.onmessage = (ev) => {
            const u8 = new Uint8Array(ev.data);
            if (rel && u8.length === 1 && u8[0] === 0) { // JS-level bye frame
                if (!finished) { finished = true; phase('closed', 'bye'); teardown(); }
                return;
            }
            invoke('rtcData', b64FromBuf(ev.data), rel);
        };
        ch.onopen = () => {
            if (connected || finished) return;
            if (chS && chS.readyState === 'open' && chR && chR.readyState === 'open') {
                connected = true;
                if (connectTimer) { clearTimeout(connectTimer); connectTimer = null; }
                // The listing role is over -- a peer arrived, the game is now full.
                stopBeat();
                listing = false;
                // Signaling's job is done -- gameplay never touches the server again.
                if (ws) { try { ws.onclose = null; ws.close(); } catch (e) { } ws = null; }
                phase('connected');
            }
        };
        ch.onclose = () => {
            if (!finished && connected) { finished = true; phase('closed', 'channel'); teardown(); }
        };
    };

    const makePc = () => {
        pc = new RTCPeerConnection({ iceServers: STUN });
        pc.onicecandidate = (ev) => { if (ev.candidate) sendSignal({ t: 'ice', c: ev.candidate.toJSON() }); };
        pc.onconnectionstatechange = () => {
            if (pc && (pc.connectionState === 'failed' || pc.connectionState === 'disconnected') && !connected) fail('ice');
        };
        // No TURN in v1: symmetric-NAT pairs (~10-15%) never complete ICE -- surface a
        // clean "could not connect" instead of hanging forever.
        connectTimer = setTimeout(() => { if (!connected) fail('timeout'); }, CONNECT_TIMEOUT_MS);
        window.addEventListener('pagehide', bye);
    };

    const startAsHost = async () => {
        makePc();
        chS = pc.createDataChannel('s', { ordered: false, maxRetransmits: 0 });
        chR = pc.createDataChannel('r');
        wireChannel(chS, false);
        wireChannel(chR, true);
        const offer = await pc.createOffer();
        await pc.setLocalDescription(offer);
        sendSignal({ t: 'sdp', d: pc.localDescription });
    };

    const startAsJoiner = () => {
        makePc();
        pc.ondatachannel = (ev) => {
            if (ev.channel.label === 's') { chS = ev.channel; wireChannel(chS, false); }
            else if (ev.channel.label === 'r') { chR = ev.channel; wireChannel(chR, true); }
        };
    };

    const onSignalMessage = async (ev) => {
        let m;
        try { m = JSON.parse(ev.data); } catch (e) { return; }
        try {
            if (m.t === 'code') {
                phase('code', m.code);
                // Listed host: advertise now that we have a room, and start the heartbeat
                // that keeps the room alive across a long level (TTL counts from last beat).
                if (listing) {
                    sendList();
                    if (!beatTimer) beatTimer = setInterval(() => sendSignal({ t: 'beat' }), BEAT_MS);
                }
            }
            else if (m.t === 'peer') { phase('peer'); if (isHost) await startAsHost(); else startAsJoiner(); }
            // A browser is pinging us (relayed by the server). Auto-pong in JS without
            // touching C#, so the measured RTT is the network, not our frame pacing.
            else if (m.t === 'ping') sendSignal({ t: 'pong', id: m.id, ref: m.ref });
            // The server is pulling a thumbnail off us (card e7404647). Unlike the
            // ping this CANNOT be answered in JS: the picture has to come from the
            // game's scene target, so C# arms a capture and calls sendShot back on
            // its next post-draw. Ignored unless we are actually listing -- a pull
            // arriving at a room that has since gone private answers nothing.
            else if (m.t === 'shot') { if (listing) invoke('rtcShotRequest'); }
            else if (m.t === 'sdp') {
                await pc.setRemoteDescription(m.d);
                if (!isHost) {
                    const answer = await pc.createAnswer();
                    await pc.setLocalDescription(answer);
                    sendSignal({ t: 'sdp', d: pc.localDescription });
                }
            }
            else if (m.t === 'ice') { if (pc) await pc.addIceCandidate(m.c); }
            else if (m.t === 'gone') { if (!connected) fail('gone'); }
            else if (m.t === 'error') fail(m.reason || 'server');
        } catch (e) {
            console.warn('[rtc] signal handling failed: ' + e.message);
            fail('protocol');
        }
    };

    const openSignaling = (url, onOpen) => {
        finished = false;
        try { ws = new WebSocket(url); }
        catch (e) { fail('signal'); return; }
        ws.onopen = onOpen;
        // Serialize the async handler: WS frames arrive in order, but each onmessage is
        // async and awaits (setRemoteDescription etc.) -- unchained, a relayed ICE frame
        // could run addIceCandidate before the offer's setRemoteDescription resolves and
        // kill a viable pairing with InvalidStateError.
        let chain = Promise.resolve();
        ws.onmessage = (ev) => { chain = chain.then(() => onSignalMessage(ev)); };
        ws.onerror = () => { if (!connected) fail('signal'); };
        ws.onclose = () => { if (!connected && !finished) fail('signal'); };
    };

    return {
        host(signalUrl) {
            if (ws || pc) return;
            isHost = true;
            phase('contacting');
            openSignaling(signalUrl, () => sendSignal({ t: 'host' }));
        },
        join(signalUrl, code) {
            if (ws || pc) return;
            isHost = false;
            phase('contacting');
            openSignaling(signalUrl, () => sendSignal({ t: 'join', code: String(code || '') }));
        },
        send(b64, rel) {
            const ch = rel ? chR : chS;
            if (ch && ch.readyState === 'open') {
                try { ch.send(bufFromB64(b64)); } catch (e) { }
            }
        },
        close() {
            finished = true; // deliberate local close -- suppress failed/closed callbacks
            bye();
            teardown();
            this.closePrompt();
        },
        buildHash() { return String(window.eaBuildHash || 'dev'); },

        // This browser's own identity token (card 0b8a300b). Minted once at random and kept
        // in localStorage; the hello carries an FNV-1a hash of it so a host that kicked +
        // blocked someone can refuse their rejoin for the rest of its level.
        //
        // It is sent ONLY to a peer we are already connected to P2P -- never to the signaling
        // server -- and it is self-reported, so it stops casual re-joining and nothing more:
        // clearing site data or an incognito window mints a fresh one. That is the intended
        // strength; do not build anything that has to trust it on top of this.
        //
        // The flip side, stated plainly: because it PERSISTS, anyone you play with can recognise
        // you across rooms and days. That is a deliberate trade, not an oversight -- a token
        // scoped to the page load would be regenerated by the reload a kicked griefer performs
        // anyway, which would make blocking useless. It is random and reveals nothing but
        // itself, and only peers you actually connect to ever see it.
        peerId() {
            try {
                let id = window.localStorage.getItem('eaPeerId');
                if (!id) {
                    const b = new Uint8Array(16);
                    crypto.getRandomValues(b);
                    id = Array.from(b, x => x.toString(16).padStart(2, '0')).join('');
                    window.localStorage.setItem('eaPeerId', id);
                }
                return String(id);
            } catch (e) {
                // Storage blocked (private mode / cookies off): a per-page-load id still works
                // for the length of one session, which is the scope a block lives in anyway.
                if (!fallbackPeerId) {
                    const b = new Uint8Array(16);
                    crypto.getRandomValues(b);
                    fallbackPeerId = Array.from(b, x => x.toString(16).padStart(2, '0')).join('');
                }
                return fallbackPeerId;
            }
        },

        // ---- public game browser (card 2001fbd8) ------------------------------------

        // Host side: list this single-player game so strangers can find + join it. Opens
        // the signaling WS (reusing the host machinery above) if it isn't already up; the
        // 'code' handler then advertises + starts beating. Called again to UPDATE metadata
        // (level/difficulty/players changed) -- idempotent on the server.
        list(signalUrl, level, difficulty, players, proto) {
            listMeta = { level, difficulty, players, proto: String(proto) };
            if (ws || pc) {
                // Signaling socket already up: this is the metadata-update path (or a
                // no-op if a non-listing session owns the socket).
                if (listing) sendList();
                return;
            }
            listing = true;
            isHost = true;
            phase('contacting');
            openSignaling(signalUrl, () => sendSignal({ t: 'host' }));
        },
        // Update metadata / re-advertise after an unlist, without reopening the socket.
        relist(level, difficulty, players) {
            if (!listMeta || !ws) return;
            listMeta.level = level; listMeta.difficulty = difficulty; listMeta.players = players;
            listing = true;
            sendList();
        },
        // Hide from browse but keep the room (and its code) joinable + alive; the beat and
        // socket stay so a later relist() reuses the same code.
        unlist() {
            if (!listing) return;
            listing = false;
            sendSignal({ t: 'unlist' });
        },
        // Fully stop listing (level exit with no peer): drop the room entirely. A session
        // that already started owns its own teardown via close().
        endListing() {
            listing = false;
            listMeta = null;
            stopBeat();
            if (!connected && !pc && ws) { try { ws.onclose = null; ws.close(); } catch (e) { } ws = null; }
        },

        // Host side, card e7404647: answer a pull. `b64` is raw RGBA straight out of
        // C#'s capture (w*h*4 bytes), which we paint into a scratch canvas purely to
        // get the browser's JPEG encoder. Alpha is already sealed to 255 by the
        // capture -- it has to be, because toDataURL('image/jpeg') composites a
        // translucent canvas over black and would hand the server a darkened frame.
        sendShot(b64, w, h) {
            if (!listing || !ws || ws.readyState !== WebSocket.OPEN) return;
            try {
                const bytes = bufFromB64(b64);
                if (bytes.length !== w * h * 4) return;
                const ctx = shotCtx(w, h);
                ctx.putImageData(new ImageData(new Uint8ClampedArray(bytes), w, h), 0, 0);
                const url = shotCanvas.toDataURL('image/jpeg', SHOT_QUALITY);
                const data = url.slice(url.indexOf(',') + 1);
                // Refused here as well as server-side: a frame the server would drop
                // is not worth the socket traffic, and this is the only side that can
                // say WHY in a way a developer will see.
                if (data.length > MAX_SHOT_CHARS) {
                    console.warn('[rtc] thumbnail too large (' + data.length + ' chars) -- not sent');
                    return;
                }
                sendSignal({ t: 'shot', data });
            } catch (e) {
                console.warn('[rtc] thumbnail encode failed: ' + e.message);
            }
        },

        // Joiner side, card e7404647: fetch one room's stored thumbnail. Only ever
        // called for a code whose listing carried a non-zero seq, so an older server
        // (which never sends that field, and would answer this with a fatal `bad`)
        // is never asked.
        shotGet(code) {
            if (bws && bws.readyState === WebSocket.OPEN) {
                try { bws.send(JSON.stringify({ t: 'shotget', code: String(code) })); } catch (e) { }
            }
        },

        // Joiner side: open the browse socket (a third role, no room), fetch the listed
        // build-compatible games, and ping each host in parallel. Rooms arrive via
        // rtcRooms; each entry's RTT fills in via rtcPing as its pong lands.
        browse(signalUrl, proto) {
            if (bws) return;
            pingSentAt.clear();
            try { bws = new WebSocket(signalUrl); }
            catch (e) { invoke('rtcBrowseFailed', 'signal'); bws = null; return; }
            const doBrowse = () => {
                if (bws && bws.readyState === WebSocket.OPEN) {
                    bws.send(JSON.stringify({ t: 'browse', proto: String(proto), hash: String(window.eaBuildHash || 'dev') }));
                }
            };
            bws.onopen = () => { doBrowse(); browseTimer = setInterval(doBrowse, BROWSE_REFRESH_MS); };
            bws.onmessage = (ev) => {
                let m;
                try { m = JSON.parse(ev.data); } catch (e) { return; }
                if (m.t === 'rooms') {
                    const list = Array.isArray(m.rooms) ? m.rooms : [];
                    invoke('rtcRooms', JSON.stringify(list));
                    const now = performance.now();
                    for (const r of list) {
                        if (!r || typeof r.code !== 'string') continue;
                        pingSentAt.set(r.code, now);
                        try { bws.send(JSON.stringify({ t: 'ping', code: r.code, id: r.code })); } catch (e) { }
                    }
                } else if (m.t === 'pong') {
                    const t0 = pingSentAt.get(m.id);
                    if (t0 !== undefined) invoke('rtcPing', String(m.id), Math.round(performance.now() - t0));
                } else if (m.t === 'shot') {
                    // A fetched thumbnail (card e7404647). Decoded to raw RGBA here so
                    // C# only ever sees pixels; seq 0 / empty data is the server saying
                    // it has nothing, which C# needs so it can retire the request
                    // rather than ask forever.
                    const code = String(m.code || '');
                    const seq = Number(m.seq) || 0;
                    if (!code) return;
                    if (!seq || !m.data) { invoke('rtcShot', code, 0, '', 0, 0); return; }
                    const img = new Image();
                    img.onload = () => {
                        try {
                            const w = img.naturalWidth, h = img.naturalHeight;
                            const ctx = shotCtx(w, h);
                            ctx.clearRect(0, 0, w, h);
                            ctx.drawImage(img, 0, 0);
                            const px = ctx.getImageData(0, 0, w, h).data;
                            invoke('rtcShot', code, seq, b64FromBuf(px.buffer), w, h);
                        } catch (e) {
                            console.warn('[rtc] thumbnail decode failed: ' + e.message);
                            invoke('rtcShot', code, 0, '', 0, 0);
                        }
                    };
                    // A corrupt/hostile JPEG must retire the request too, not wedge it.
                    img.onerror = () => invoke('rtcShot', code, 0, '', 0, 0);
                    img.src = 'data:image/jpeg;base64,' + String(m.data);
                } else if (m.t === 'error') {
                    invoke('rtcBrowseFailed', String(m.reason || 'server'));
                }
            };
            bws.onerror = () => { invoke('rtcBrowseFailed', 'signal'); };
            bws.onclose = () => { if (browseTimer) { clearInterval(browseTimer); browseTimer = null; } };
        },
        endBrowse() {
            if (browseTimer) { clearInterval(browseTimer); browseTimer = null; }
            if (bws) { try { bws.onclose = null; bws.close(); } catch (e) { } bws = null; }
            pingSentAt.clear();
        },

        // Room-code entry overlay (the house outside-#app pattern, like the slider panels/
        // trailer overlay). Built on demand; Join -> rtcCodeEntry(code), Cancel/Esc ->
        // rtcCodeEntry(''). Keys are stopPropagation'd so typing never drives the menu.
        promptCode() {
            if (overlay) { overlay.querySelector('input').focus(); return; }
            const wrap = document.createElement('div');
            wrap.id = 'ea-rtc-prompt';
            wrap.style.cssText = 'position:fixed;inset:0;z-index:60;display:flex;align-items:center;justify-content:center;background:rgba(0,0,0,0.6);font-family:Consolas,monospace;';
            const box = document.createElement('div');
            box.style.cssText = 'background:#101018;border:2px solid #46e;border-radius:10px;padding:28px 34px;text-align:center;color:#cdf;';
            box.innerHTML = '<div style="font-size:20px;margin-bottom:14px;letter-spacing:2px;">ENTER ROOM CODE</div>';
            const input = document.createElement('input');
            input.type = 'text';
            input.maxLength = 5;
            input.autocomplete = 'off';
            input.spellcheck = false;
            input.style.cssText = 'width:7ch;font-size:34px;text-align:center;letter-spacing:6px;text-transform:uppercase;background:#000;color:#8cf;border:1px solid #46e;border-radius:6px;padding:6px 10px;outline:none;';
            const row = document.createElement('div');
            row.style.cssText = 'margin-top:18px;display:flex;gap:14px;justify-content:center;';
            const mkBtn = (label) => {
                const b = document.createElement('button');
                b.textContent = label;
                b.style.cssText = 'font-size:16px;padding:8px 22px;background:#223;color:#cdf;border:1px solid #46e;border-radius:6px;cursor:pointer;';
                return b;
            };
            const joinBtn = mkBtn('JOIN'), cancelBtn = mkBtn('CANCEL');
            const done = (code) => {
                this.closePrompt();
                invoke('rtcCodeEntry', String(code));
                const canvas = document.querySelector('#app canvas');
                if (canvas) canvas.focus();
            };
            joinBtn.onclick = () => { const c = input.value.trim().toUpperCase(); if (c.length === 5) done(c); else input.focus(); };
            cancelBtn.onclick = () => done('');
            wrap.addEventListener('keydown', (e) => {
                e.stopPropagation();
                if (e.key === 'Enter') joinBtn.onclick();
                else if (e.key === 'Escape') done('');
            }, true);
            row.appendChild(joinBtn);
            row.appendChild(cancelBtn);
            box.appendChild(input);
            box.appendChild(row);
            wrap.appendChild(box);
            document.body.appendChild(wrap);
            overlay = wrap;
            input.focus();
        },
        closePrompt() {
            if (overlay) { overlay.remove(); overlay = null; }
        }
    };
})();
