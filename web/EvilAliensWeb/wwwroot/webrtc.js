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
// Signaling protocol (server/signal/main.py): {t:host[,max]} -> {t:code}; {t:join,code};
// {t:peer} to the joiner / {t:peer,id} to the host when paired; then {t:sdp}/{t:ice}
// relayed (verbatim in a 2-room; `from`-tagged joiner->host and `to`-addressed host->joiner
// in a bigger room); {t:gone[,id]} on a member leaving; {t:error,reason}. The WS is closed
// once the room is FULL (for the shipped 2-room: once the first peer's channels are up) --
// the server is out of the loop and gameplay is pure P2P.
//
// N-PEER (card 583a3ef8): the connection state is a MAP of peer entries, not singletons.
// The host holds one {pc, chS, chR} triple per joiner (server joiner ids 1..3, monotone);
// a joiner holds exactly one entry -- the host. The senderId C# sees ("1".."3" host-side,
// "h" joiner-side) is also the address eaRtc.sendTo takes.
//
// Since card 0257f8ba (Stage 11.10) rooms above 2 are PRODUCTION, not console-rig territory:
// the menu lobby hosts at 4 and listed/JIP rooms open at 4 too (LIST_ROOM_MAX). A >2 host
// KEEPS its signaling ws (and beat) for the room's whole life -- even while momentarily full
// -- so late joiners can still arrive and a freed seat can re-list under the same code; only
// the max-2 flows keep the old close-on-full behaviour byte-identical.
window.eaRtc = (() => {
    const STUN = [{ urls: 'stun:stun.l.google.com:19302' }, { urls: 'stun:stun1.l.google.com:19302' }];
    const CONNECT_TIMEOUT_MS = 20000;
    const HOST_KEY = 0;   // the joiner's single peer entry: the host
    let ws = null;
    let peers = new Map();          // key -> {key, pc, chS, chR, connected, connectTimer}
    let roomMax = 2;                // what host() asked for; join/list flows always 2
    let isHost = false, finished = false;
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
    // Card 0257f8ba: listed / JIP rooms hold up to 4 machines (mirrors Oracle.MaxPlayers).
    const LIST_ROOM_MAX = 4;

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

    // bufferedAmount back-pressure (card 6fb406bc, Stage 11.11). SCTP queues even
    // unreliable-channel sends, so when a link stalls the stream lane does not drop -- it
    // BACKLOGS, and every ship/snapshot frame then arrives late by however much is queued in
    // front of it, which is strictly worse than the loss the lane's consumers are all built to
    // tolerate (interpolation underrun, snapshot self-heal, cumulative shot counts). So a
    // stream send is SKIPPED while the channel already holds more than STREAM_BUF_LIMIT of
    // unsent bytes: at the measured whole-N=4 payload rates that is well over a second of
    // backlog, i.e. a link that is genuinely stalled rather than jittering. The RELIABLE lane
    // is never dropped (the INetTransport contract) -- its backlog is tracked and named once
    // past REL_BUF_WARN so a wedged link says so instead of silently freezing the event lane.
    const STREAM_BUF_LIMIT = 16 * 1024;
    const REL_BUF_WARN = 256 * 1024;
    const netStats = { streamDropped: 0, streamPeak: 0, relPeak: 0, relWarned: false };
    const chanSend = (ch, rel, bytes) => {
        if (!ch || ch.readyState !== 'open') return;
        const buf = ch.bufferedAmount || 0;
        if (rel) {
            if (buf > netStats.relPeak) netStats.relPeak = buf;
            if (buf > REL_BUF_WARN && !netStats.relWarned) {
                netStats.relWarned = true;
                console.warn('[rtc] reliable channel backlog ' + buf + 'B -- the link is not draining');
            }
        } else {
            if (buf > netStats.streamPeak) netStats.streamPeak = buf;
            if (buf > STREAM_BUF_LIMIT) { netStats.streamDropped++; return; }
        }
        try { ch.send(bytes); } catch (e) { }
    };

    // The peer-id string C# sees as OnData's senderId AND takes as sendTo's address.
    const peerIdStr = (key) => isHost ? String(key) : 'h';
    const anyConnected = () => { for (const p of peers.values()) if (p.connected) return true; return false; };
    const connectedCount = () => { let n = 0; for (const p of peers.values()) if (p.connected) n++; return n; };
    const soleJoinerKey = () => { for (const k of peers.keys()) return k; return null; };

    const bye = () => {
        for (const p of peers.values()) {
            try { if (p.chR && p.chR.readyState === 'open') p.chR.send(new Uint8Array([0])); } catch (e) { }
        }
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

    const killPeer = (p) => {
        if (p.connectTimer) { clearTimeout(p.connectTimer); p.connectTimer = null; }
        const kill = (ch) => { if (ch) { try { ch.onclose = null; ch.close(); } catch (e) { } } };
        kill(p.chS); kill(p.chR); p.chS = p.chR = null;
        if (p.pc) { try { p.pc.onconnectionstatechange = null; p.pc.close(); } catch (e) { } p.pc = null; }
        p.connected = false;
    };

    const teardown = () => {
        window.removeEventListener('pagehide', bye);
        stopBeat();
        listing = false;
        if (ws) { try { ws.onclose = null; ws.close(); } catch (e) { } ws = null; }
        for (const p of peers.values()) killPeer(p);
        peers.clear();
    };

    // The signaling socket's job ends when the room is full; the beat and the listing role
    // end with it (a full game has nothing to advertise).
    const signalingDone = () => {
        stopBeat();
        listing = false;
        if (ws) { try { ws.onclose = null; ws.close(); } catch (e) { } ws = null; }
    };

    // ONE peer's link died (bye frame, channel close, ICE failure, connect timeout, or a
    // pre-connect {t:gone,id} seat-free from the server). Escalation keeps every shipped flow
    // exactly as it was: the joiner has only the host, a roomMax==2 host has only its joiner,
    // and a host with nothing left and no open ws to admit more is over -- those take the
    // terminal fail/closed path unchanged. A bigger host otherwise reports 'peergone' (the
    // per-peer session behaviour is card 87242257's) and plays on -- and since card 0257f8ba
    // its ws stays open for the room's whole life, so the departed peer's seat really is
    // replaceable: a fresh joiner arrives through the same still-registered room.
    const peerGone = (p, kind) => {
        if (!peers.has(p.key)) return;
        killPeer(p);
        peers.delete(p.key);
        const wsUp = !!(ws && ws.readyState === WebSocket.OPEN);
        if (!isHost || roomMax <= 2 || (peers.size === 0 && !wsUp)) {
            if (kind === 'bye' || kind === 'channel') {
                if (!finished) { finished = true; phase('closed', kind); teardown(); }
            } else {
                fail(kind);
            }
            return;
        }
        phase('peergone', peerIdStr(p.key));
    };

    const wireChannel = (p, ch, rel) => {
        ch.binaryType = 'arraybuffer';
        ch.onmessage = (ev) => {
            const u8 = new Uint8Array(ev.data);
            if (rel && u8.length === 1 && u8[0] === 0) { peerGone(p, 'bye'); return; } // JS-level bye frame
            invoke('rtcData', b64FromBuf(ev.data), rel, peerIdStr(p.key));
        };
        ch.onopen = () => {
            if (p.connected || finished) return;
            if (p.chS && p.chS.readyState === 'open' && p.chR && p.chR.readyState === 'open') {
                p.connected = true;
                if (p.connectTimer) { clearTimeout(p.connectTimer); p.connectTimer = null; }
                // For roomMax 2 the first peer IS full, so this is exactly the old "first
                // connect -> stop beating, drop the listing, close the ws". A bigger host
                // NEVER closes it (card 0257f8ba) -- not even while momentarily full: seats
                // free again when a peer leaves mid-match, and a closed ws would mean the
                // server room (and its code) died with the third arrival. The joiner side is
                // unchanged: its ws has done its whole job once P2P is up.
                if (!isHost || (roomMax <= 2 && connectedCount() >= roomMax - 1)) signalingDone();
                phase('connected', isHost && roomMax > 2 ? peerIdStr(p.key) : '');
            }
        };
        ch.onclose = () => {
            if (!finished && p.connected) peerGone(p, 'channel');
        };
    };

    const makePc = (p) => {
        p.pc = new RTCPeerConnection({ iceServers: STUN });
        p.pc.onicecandidate = (ev) => {
            if (!ev.candidate) return;
            // The host addresses each joiner; a joiner only ever talks to the host, so its
            // outbound frames keep exactly the shipped shape (no `to` -- and an old server
            // relays the host's `to` along verbatim, where the old joiner ignores it).
            if (isHost) sendSignal({ t: 'ice', c: ev.candidate.toJSON(), to: p.key });
            else sendSignal({ t: 'ice', c: ev.candidate.toJSON() });
        };
        p.pc.onconnectionstatechange = () => {
            if (p.pc && (p.pc.connectionState === 'failed' || p.pc.connectionState === 'disconnected') && !p.connected) peerGone(p, 'ice');
        };
        // No TURN in v1: symmetric-NAT pairs (~10-15%) never complete ICE -- surface a
        // clean "could not connect" instead of hanging forever.
        p.connectTimer = setTimeout(() => { if (!p.connected) peerGone(p, 'timeout'); }, CONNECT_TIMEOUT_MS);
        window.addEventListener('pagehide', bye);
    };

    const newPeer = (key) => {
        const p = { key, pc: null, chS: null, chR: null, connected: false, connectTimer: null };
        peers.set(key, p);
        return p;
    };

    const startAsHost = async (p) => {
        makePc(p);
        p.chS = p.pc.createDataChannel('s', { ordered: false, maxRetransmits: 0 });
        p.chR = p.pc.createDataChannel('r');
        wireChannel(p, p.chS, false);
        wireChannel(p, p.chR, true);
        const offer = await p.pc.createOffer();
        await p.pc.setLocalDescription(offer);
        sendSignal({ t: 'sdp', d: p.pc.localDescription, to: p.key });
    };

    const startAsJoiner = (p) => {
        makePc(p);
        p.pc.ondatachannel = (ev) => {
            if (ev.channel.label === 's') { p.chS = ev.channel; wireChannel(p, p.chS, false); }
            else if (ev.channel.label === 'r') { p.chR = ev.channel; wireChannel(p, p.chR, true); }
        };
    };

    // Which peer does an inbound relayed frame belong to? The joiner has exactly one (the
    // host). The host reads the server's `from` tag; absent (old server, or a 2-room's
    // verbatim relay) there is only one joiner it can mean.
    const routeInbound = (m) => {
        if (!isHost) return peers.get(HOST_KEY) || null;
        const key = Number.isInteger(m.from) ? m.from : soleJoinerKey();
        return key === null ? null : (peers.get(key) || null);
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
            else if (m.t === 'peer') {
                if (isHost) {
                    // The server tags each arrival with its joiner id; an old server sends none
                    // and only ever delivers one arrival, so default to 1.
                    const key = Number.isInteger(m.id) ? m.id : 1;
                    // Capacity race guard: the server's seats only bound concurrent PAIRING
                    // attempts (a joiner vacates its seat on connect), so an arrival can land
                    // here while the map already holds roomMax-1 peers -- e.g. a 4th joiner
                    // racing the 3rd's channels opening. No offer = it times out server-side;
                    // there is no decline frame to send it.
                    if (peers.size >= roomMax - 1) {
                        phase('peerover', String(key));
                        return;
                    }
                    phase('peer', roomMax > 2 ? String(key) : '');
                    await startAsHost(newPeer(key));
                } else {
                    phase('peer');
                    startAsJoiner(newPeer(HOST_KEY));
                }
            }
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
                const p = routeInbound(m);
                if (!p || !p.pc) return;
                await p.pc.setRemoteDescription(m.d);
                if (!isHost) {
                    const answer = await p.pc.createAnswer();
                    await p.pc.setLocalDescription(answer);
                    sendSignal({ t: 'sdp', d: p.pc.localDescription });
                }
            }
            else if (m.t === 'ice') { const p = routeInbound(m); if (p && p.pc) await p.pc.addIceCandidate(m.c); }
            else if (m.t === 'gone') {
                // With an id: that joiner's SIGNALING seat freed (bigger rooms only). A joiner
                // deliberately closes its ws the moment P2P is up -- the shipped 2-room flow --
                // so post-connect this is EXPECTED and the live link's own liveness (bye frame,
                // channel close, the C# stream timeout) governs; acting on it here killed every
                // freshly-connected peer (measured on the 3-tab rig). Pre-connect it means the
                // joiner really is gone, so the pending pc is torn down. Bare: the whole room
                // died (host drop / 2-room teardown); pre-P2P that is terminal exactly as before.
                if (Number.isInteger(m.id)) {
                    const p = peers.get(m.id);
                    if (p && !p.connected) peerGone(p, 'gone');
                }
                else if (!anyConnected()) fail('gone');
            }
            else if (m.t === 'error') {
                // Terminal only while nothing is connected (the shipped rule -- and in shipped
                // flows the ws is closed post-connect, so this branch was unreachable then). An
                // N-host keeps its ws open, and a server complaint about signaling -- e.g. a
                // relay frame that raced a vacated seat -- must not tear down live P2P links.
                if (!anyConnected()) fail(m.reason || 'server');
                else console.warn('[rtc] server error ignored post-connect: ' + (m.reason || 'server'));
            }
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
        ws.onerror = () => { if (!anyConnected()) fail('signal'); };
        ws.onclose = () => { if (!anyConnected() && !finished) fail('signal'); };
    };

    return {
        // `max` (card 583a3ef8): total machines including us, clamped 2..4, default 2 --
        // sent to the server so an old client's room can never admit more than its build
        // handles. Values above 2 are console-rig territory until the session layer is
        // N-peer (plans/4p-online-coop.md, 11.9).
        host(signalUrl, max) {
            if (ws || peers.size) return;
            isHost = true;
            roomMax = Math.max(2, Math.min(4, Number(max) || 2));
            phase('contacting');
            openSignaling(signalUrl, () => sendSignal({ t: 'host', max: roomMax }));
        },
        join(signalUrl, code) {
            if (ws || peers.size) return;
            isHost = false;
            roomMax = 2;
            phase('contacting');
            openSignaling(signalUrl, () => sendSignal({ t: 'join', code: String(code || '') }));
        },
        send(b64, rel) {
            const bytes = bufFromB64(b64);
            for (const p of peers.values()) {
                chanSend(rel ? p.chR : p.chS, rel, bytes);
            }
        },
        // Addressed send: peerKey is the same string C# saw as the senderId ("1".."3" on the
        // host, "h" on a joiner). Unknown/departed peer or closed channel = silent drop, the
        // INetTransport contract.
        sendTo(peerKey, b64, rel) {
            const key = isHost ? Number(peerKey) : (peerKey === 'h' ? HOST_KEY : NaN);
            const p = peers.get(key);
            if (!p) return;
            chanSend(rel ? p.chR : p.chS, rel, bufFromB64(b64));
        },
        // Back-pressure diagnostics (card 6fb406bc): drops + per-lane bufferedAmount high-water
        // marks since page load. Console-read like the FPS HUD's stats; nothing C# depends on it.
        netStats() {
            return { streamDropped: netStats.streamDropped, streamPeak: netStats.streamPeak, relPeak: netStats.relPeak };
        },
        // Self-test in the eaFps.test idiom (card 6fb406bc): the JS layer has no headless
        // runner, so the send gate is a pure function driven over FAKE channel objects here --
        // callable from any boot's console, no session needed. Prints PASS/FAIL per leg and
        // returns true iff all held.
        testBackpressure() {
            const mk = (state, buffered) => {
                const c = { readyState: state, bufferedAmount: buffered, sent: 0, send() { this.sent++; } };
                return c;
            };
            const base = { d: netStats.streamDropped, p: netStats.streamPeak, r: netStats.relPeak };
            let pass = 0, fail = 0;
            const leg = (what, ok) => { console.log('[rtc] ' + (ok ? 'PASS' : 'FAIL') + ' ' + what); if (ok) pass++; else fail++; };
            let ch = mk('open', 0);
            chanSend(ch, false, new Uint8Array(4));
            leg('stream under the limit sends', ch.sent === 1 && netStats.streamDropped === base.d);
            ch = mk('open', STREAM_BUF_LIMIT + 1);
            chanSend(ch, false, new Uint8Array(4));
            leg('stream over the limit drops and counts', ch.sent === 0 && netStats.streamDropped === base.d + 1);
            leg('...and the stream peak recorded it', netStats.streamPeak >= STREAM_BUF_LIMIT + 1);
            ch = mk('open', STREAM_BUF_LIMIT + 1);
            chanSend(ch, true, new Uint8Array(4));
            leg('reliable NEVER drops, whatever the backlog', ch.sent === 1 && netStats.streamDropped === base.d + 1);
            leg('...and the reliable peak recorded it', netStats.relPeak >= STREAM_BUF_LIMIT + 1);
            ch = mk('closed', 0);
            chanSend(ch, false, new Uint8Array(4));
            leg('a closed channel is a silent no-op, not a drop count', ch.sent === 0 && netStats.streamDropped === base.d + 1);
            // Leave-no-trace on the counters a REAL run reads: the peaks are high-water marks a
            // fake channel legitimately raised, but the drop count must go back to describing
            // real traffic only.
            netStats.streamDropped = base.d;
            netStats.streamPeak = base.p;
            netStats.relPeak = base.r;
            console.log('[rtc] backpressure self-test: ' + pass + ' passed, ' + fail + ' failed');
            return fail === 0;
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
            if (ws || peers.size) {
                // Signaling socket already up. Since card 0257f8ba a HOST with a live room can
                // ADOPT it as a listing -- a menu-lobby game mid-level with a free seat starts
                // advertising (and beating: the lobby flow never armed one, and the room's TTL
                // counts from the last beat) on the socket it already has, so the SAME code the
                // friends joined by is what strangers find in the browser. Anything else --
                // a joiner, a session whose ws already closed -- stays the metadata-update
                // path it always was.
                if (!listing && isHost && ws && ws.readyState === WebSocket.OPEN && !finished) {
                    listing = true;
                    sendList();
                    if (!beatTimer) beatTimer = setInterval(() => sendSignal({ t: 'beat' }), BEAT_MS);
                    return;
                }
                if (listing) sendList();
                return;
            }
            listing = true;
            isHost = true;
            roomMax = LIST_ROOM_MAX;   // card 0257f8ba: a listed room takes up to 3 strangers
            phase('contacting');
            openSignaling(signalUrl, () => sendSignal({ t: 'host', max: roomMax }));
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
            if (!anyConnected() && peers.size === 0 && ws) { try { ws.onclose = null; ws.close(); } catch (e) { } ws = null; }
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
