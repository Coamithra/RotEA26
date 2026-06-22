/* ===========================================================================
   MERIDIAN WORKSPACE  —  the fake corporate desktop "boss key" decoy.

   A self-contained, dependency-free desktop OS shell. The game navigates here
   when the player picks "Exit" (see ../index.html eaQuit). Nothing here touches
   the game; "Shut Down" in the Start menu navigates back to it ("../").

   Sections:  icons -> data -> boot -> dialogs/toasts -> windows -> drag
              -> taskbar/start -> desktop -> apps (Files/Sheets/Docs/Mail/Bin)
              -> ambient corporate nags.
   =========================================================================== */
(function () {
    "use strict";

    /* ---------------------------------------------------------------- utils */
    const $ = (sel, root) => (root || document).querySelector(sel);
    const RETURN_TO_GAME = new URL("../", window.location.href).href;

    function el(tag, props, kids) {
        const n = document.createElement(tag);
        if (props) for (const k in props) {
            if (k === "class") n.className = props[k];
            else if (k === "html") n.innerHTML = props[k];
            else if (k === "text") n.textContent = props[k];
            else if (k.startsWith("on") && typeof props[k] === "function") n.addEventListener(k.slice(2), props[k]);
            else if (k === "style" && typeof props[k] === "object") Object.assign(n.style, props[k]);
            else if (props[k] != null) n.setAttribute(k, props[k]);
        }
        if (kids != null) (Array.isArray(kids) ? kids : [kids]).forEach(c => {
            if (c == null) return;
            n.appendChild(typeof c === "string" ? document.createTextNode(c) : c);
        });
        return n;
    }

    /* ---------------------------------------------------------------- icons */
    const tile = (bg, inner) => `<svg class="wico" viewBox="0 0 24 24"><rect width="24" height="24" rx="5" fill="${bg}"/>${inner}</svg>`;

    const ICO = {
        files: () => tile("#d99b12", '<path d="M5 8h4l1.4 1.8H19V17a1 1 0 0 1-1 1H6a1 1 0 0 1-1-1z" fill="#fff"/>'),
        sheets: () => tile("#1e7f4f", '<g fill="none" stroke="#fff" stroke-width="1.4"><rect x="5.5" y="6.5" width="13" height="11" rx="1"/><path d="M5.5 10h13M5.5 13.7h13M10 6.5v11M14 6.5v11"/></g>'),
        docs: () => tile("#2b5fb0", '<rect x="6" y="5" width="12" height="14" rx="1.3" fill="#fff"/><g stroke="#2b5fb0" stroke-width="1.2" stroke-linecap="round"><path d="M8.6 9h6.8M8.6 12h6.8M8.6 15h4.4"/></g>'),
        mail: () => tile("#0d8a8a", '<g fill="none" stroke="#fff" stroke-width="1.5" stroke-linejoin="round"><rect x="5" y="7" width="14" height="10" rx="1.5"/><path d="M5.4 8l6.6 5 6.6-5"/></g>'),
        bin: () => tile("#6b7280", '<g fill="#fff"><path d="M9.5 5h5l.8 1.6H18V8H6V6.6h2.7z"/><path d="M7.2 9h9.6l-.8 9.4a1.2 1.2 0 0 1-1.2 1.1H9.2a1.2 1.2 0 0 1-1.2-1.1z"/></g>'),
        binFull: () => tile("#6b7280", '<g fill="#fff"><path d="M9.5 5h5l.8 1.6H18V8H6V6.6h2.7z"/><path d="M7.2 9h9.6l-.8 9.4a1.2 1.2 0 0 1-1.2 1.1H9.2a1.2 1.2 0 0 1-1.2-1.1z"/></g><g fill="#3aa655"><rect x="9" y="2.4" width="6" height="3" rx="1"/></g>'),
        games: () => tile("#6d4bd1", '<path d="M8 9.5h8a3.4 3.4 0 0 1 3.3 4.2l-.5 2a2.1 2.1 0 0 1-3.8.5L14.2 15H9.8l-1 1.2a2.1 2.1 0 0 1-3.8-.5l-.5-2A3.4 3.4 0 0 1 8 9.5z" fill="#fff"/><g fill="#6d4bd1"><rect x="6.6" y="12" width="3" height="1" rx=".5"/><rect x="7.6" y="11" width="1" height="3" rx=".5"/><circle cx="15.4" cy="11.6" r=".9"/><circle cx="16.8" cy="13" r=".9"/></g>'),
        note: () => tile("#6b7280", '<rect x="6" y="5" width="12" height="14" rx="1.3" fill="#fff"/><g stroke="#6b7280" stroke-width="1.2" stroke-linecap="round"><path d="M8.6 9h6.8M8.6 12h6.8M8.6 15h4.4"/></g>'),
    };

    // big desktop versions (transparent, drop-shadowed by CSS)
    const BIGICO = {
        files: `<svg class="fico" viewBox="0 0 48 42"><path d="M5 9a3 3 0 0 1 3-3h9.5l4 5H40a3 3 0 0 1 3 3v3H5z" fill="#b9810f"/><path d="M5 13h38v18a3 3 0 0 1-3 3H8a3 3 0 0 1-3-3z" fill="#edb12c"/></svg>`,
        sheets: `<svg class="fico" viewBox="0 0 40 48"><path d="M6 2h20l8 8v34a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2z" fill="#fff" stroke="#cfd6e0" stroke-width="1.4"/><path d="M26 2l8 8h-8z" fill="#e6ebf2"/><rect x="9" y="20" width="22" height="18" rx="1.5" fill="none" stroke="#1e7f4f" stroke-width="1.6"/><path d="M9 26h22M9 32h22M16 20v18M23 20v18" stroke="#1e7f4f" stroke-width="1.2"/></svg>`,
        docs: `<svg class="fico" viewBox="0 0 40 48"><path d="M6 2h20l8 8v34a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2z" fill="#fff" stroke="#cfd6e0" stroke-width="1.4"/><path d="M26 2l8 8h-8z" fill="#e6ebf2"/><g stroke="#2b5fb0" stroke-width="1.7" stroke-linecap="round"><path d="M11 21h18M11 27h18M11 33h12"/></g></svg>`,
        mail: `<svg class="fico" viewBox="0 0 48 42"><rect x="4" y="6" width="40" height="30" rx="3" fill="#fff" stroke="#cfd6e0" stroke-width="1.4"/><path d="M5 9l19 14L43 9" fill="none" stroke="#0d8a8a" stroke-width="2.4" stroke-linejoin="round"/></svg>`,
        bin: `<svg class="fico" viewBox="0 0 44 48"><path d="M14 6l2-3h12l2 3h7v4H7V6z" fill="#8a929e"/><path d="M9 12h26l-2.2 31A3 3 0 0 1 29.8 46H14.2A3 3 0 0 1 11.2 43z" fill="#aeb6c2"/><g stroke="#7a828e" stroke-width="1.6"><path d="M18 18v22M22 18v22M26 18v22"/></g></svg>`,
    };
    BIGICO.binFull = `<svg class="fico" viewBox="0 0 44 48"><path d="M14 6l2-3h12l2 3h7v4H7V6z" fill="#8a929e"/><path d="M9 12h26l-2.2 31A3 3 0 0 1 29.8 46H14.2A3 3 0 0 1 11.2 43z" fill="#aeb6c2"/><g stroke="#7a828e" stroke-width="1.6"><path d="M18 18v22M22 18v22M26 18v22"/></g><path d="M13 12l4-7h10l4 7z" fill="#3aa655" opacity=".92"/></svg>`;
    // "Games" — a folder (purple) with a game-controller badge.
    BIGICO.games = `<svg class="fico" viewBox="0 0 48 42"><path d="M5 9a3 3 0 0 1 3-3h9.5l4 5H40a3 3 0 0 1 3 3v3H5z" fill="#5a3bb8"/><path d="M5 13h38v19a3 3 0 0 1-3 3H8a3 3 0 0 1-3-3z" fill="#7e5cdb"/><path d="M17 21h14a4.2 4.2 0 0 1 4.1 5.1l-.7 3a2.5 2.5 0 0 1-4.5.6L28.6 28h-9.2l-1.3 1.7a2.5 2.5 0 0 1-4.5-.6l-.7-3A4.2 4.2 0 0 1 17 21z" fill="#fff"/><g fill="#7e5cdb"><rect x="17.5" y="24.4" width="4" height="1.3" rx=".6"/><rect x="18.85" y="23.05" width="1.3" height="4" rx=".6"/><circle cx="29.2" cy="24" r="1.1"/><circle cx="31" cy="25.6" r="1.1"/></g></svg>`;

    // file-type document icon (colored label band)
    function docIcon(color, label) {
        return `<svg class="fico" viewBox="0 0 40 48"><path d="M6 2h20l8 8v34a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2z" fill="#fff" stroke="#cfd6e0" stroke-width="1.4"/><path d="M26 2l8 8h-8z" fill="#e6ebf2"/><rect x="3" y="28" width="34" height="13" rx="2" fill="${color}"/><text x="20" y="37.4" text-anchor="middle" font-size="8.4" font-weight="700" fill="#fff" font-family="Segoe UI, system-ui, sans-serif">${label}</text></svg>`;
    }

    const EXT = {
        xlsx: { color: "#1e7f4f", label: "XLS", app: "sheets" },
        docx: { color: "#2b5fb0", label: "DOC", app: "docs" },
        pdf: { color: "#c0392b", label: "PDF" },
        pptx: { color: "#c25510", label: "PPT" },
        txt: { color: "#6b7280", label: "TXT" },
        png: { color: "#7a5bb0", label: "IMG" },
        zip: { color: "#9a7b1f", label: "ZIP" },
        csv: { color: "#1e7f4f", label: "CSV" },
    };
    const fileGlyph = ext => { const e = EXT[ext] || { color: "#6b7280", label: (ext || "FILE").toUpperCase().slice(0, 3) }; return docIcon(e.color, e.label); };

    // status / dialog glyphs (40px)
    const STATUS = {
        info: '<svg class="dlg-ico" viewBox="0 0 40 40"><circle cx="20" cy="20" r="18" fill="#2563a8"/><circle cx="20" cy="12.5" r="2.4" fill="#fff"/><rect x="17.6" y="17" width="4.8" height="13" rx="2.4" fill="#fff"/></svg>',
        question: '<svg class="dlg-ico" viewBox="0 0 40 40"><circle cx="20" cy="20" r="18" fill="#2563a8"/><path d="M15.5 15.5a4.5 4.5 0 1 1 6.2 4.2c-1.4.6-1.9 1.3-1.9 2.7v1" fill="none" stroke="#fff" stroke-width="2.6" stroke-linecap="round"/><circle cx="20" cy="29" r="2.2" fill="#fff"/></svg>',
        warn: '<svg class="dlg-ico" viewBox="0 0 40 40"><path d="M20 3l18 32H2z" fill="#e2a017"/><rect x="17.7" y="14" width="4.6" height="12" rx="2.3" fill="#fff"/><circle cx="20" cy="30" r="2.4" fill="#fff"/></svg>',
        error: '<svg class="dlg-ico" viewBox="0 0 40 40"><circle cx="20" cy="20" r="18" fill="#c0392b"/><path d="M14 14l12 12M26 14L14 26" stroke="#fff" stroke-width="3" stroke-linecap="round"/></svg>',
        ok: '<svg class="dlg-ico" viewBox="0 0 40 40"><circle cx="20" cy="20" r="18" fill="#1e7f4f"/><path d="M12 20.5l5 5 11-11" fill="none" stroke="#fff" stroke-width="3.2" stroke-linecap="round" stroke-linejoin="round"/></svg>',
    };
    const TOASTICO = {
        info: '<svg class="ti" viewBox="0 0 24 24"><circle cx="12" cy="12" r="11" fill="#2563a8"/><circle cx="12" cy="7.5" r="1.5" fill="#fff"/><rect x="10.6" y="10.5" width="2.8" height="8" rx="1.4" fill="#fff"/></svg>',
        ok: '<svg class="ti" viewBox="0 0 24 24"><circle cx="12" cy="12" r="11" fill="#1e7f4f"/><path d="M7 12.5l3 3 7-7" fill="none" stroke="#fff" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"/></svg>',
        warn: '<svg class="ti" viewBox="0 0 24 24"><path d="M12 2l11 19H1z" fill="#e2a017"/><rect x="10.7" y="9" width="2.6" height="7" rx="1.3" fill="#fff"/><circle cx="12" cy="18.5" r="1.4" fill="#fff"/></svg>',
        danger: '<svg class="ti" viewBox="0 0 24 24"><circle cx="12" cy="12" r="11" fill="#c0392b"/><path d="M8 8l8 8M16 8l-8 8" stroke="#fff" stroke-width="2" stroke-linecap="round"/></svg>',
    };
    const winCtl = {
        min: '<svg viewBox="0 0 12 12"><path d="M2 9h8" stroke="currentColor" stroke-width="1.4"/></svg>',
        max: '<svg viewBox="0 0 12 12"><rect x="2.2" y="2.2" width="7.6" height="7.6" fill="none" stroke="currentColor" stroke-width="1.3"/></svg>',
        restore: '<svg viewBox="0 0 12 12"><rect x="3.4" y="1.6" width="6.4" height="6.4" fill="none" stroke="currentColor" stroke-width="1.2"/><rect x="1.6" y="3.4" width="6.4" height="6.4" fill="#fff" stroke="currentColor" stroke-width="1.2"/></svg>',
        close: '<svg viewBox="0 0 12 12"><path d="M2.5 2.5l7 7M9.5 2.5l-7 7" stroke="currentColor" stroke-width="1.4" stroke-linecap="round"/></svg>',
    };

    /* ----------------------------------------------------------------- data */
    let fileSeq = 1;
    const mkFile = (name, ext, extra) => Object.assign({ id: "f" + (fileSeq++), kind: "file", name, ext }, extra);

    const folders = {
        documents: {
            name: "Documents", parent: null, items: [
                mkFile("Q3_Financials", "xlsx"),
                mkFile("Strategic_Synergy_Memo", "docx"),
                mkFile("Board_Deck_FINAL_v7", "pptx"),
                mkFile("2026_Operating_Budget", "xlsx"),
                mkFile("Performance_Reviews_CONFIDENTIAL", "pdf"),
                mkFile("Headcount_Plan", "csv"),
                { kind: "folder", to: "reports", name: "Reports" },
                { kind: "folder", to: "archive", name: "Archive" },
            ]
        },
        reports: {
            name: "Reports", parent: "documents", items: [
                mkFile("Weekly_Status_Report", "docx"),
                mkFile("KPI_Dashboard", "xlsx"),
                mkFile("Market_Analysis_2026", "pdf"),
                mkFile("Roadmap_Q4", "pptx"),
            ]
        },
        archive: {
            name: "Archive", parent: "documents", items: [
                mkFile("Old_Invoices_2024", "zip"),
                mkFile("Legacy_Spreadsheet", "xlsx"),
                mkFile("logo_draft_v3", "png"),
            ]
        },
    };
    const RESIGNATION_LETTER =
`DRAFT 4 — do NOT send until I have slept on it

Dear Jordan,

Please accept this letter as my formal resignation from the position of
Regional Synergy Lead at Meridian Dynamics, effective two weeks from today.

After 6 years, 11 reorgs, 4 "new" mission statements and roughly 1,900 hours
of mandatory Synergy Syncs, I have decided to pursue an opportunity that does
not require me to "circle back," "double-click," or "take this offline" ever
again.

I am deeply grateful for everything I have learned here, chiefly that a
12-minute coffee break is, in fact, logged.

----------------------------------------------------------------------
[draft 3 — too honest, tone down before sending]
Jordan. I quit. I am going to go outside. The sun is real. Synergy is not.
----------------------------------------------------------------------
[draft 2 — closer?]
To whom it may concern: I regret to inform you that I no longer regret
anything.
----------------------------------------------------------------------

Sincerely,
Alex Morgan

P.S. — I have NOT sent this. I closed the lid, told myself "Monday," and
dragged it to the Recycle Bin instead. It is still Monday somewhere. It is
always Monday here.`;

    const MEETING_NOTES =
        "SYNERGY SYNC — notes\r\n\r\n" +
        "- discussed the alignment doc\r\n" +
        "- aligned on aligning the alignment\r\n" +
        "- ACTION: socialize the framework, circle back\r\n" +
        "- ACTION: take the other thing offline\r\n" +
        "- next steps: define \"next steps\"\r\n\r\n" +
        "(meeting ran 50 min over. nobody knows why we met.)\r\n";

    // A resignation-adjacent email Alex actually addressed to their manager, then
    // chickened out of and binned. Restored, it lands in Mail → Drafts where it can
    // be deleted again — or sent, which has consequences (see scheduleIncomingCall).
    const DRAFT_EMAIL = {
        to: "Jordan Ellis", toAddr: "jordan.ellis@meridian-dynamics.com",
        subj: "RE: quick sync?",
        body: "<p>Hi Jordan,</p><p>Thanks for the invite to align on the alignment doc before we align with the broader alignment group. Honest answer: there is no sync short enough.</p><p>I've gone ahead and blocked my calendar for the next four to six business years. If something is genuinely urgent it can go in a document, which I will then move &mdash; unread &mdash; to a folder named &ldquo;Reviewed.&rdquo;</p><p>Warmest possible regards,<br>Alex</p>",
    };

    // Recycle Bin entries are either { file, fromFolder, fromName } or, for a deleted
    // email, { kind:"mail", mail, fromName }. Pre-seeded with everything Alex thought
    // better of and quietly deleted: the resignation letter, the meeting notes, the email.
    const recycleBin = [
        { file: mkFile("Resignation_Letter_DRAFT", "txt", { text: RESIGNATION_LETTER }), fromFolder: "documents", fromName: "Documents" },
        { file: mkFile("Meeting_Notes", "txt", { text: MEETING_NOTES }), fromFolder: "documents", fromName: "Documents" },
        { kind: "mail", mail: DRAFT_EMAIL, fromName: "Mail" },
    ];
    const drafts = [];          // emails restored from the bin live here (Mail → Drafts)
    let mailRefresh = null;     // set while a Mail window is open, so restores re-render it

    /* --------------------------------------------------- boot sequence ----- */
    const boot = $("#boot"), os = $("#os");
    const bootMsgs = [
        "Starting your session…",
        "Loading user profile…",
        "Connecting to Meridian Cloud…",
        "Synchronizing OneVault…",
        "Applying group policy…",
        "Preparing your workspace…",
    ];
    let bm = 0;
    const bootTimer = setInterval(() => {
        bm++;
        if (bm < bootMsgs.length) $("#bootStatus").textContent = bootMsgs[bm];
    }, 360);
    setTimeout(() => {
        clearInterval(bootTimer);
        boot.classList.add("fade");
        os.hidden = false;
        requestAnimationFrame(() => os.classList.add("shown"));
        setTimeout(() => boot.remove(), 600);
        buildDesktop();
        scheduleNags();
    }, 2300);

    /* ----------------------------------------------------- dialogs/toasts -- */
    const dlgLayer = $("#dialog-layer");

    // showDialog(opt) -> Promise(value). Dialogs are SERIALIZED through a queue so a
    // timed nag can never stomp a modal the user is part-way through (which would orphan
    // the first dialog's promise — e.g. leave an app stuck behind a dead EULA). When no
    // dialog is open, pumpDialog runs synchronously, so callers that read .dlg-progress
    // right after showDialog still find it.
    let dlgQueue = [], dlgActive = false;
    function showDialog(opt) {
        return new Promise(resolve => {
            dlgQueue.push({ opt, resolve });
            if (!dlgActive) pumpDialog();
        });
    }
    function pumpDialog() {
        if (!dlgQueue.length) { dlgActive = false; dlgLayer.classList.add("hidden"); dlgLayer.innerHTML = ""; return; }
        dlgActive = true;
        const job = dlgQueue.shift(), opt = job.opt;
        const icon = STATUS[opt.icon || "info"] || STATUS.info;
        const titleIco = ICO[opt.titleIcon] ? ICO[opt.titleIcon]() : ICO.files();
        const body = el("div", { class: "dialog", role: "dialog", "aria-modal": "true" });
        body.innerHTML =
            `<div class="dlg-title">${opt.titleIcon === false ? "" : titleIco}<span>${opt.title || "Meridian Workspace"}</span></div>` +
            `<div class="dlg-body">${opt.icon === null ? "" : icon}<div class="dlg-text">${opt.html || ""}</div></div>` +
            (opt.check ? `<label class="dlg-check"><input type="checkbox"> ${opt.check}</label>` : "");
        const btnRow = el("div", { class: "dlg-buttons" });
        (opt.buttons || [{ label: "OK", value: "ok", primary: true }]).forEach(b => {
            const btn = el("button", {
                class: "btn" + (b.primary ? " primary" : "") + (b.kind === "danger" ? " danger" : ""),
                text: b.label,
                onclick: () => done(b.value)
            });
            if (b.autofocus || b.primary) setTimeout(() => btn.focus(), 30);
            btnRow.appendChild(btn);
        });
        body.appendChild(btnRow);
        dlgLayer.innerHTML = "";
        dlgLayer.appendChild(body);
        dlgLayer.classList.remove("hidden");
        document.addEventListener("keydown", onKey);
        function onKey(e) {
            if (e.key === "Escape") done(opt.escValue != null ? opt.escValue : null);
            else if (e.key === "Enter") { const p = btnRow.querySelector(".primary") || btnRow.lastChild; p && p.click(); }
        }
        function done(val) {
            document.removeEventListener("keydown", onKey);
            job.resolve(val);
            pumpDialog();   // render the next queued dialog, or hide the layer
        }
    }

    const toastLayer = $("#toast-layer");
    function toast(opt) {
        const t = el("div", { class: "toast " + (opt.kind || "info") });
        t.innerHTML =
            (TOASTICO[opt.kind] || TOASTICO.info) +
            `<div class="tt"><div class="h">${opt.title || ""}</div><div class="b">${opt.body || ""}</div></div>`;
        const x = el("button", { class: "tx", html: "&times;", onclick: () => dismiss() });
        t.appendChild(x);
        if (opt.action) {
            const a = el("button", { class: "btn primary", style: { minWidth: "auto", padding: "5px 12px", fontSize: "12px", marginLeft: "8px" }, text: opt.action.label, onclick: () => { dismiss(); opt.action.fn(); } });
            t.querySelector(".tt").appendChild(a);
        }
        toastLayer.appendChild(t);
        const ttl = opt.sticky ? 0 : (opt.ttl || 6000);
        let to = ttl ? setTimeout(dismiss, ttl) : 0;
        function dismiss() { if (!t.parentNode) return; clearTimeout(to); t.classList.add("out"); setTimeout(() => t.remove(), 260); }
        return t;
    }

    /* ------------------------------------------------------ window manager -- */
    const winLayer = $("#window-layer"), taskApps = () => $("#task-apps");
    const wins = new Map();   // id -> winObj
    let zTop = 20, activeId = null, cascade = 0;

    function focusWin(id) {
        activeId = id;
        wins.forEach((w, k) => {
            const on = k === id;
            w.el.classList.toggle("inactive", !on);
            if (on) w.el.style.zIndex = (++zTop);
            if (w.taskBtn) w.taskBtn.classList.toggle("active", on && !w.minimized);
        });
    }

    function openWindow(spec) {
        if (wins.has(spec.id)) { const w = wins.get(spec.id); if (w.minimized) toggleMin(spec.id); focusWin(spec.id); return w; }

        const win = el("div", { class: "win inactive" });
        const w = Math.min(spec.width || 720, innerWidth - 24);
        const h = Math.min(spec.height || 480, innerHeight - 70);
        const x = spec.x != null ? spec.x : Math.max(12, Math.round((innerWidth - w) / 2) + (cascade % 6) * 26 - 60);
        const y = spec.y != null ? spec.y : Math.max(10, Math.round((innerHeight - 46 - h) / 2) + (cascade % 6) * 22 - 50);
        cascade++;
        Object.assign(win.style, { left: x + "px", top: y + "px", width: w + "px", height: h + "px" });

        const title = el("div", { class: "win-title" });
        title.innerHTML = `${spec.icon ? spec.icon() : ICO.files()}<span class="wname">${spec.title}</span>`;
        const btns = el("div", { class: "win-btns" });
        const bMin = el("button", { class: "win-btn", title: "Minimize", html: winCtl.min, onclick: e => { e.stopPropagation(); toggleMin(spec.id); } });
        const bMax = el("button", { class: "win-btn", title: "Maximize", html: winCtl.max, onclick: e => { e.stopPropagation(); toggleMax(spec.id); } });
        const bCls = el("button", { class: "win-btn close", title: "Close", html: winCtl.close, onclick: e => { e.stopPropagation(); closeWin(spec.id); } });
        btns.append(bMin, bMax, bCls);
        title.appendChild(btns);

        const bodyEl = el("div", { class: "win-body" });
        win.append(title, bodyEl);
        win.addEventListener("pointerdown", () => focusWin(spec.id), true);
        winLayer.appendChild(win);

        // taskbar entry
        const taskBtn = el("button", { class: "task-app", title: spec.title });
        taskBtn.innerHTML = `${spec.icon ? spec.icon() : ICO.files()}<span class="t">${spec.title}</span>`;
        taskBtn.addEventListener("click", () => {
            const ww = wins.get(spec.id); if (!ww) return;
            if (ww.minimized) { toggleMin(spec.id); focusWin(spec.id); }
            else if (activeId === spec.id) toggleMin(spec.id);
            else focusWin(spec.id);
        });
        taskApps().appendChild(taskBtn);

        const wObj = { id: spec.id, el: win, bodyEl, taskBtn, bMax, minimized: false, maximized: false, restore: null, spec };
        wins.set(spec.id, wObj);
        makeWindowDraggable(wObj, title);

        spec.build(bodyEl, wObj);
        focusWin(spec.id);
        return wObj;
    }

    function toggleMin(id) {
        const w = wins.get(id); if (!w) return;
        w.minimized = !w.minimized;
        w.el.style.display = w.minimized ? "none" : "flex";
        w.taskBtn.classList.toggle("active", !w.minimized);
        if (w.minimized && activeId === id) activeId = null;
        if (!w.minimized) focusWin(id);
    }
    function toggleMax(id) {
        const w = wins.get(id); if (!w) return;
        if (!w.maximized) {
            w.restore = { left: w.el.style.left, top: w.el.style.top, width: w.el.style.width, height: w.el.style.height };
            Object.assign(w.el.style, { left: "0px", top: "0px", width: "100%", height: "calc(100% - var(--taskbar-h))" });
            w.el.classList.add("maxd"); w.bMax.innerHTML = winCtl.restore; w.bMax.title = "Restore";
        } else {
            Object.assign(w.el.style, w.restore);
            w.el.classList.remove("maxd"); w.bMax.innerHTML = winCtl.max; w.bMax.title = "Maximize";
        }
        w.maximized = !w.maximized;
    }
    function closeWin(id) {
        const w = wins.get(id); if (!w) return;
        if (w._onClose) w._onClose();
        w.el.remove(); w.taskBtn.remove(); wins.delete(id);
        if (activeId === id) activeId = null;
    }

    /* -------------------------------------------------------- drag system -- */
    const ghost = $("#drag-ghost");
    let curDrop = null;

    // Window drag by titlebar.
    function makeWindowDraggable(w, handle) {
        let sx, sy, ox, oy, moving = false;
        handle.addEventListener("pointerdown", e => {
            if (e.button !== 0 || e.target.closest(".win-btn")) return;
            if (w.maximized) return;
            sx = e.clientX; sy = e.clientY; ox = w.el.offsetLeft; oy = w.el.offsetTop; moving = true;
            handle.setPointerCapture(e.pointerId);
            e.preventDefault();
        });
        handle.addEventListener("pointermove", e => {
            if (!moving) return;
            let nx = ox + (e.clientX - sx), ny = oy + (e.clientY - sy);
            nx = Math.max(-w.el.offsetWidth + 80, Math.min(nx, innerWidth - 60));
            ny = Math.max(0, Math.min(ny, innerHeight - 46 - 36));
            w.el.style.left = nx + "px"; w.el.style.top = ny + "px";
        });
        const end = () => { moving = false; };
        handle.addEventListener("pointerup", end);
        handle.addEventListener("pointercancel", end);
        handle.addEventListener("dblclick", e => { if (!e.target.closest(".win-btn")) toggleMax(w.id); });
    }

    // Generic file/icon drag with drop-target hit testing.
    // dragSpec: { ghostHtml, label, payload }  -> dropzones read e.target.__drop
    function startItemDrag(e, sourceEl, dragSpec) {
        let sx = e.clientX, sy = e.clientY, started = false;
        const move = ev => {
            if (!started) {
                if (Math.abs(ev.clientX - sx) + Math.abs(ev.clientY - sy) < 6) return;
                started = true;
                sourceEl.classList.add("dragging");
                ghost.innerHTML = `<div style="display:flex;align-items:center;gap:8px;background:#fff;border:1px solid #c2cbd9;border-radius:8px;padding:6px 12px 6px 8px;font:13px var(--font);box-shadow:var(--shadow-pop)"><span style="width:24px;height:24px;display:inline-block">${dragSpec.ghostHtml}</span><span>${dragSpec.label}</span></div>`;
                ghost.hidden = false;
            }
            ghost.style.left = ev.clientX + "px"; ghost.style.top = ev.clientY + "px";
            const tgt = dropAt(ev.clientX, ev.clientY);
            if (tgt !== curDrop) {
                if (curDrop) curDrop.classList.remove("drop-ok");
                curDrop = tgt; if (curDrop) curDrop.classList.add("drop-ok");
            }
        };
        const up = ev => {
            window.removeEventListener("pointermove", move);
            window.removeEventListener("pointerup", up);
            ghost.hidden = true; sourceEl.classList.remove("dragging");
            if (curDrop) { curDrop.classList.remove("drop-ok"); }
            if (started && curDrop && curDrop.__drop) curDrop.__drop(dragSpec.payload, sourceEl);
            curDrop = null;
        };
        window.addEventListener("pointermove", move);
        window.addEventListener("pointerup", up);
    }
    function dropAt(x, y) {
        ghost.style.pointerEvents = "none";
        const stack = document.elementsFromPoint(x, y);
        for (const n of stack) { const z = n.closest && n.closest(".dropzone"); if (z) return z; }
        return null;
    }
    const makeDropzone = (node, handler) => { node.classList.add("dropzone"); node.__drop = handler; return node; };

    /* ------------------------------------------------------- taskbar/start - */
    function buildTaskbar() {
        const tb = $("#taskbar");
        const startBtn = el("button", { class: "start-btn", id: "startBtn" });
        startBtn.innerHTML = `<svg viewBox="0 0 24 24"><path d="M5 19V5l7 8 7-8v14" fill="none" stroke="#6ea8ec" stroke-width="2.4" stroke-linejoin="round" stroke-linecap="round"/></svg><span>Start</span>`;
        startBtn.addEventListener("click", e => { e.stopPropagation(); toggleStart(); });

        const apps = el("div", { class: "task-apps", id: "task-apps" });

        const tray = el("div", { class: "tray" });
        tray.innerHTML =
            `<div class="tray-ico" title="Search"><svg viewBox="0 0 24 24"><circle cx="11" cy="11" r="6.4" fill="none" stroke="currentColor" stroke-width="1.8"/><path d="M16 16l4 4" stroke="currentColor" stroke-width="1.8" stroke-linecap="round"/></svg></div>` +
            `<div class="tray-ico" title="Network: Connected"><svg viewBox="0 0 24 24"><path d="M2 8.5C7.5 3.5 16.5 3.5 22 8.5M5 12c4-3.4 10-3.4 14 0M8.5 15.5c2.1-1.8 4.9-1.8 7 0" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round"/><circle cx="12" cy="19" r="1.6" fill="currentColor"/></svg></div>` +
            `<div class="tray-ico" title="Volume"><svg viewBox="0 0 24 24"><path d="M4 9v6h4l5 4V5L8 9z" fill="currentColor"/><path d="M16 9c1.5 1.5 1.5 4.5 0 6" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round"/></svg></div>`;
        const bell = el("div", { class: "tray-ico", title: "Notifications", html: '<svg viewBox="0 0 24 24"><path d="M6 16V11a6 6 0 1 1 12 0v5l2 2H4z" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linejoin="round"/><path d="M10 20a2 2 0 0 0 4 0" fill="none" stroke="currentColor" stroke-width="1.7"/></svg>' });
        bell.addEventListener("click", () => toast({ kind: "info", title: "Notifications", body: "You're all caught up. Nice work today." }));
        tray.appendChild(bell);

        const clock = el("div", { class: "clock", id: "clock" });
        clock.addEventListener("click", () => toast({ kind: "info", title: "Calendar", body: "Next: <b>Q3 Synergy Sync</b> in Conf. Room B (in 1 hour)." }));
        tray.appendChild(clock);

        tb.append(startBtn, el("div", { class: "task-sep" }), apps, el("div", { class: "task-sep" }), tray);
        tickClock();
        setInterval(tickClock, 1000);
    }
    function tickClock() {
        const d = new Date();
        let h = d.getHours(), m = d.getMinutes();
        const ap = h >= 12 ? "PM" : "AM"; h = h % 12 || 12;
        const time = h + ":" + String(m).padStart(2, "0") + " " + ap;
        const date = d.toLocaleDateString(undefined, { month: "short", day: "numeric", year: "numeric" });
        const c = $("#clock"); if (c) c.innerHTML = `<div>${time}</div><div class="d">${date}</div>`;
    }

    const APPDEFS = [
        { key: "files", title: "Meridian Files", icon: ICO.files, open: openFiles },
        { key: "sheets", title: "Meridian Sheets", icon: ICO.sheets, open: openSheets },
        { key: "docs", title: "Meridian Docs", icon: ICO.docs, open: openDocs },
        { key: "mail", title: "Meridian Mail", icon: ICO.mail, open: openMail },
        { key: "games", title: "Games", icon: ICO.games, open: openGames },
        { key: "bin", title: "Recycle Bin", icon: () => (recycleBin.length ? ICO.binFull() : ICO.bin()), open: openBin },
    ];

    function toggleStart(force) {
        const sm = $("#startmenu"), btn = $("#startBtn");
        const show = force != null ? force : sm.classList.contains("hidden");
        sm.classList.toggle("hidden", !show);
        btn.classList.toggle("open", show);
    }
    function buildStartMenu() {
        const sm = $("#startmenu");
        const apps = el("div", { class: "sm-apps" });
        APPDEFS.forEach(a => {
            const b = el("button", { class: "sm-app", onclick: () => { toggleStart(false); a.open(); } });
            b.innerHTML = `${a.icon()}<span>${a.title}</span>`;
            apps.appendChild(b);
        });
        const foot = el("div", { class: "sm-foot" });
        foot.innerHTML = `<div class="sm-user"><div class="sm-avatar">AM</div><div>Alex Morgan<div style="font-size:11px;color:#8ba0c4">Regional Synergy Lead</div></div></div>`;
        const power = el("button", { class: "sm-power", title: "Power", html: '<svg viewBox="0 0 24 24"><path d="M12 3v9" stroke="currentColor" stroke-width="2" stroke-linecap="round"/><path d="M7 6.5a8 8 0 1 0 10 0" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"/></svg>', onclick: powerMenu });
        foot.appendChild(power);
        sm.innerHTML = `<div class="sm-head">Meridian Workspace — Pinned</div>`;
        sm.append(apps, foot);
        document.addEventListener("pointerdown", e => {
            if (!sm.classList.contains("hidden") && !e.target.closest("#startmenu") && !e.target.closest("#startBtn")) toggleStart(false);
        });
    }
    async function powerMenu() {
        toggleStart(false);
        const v = await showDialog({
            title: "Power options", titleIcon: false, icon: "question",
            html: "What would you like to do?<div class='sub'>Unsaved work in open applications may be lost.</div>",
            buttons: [
                { label: "Sign out", value: "out" },
                { label: "Restart", value: "restart" },
                { label: "Shut Down", value: "shutdown", primary: true },
                { label: "Cancel", value: null },
            ],
        });
        if (v === "shutdown" || v === "restart" || v === "out") shutdown(v);
    }

    /* ---------------------------------------------------------- desktop ---- */
    function buildDesktop() {
        buildTaskbar();
        buildStartMenu();
        const desk = $("#desktop");
        desk.addEventListener("pointerdown", e => {
            if (e.target === desk) { desk.querySelectorAll(".desk-icon.sel").forEach(n => n.classList.remove("sel")); toggleStart(false); }
        });

        const layout = [
            { app: "files", label: "Documents", big: BIGICO.files },
            { app: "games", label: "Games", big: BIGICO.games },
            { app: "sheets", label: "Meridian Sheets", big: BIGICO.sheets },
            { app: "docs", label: "Meridian Docs", big: BIGICO.docs },
            { app: "mail", label: "Meridian Mail", big: BIGICO.mail },
        ];
        layout.forEach((it, i) => desk.appendChild(makeDeskIcon(it.app, it.label, it.big, 18, 16 + i * 104)));

        // Recycle Bin lives bottom-right and is a drop target for deletions.
        const bin = makeDeskIcon("bin", "Recycle Bin", recycleBin.length ? BIGICO.binFull : BIGICO.bin, null, null);
        bin.id = "deskBin";
        bin.style.right = "26px"; bin.style.bottom = "26px"; bin.style.left = "auto"; bin.style.top = "auto";
        desk.appendChild(bin);
        makeDropzone(bin, (payload) => onDropToBin(payload));
        refreshBinIcon();
    }

    function makeDeskIcon(app, label, bigHtml, x, y) {
        const node = el("div", { class: "desk-icon", tabindex: "0" });
        if (x != null) node.style.left = x + "px";
        if (y != null) node.style.top = y + "px";
        node.innerHTML = `<div class="ico">${bigHtml}</div><div class="lbl">${label}</div>`;
        node.dataset.app = app;
        node.addEventListener("dblclick", () => APPDEFS.find(a => a.key === app).open());
        node.addEventListener("pointerdown", e => {
            if (e.button !== 0) return;
            $("#desktop").querySelectorAll(".desk-icon.sel").forEach(n => n.classList.remove("sel"));
            node.classList.add("sel");
            // drag to reposition; can be dropped on the bin (handled cheekily)
            startDeskDrag(e, node, app, label, bigHtml);
        });
        return node;
    }
    function startDeskDrag(e, node, app, label, bigHtml) {
        let sx = e.clientX, sy = e.clientY, ox = node.offsetLeft, oy = node.offsetTop, started = false;
        const move = ev => {
            if (!started && Math.abs(ev.clientX - sx) + Math.abs(ev.clientY - sy) < 6) return;
            started = true;
            const nx = Math.max(2, Math.min(ox + ev.clientX - sx, innerWidth - 96));
            const ny = Math.max(2, Math.min(oy + ev.clientY - sy, innerHeight - 46 - 100));
            node.style.left = nx + "px"; node.style.top = ny + "px"; node.style.right = "auto"; node.style.bottom = "auto";
            const tgt = dropAt(ev.clientX, ev.clientY);
            if (tgt !== curDrop) { if (curDrop) curDrop.classList.remove("drop-ok"); curDrop = tgt; if (curDrop) curDrop.classList.add("drop-ok"); }
        };
        const up = ev => {
            window.removeEventListener("pointermove", move); window.removeEventListener("pointerup", up);
            if (curDrop) curDrop.classList.remove("drop-ok");
            if (started && curDrop && curDrop.id === "deskBin" && app !== "bin") {
                showDialog({ title: "Cannot remove shortcut", icon: "warn", html: `<b>${label}</b> is a managed system application and cannot be removed.<div class='sub'>Contact your IT administrator (ext. 4357) to request changes to your provisioned software.</div>`, buttons: [{ label: "OK", value: "ok", primary: true }] });
                // snap back
                node.style.left = ox + "px"; node.style.top = oy + "px";
            }
            curDrop = null; started = false;
        };
        window.addEventListener("pointermove", move); window.addEventListener("pointerup", up);
    }

    function refreshBinIcon() {
        const bin = $("#deskBin"); if (bin) bin.querySelector(".ico").innerHTML = recycleBin.length ? BIGICO.binFull : BIGICO.bin;
        const bw = wins.get("bin"); if (bw && bw.render) bw.render();   // bin window owns its own taskbar icon
    }

    async function onDropToBin(payload) {
        if (!payload || payload.kind === "folder") {
            if (payload && payload.kind === "folder") toast({ kind: "warn", title: "Folder not moved", body: "Folders can only be deleted from inside Meridian Files." });
            return;
        }
        const ok = await confirmDelete(payload.file.name + "." + payload.file.ext);
        if (ok) deleteFile(payload.folderKey, payload.file);
    }
    function confirmDelete(displayName) {
        return showDialog({
            title: "Delete File", icon: "warn",
            html: `Are you sure you want to move <b>${displayName}</b> to the Recycle Bin?<div class='sub'>This item will be removed from its current location.</div>`,
            buttons: [{ label: "Delete", value: true, kind: "danger" }, { label: "Cancel", value: false, primary: true }],
            escValue: false,
        });
    }
    function deleteFile(folderKey, file) {
        const f = folders[folderKey]; if (!f) return;
        const i = f.items.indexOf(file); if (i < 0) return;
        f.items.splice(i, 1);
        recycleBin.push({ file, fromFolder: folderKey, fromName: f.name });
        refreshAllFileViews(); refreshBinIcon();
        toast({ kind: "ok", title: "Moved to Recycle Bin", body: `${file.name}.${file.ext}` });
    }

    /* ============================================================ APPS ===== */
    let eulaAccepted = false;
    async function gateEula() {
        if (eulaAccepted) return true;
        const v = await showDialog({
            title: "Meridian Office Suite", titleIcon: false, icon: null,
            html: `<b>End User License Agreement</b><div class='sub'>Please review and accept the terms to continue.</div>` +
                `<div class='eula'>This Meridian Office Suite Software License Agreement ("Agreement") is a binding contract between you ("Licensee") and Meridian Dynamics Inc. ("Meridian"). By clicking "I Accept", you acknowledge that you have read, understood, and agree to be bound by the terms herein, including but not limited to: (a) the Synergy Telemetry Addendum; (b) the OneVault Data Residency Schedule; (c) mandatory quarterly productivity self-assessments; and (d) Section 14.2, regarding the non-transferability of unused collaboration credits. Meridian reserves the right to modify the definition of "productivity" at any time. This software is provided "as-is" without warranty of measurable output. Continued use constitutes ongoing agreement. Coffee breaks exceeding 12 minutes may be logged for synergy-optimization purposes. The plural of "synergy" is "synergies."</div>`,
            check: "I have read and understood the agreement",
            buttons: [{ label: "Decline", value: false }, { label: "I Accept", value: true, primary: true }],
            escValue: false,
        });
        if (v) { eulaAccepted = true; return true; }
        toast({ kind: "warn", title: "Agreement required", body: "You must accept the license agreement to use Meridian Office." });
        return false;
    }

    /* ----- Files ----------------------------------------------------------- */
    const fileViews = new Set();   // open Files windows -> re-render on data change
    function refreshAllFileViews() { fileViews.forEach(fn => { try { fn(); } catch (_) { } }); const bw = wins.get("bin"); if (bw && bw.render) bw.render(); }

    async function openFiles(startKey) {
        if (!(await gateEula())) return;
        openWindow({
            id: "files", title: "Meridian Files", icon: ICO.files, width: 760, height: 500,
            build(body, w) {
                let cur = startKey && folders[startKey] ? startKey : "documents";
                const wrap = el("div", { class: "files" });
                const side = el("div", { class: "files-side" });
                side.innerHTML = `<div class="grp">Quick access</div>`;
                [["documents", ICO.files, "Documents"], ["reports", ICO.files, "Reports"], ["archive", ICO.files, "Archive"]].forEach(([k, ic, nm]) => {
                    const it = el("div", { class: "side-item", onclick: () => go(k) }); it.dataset.k = k;
                    it.innerHTML = `${ic()}<span>${nm}</span>`; side.appendChild(it);
                });
                side.appendChild(el("div", { class: "grp" }, "This PC"));
                const binItem = el("div", { class: "side-item", onclick: () => { openBin(); }, html: `${ICO.bin()}<span>Recycle Bin</span>` });
                side.appendChild(binItem);

                const main = el("div", { class: "files-main" });
                const addr = el("div", { class: "address" });
                const back = el("button", { class: "tbtn", title: "Up one level", html: "&#8593;", onclick: () => { const p = folders[cur].parent; if (p) go(p); } });
                const crumbs = el("div", { class: "crumbs" });
                const search = el("input", { class: "searchbox", type: "text", placeholder: "Search" });
                search.addEventListener("input", render);
                addr.append(back, crumbs, search);
                const grid = el("div", { class: "filegrid" });
                // (the grid itself isn't a dropzone — only folders + the Recycle Bin accept
                //  drops; dropping a file onto empty space just leaves it where it was.)
                const status = el("div", { class: "statusbar" });
                main.append(addr, grid, status);
                wrap.append(side, main); body.appendChild(wrap);

                function go(k) { cur = k; search.value = ""; render(); }
                function render() {
                    const f = folders[cur];
                    side.querySelectorAll(".side-item").forEach(n => n.classList.toggle("active", n.dataset.k === cur));
                    const path = []; let c = cur; while (c) { path.unshift(folders[c].name); c = folders[c].parent; }
                    crumbs.innerHTML = `This PC <span>&rsaquo;</span> ` + path.map((p, i) => i === path.length - 1 ? `<b>${p}</b>` : `${p} <span>&rsaquo;</span> `).join(" ");
                    back.disabled = !f.parent;
                    grid.innerHTML = "";
                    const q = search.value.trim().toLowerCase();
                    const items = f.items.filter(it => !q || (it.kind === "folder" ? it.name : it.name + "." + it.ext).toLowerCase().includes(q));
                    if (!items.length) grid.appendChild(el("div", { class: "empty-note" }, q ? "No items match your search." : "This folder is empty."));
                    items.forEach(it => grid.appendChild(it.kind === "folder" ? folderTile(it, go) : fileTile(it, cur)));
                    const nf = f.items.filter(i => i.kind === "file").length, nd = f.items.filter(i => i.kind === "folder").length;
                    status.textContent = `${f.items.length} item${f.items.length === 1 ? "" : "s"}` + (nd ? `  ·  ${nd} folder${nd === 1 ? "" : "s"}` : "") + (nf ? `  ·  ${nf} file${nf === 1 ? "" : "s"}` : "");
                }
                w.render = render; fileViews.add(render);
                w._onClose = () => fileViews.delete(render);   // stop re-rendering once closed
                render();
            }
        });
    }

    function folderTile(item, go) {
        const node = el("div", { class: "file", title: item.name });
        node.innerHTML = `<div class="ico">${BIGICO.files}</div><div class="nm">${item.name}</div>`;
        node.addEventListener("dblclick", () => go(item.to));
        node.addEventListener("click", e => { e.currentTarget.parentNode.querySelectorAll(".file.sel").forEach(n => n.classList.remove("sel")); node.classList.add("sel"); });
        // a folder is a drop target: move a dropped file into it
        makeDropzone(node, (payload) => {
            if (!payload || payload.kind !== "file" || payload.folderKey === item.to) return;
            moveFile(payload.folderKey, item.to, payload.file);
        });
        return node;
    }
    function fileTile(file, folderKey) {
        const node = el("div", { class: "file", title: file.name + "." + file.ext });
        node.innerHTML = `<div class="ico">${fileGlyph(file.ext)}</div><div class="nm">${file.name}.${file.ext}</div>`;
        node.addEventListener("click", e => { node.parentNode.querySelectorAll(".file.sel").forEach(n => n.classList.remove("sel")); node.classList.add("sel"); });
        node.addEventListener("dblclick", () => openFileItem(file));
        node.addEventListener("pointerdown", e => {
            if (e.button !== 0) return;
            startItemDrag(e, node, { ghostHtml: fileGlyph(file.ext), label: file.name + "." + file.ext, payload: { kind: "file", file, folderKey } });
        });
        return node;
    }
    function moveFile(fromKey, toKey, file) {
        const a = folders[fromKey], b = folders[toKey]; if (!a || !b) return;
        const i = a.items.indexOf(file); if (i < 0) return;
        a.items.splice(i, 1); b.items.push(file);
        refreshAllFileViews();
        toast({ kind: "ok", title: "File moved", body: `${file.name}.${file.ext} → ${b.name}` });
    }
    function openFileItem(file) {
        const meta = EXT[file.ext];
        if (meta && meta.app === "sheets") return openSheets(file.name);
        if (meta && meta.app === "docs") return openDocs(file.name);
        if (file.ext === "txt") return openNotepad(file);
        // everything else: a believable "opening / preview" flow
        showDialog({
            title: "Meridian Workspace", icon: "info",
            html: `Opening <b>${file.name}.${file.ext}</b>…<div class='sub'>This file type is handled by an associated application.</div><div class='dlg-progress'><i></i></div>`,
            buttons: [{ label: "Cancel", value: null }],
        });
        const bar = $("#dialog-layer .dlg-progress i");
        let p = 0; const t = setInterval(() => {
            p += 12 + Math.random() * 18; if (bar) bar.style.width = Math.min(p, 100) + "%";
            if (p >= 100) {
                clearInterval(t); dlgLayer.classList.add("hidden"); dlgLayer.innerHTML = "";
                toast({ kind: "warn", title: "Compatibility notice", body: `“${file.name}.${file.ext}” was created in a newer version. Some formatting may be lost.` });
            }
        }, 240);
    }

    /* ----- Sheets ---------------------------------------------------------- */
    async function openSheets(docName) {
        if (!(await gateEula())) return;
        const title = (docName ? docName + ".xlsx" : "2026_Operating_Budget.xlsx") + " — Meridian Sheets";
        openWindow({
            id: "sheets", title, icon: ICO.sheets, width: 720, height: 500,
            build(body, w) {
                body.appendChild(el("div", {
                    class: "menubar", html: ["File", "Home", "Insert", "Formulas", "Data", "Review", "View"].map(m => `<span class="mi">${m}</span>`).join("")
                }));
                const tools = el("div", { class: "toolbar" });
                const sumBtn = el("button", { class: "tbtn primary", html: "Σ AutoSum" });
                tools.append(
                    el("button", { class: "tbtn", text: "Save", onclick: () => savedToast("Workbook") }),
                    el("div", { class: "tsep" }),
                    el("button", { class: "tbtn", text: "Bold" }), el("button", { class: "tbtn", text: "Italic" }),
                    el("div", { class: "tsep" }),
                    el("button", { class: "tbtn", text: "% Format" }), el("button", { class: "tbtn", text: "Chart", onclick: () => toast({ kind: "info", title: "Insert Chart", body: "Chart tools require the Meridian Analytics add-in (not installed)." }) }),
                    el("div", { class: "tgrow" }), sumBtn
                );
                body.appendChild(tools);

                const fbar = el("div", { class: "formula-bar" });
                const cref = el("div", { class: "cellref", text: "A1" });
                const fx = el("span", { class: "fx", text: "fx" });
                const fcontent = el("div", { class: "fcontent", text: "" });
                fbar.append(cref, fx, fcontent); body.appendChild(fbar);

                const scroll = el("div", { class: "grid-scroll" });
                const cols = ["", "Department", "Q1", "Q2", "Q3", "Q4", "FY Total"];
                const data = [
                    ["Engineering", 412000, 438500, 451200, 470000],
                    ["Sales & Synergy", 388000, 401000, 423750, 449000],
                    ["Marketing", 196500, 210000, 188000, 232000],
                    ["Operations", 154000, 159200, 162400, 168000],
                    ["Facilities", 88000, 91000, 89500, 94000],
                    ["Synergy Consulting", 240000, 255000, 268000, 280000],
                ];
                const tbl = el("table", { class: "sheet" });
                const thead = el("thead"); const trh = el("tr");
                trh.appendChild(el("th", { class: "corner" }));
                ["A", "B", "C", "D", "E", "F"].forEach(c => trh.appendChild(el("th", { text: c })));
                thead.appendChild(trh); tbl.appendChild(thead);
                const tb = el("tbody");
                // header row (row 1)
                const r1 = el("tr"); r1.appendChild(el("th", { text: "1" }));
                cols.slice(1).forEach(c => r1.appendChild(el("td", { class: "head txt", text: c })));
                tb.appendChild(r1);
                const totalCells = [];
                data.forEach((row, ri) => {
                    const tr = el("tr"); tr.appendChild(el("th", { text: String(ri + 2) }));
                    tr.appendChild(el("td", { class: "txt", text: row[0] }));
                    for (let q = 1; q <= 4; q++) {
                        const td = el("td", { contenteditable: "true", "data-q": q, "data-r": ri, text: fmt(row[q]) });
                        td.addEventListener("focus", () => selectCell(td, ri, q));
                        td.addEventListener("input", () => { fcontent.textContent = td.textContent; });
                        td.addEventListener("blur", () => { td.textContent = fmt(num(td.textContent)); recalcRow(ri); });
                        tr.appendChild(td);
                    }
                    const tot = el("td", { class: "total", "data-rt": ri });
                    totalCells.push(tot); tr.appendChild(tot); tb.appendChild(tr);
                });
                // grand total row
                const gtr = el("tr"); gtr.appendChild(el("th", { text: String(data.length + 2) }));
                gtr.appendChild(el("td", { class: "txt total", text: "TOTAL" }));
                const gcells = [];
                for (let q = 0; q < 5; q++) { const c = el("td", { class: "total" }); gcells.push(c); gtr.appendChild(c); }
                tb.appendChild(gtr);
                tbl.appendChild(tb); scroll.appendChild(tbl); body.appendChild(scroll);

                body.appendChild(el("div", { class: "statusbar", html: `Ready &nbsp;·&nbsp; Sheet 1 of 3 &nbsp;·&nbsp; <span id="ssum">Sum: —</span> &nbsp;·&nbsp; 100%` }));

                let selTd = null;
                function selectCell(td, ri, q) {
                    if (selTd) selTd.classList.remove("sel"); selTd = td; td.classList.add("sel");
                    cref.textContent = "BCDE".charAt(q - 1) + (ri + 2);
                    fcontent.textContent = td.textContent;
                }
                function recalcRow(ri) {
                    let s = 0; const tr = totalCells[ri].parentNode;
                    tr.querySelectorAll("td[data-q]").forEach(td => s += num(td.textContent));
                    totalCells[ri].textContent = fmt(s); recalcGrand();
                }
                function recalcGrand() {
                    for (let q = 1; q <= 4; q++) { let s = 0; tb.querySelectorAll(`td[data-q="${q}"]`).forEach(td => s += num(td.textContent)); gcells[q - 1].textContent = fmt(s); }
                    let g = 0; totalCells.forEach(c => g += num(c.textContent)); gcells[4].textContent = fmt(g);
                }
                sumBtn.addEventListener("click", () => {
                    let i = 0; totalCells.forEach((c, idx) => setTimeout(() => { recalcRow(idx); c.animate([{ background: "#bdebcb" }, { background: "#eef7f1" }], { duration: 500 }); }, (i++) * 90));
                    setTimeout(() => { recalcGrand(); $("#ssum") && ($("#ssum").textContent = "Sum: " + fmt(num(gcells[4].textContent))); toast({ kind: "ok", title: "Recalculation complete", body: "6 rows, 24 cells updated." }); }, totalCells.length * 90 + 120);
                });
                data.forEach((_, ri) => recalcRow(ri));
            }
        });
        function fmt(n) { return "$" + (Math.round(n) || 0).toLocaleString("en-US"); }
        function num(s) { return parseFloat(String(s).replace(/[^0-9.\-]/g, "")) || 0; }
    }
    const savedToast = what => toast({ kind: "ok", title: "Saved to OneVault", body: `${what} saved successfully · just now` });

    /* ----- Docs ------------------------------------------------------------ */
    async function openDocs(docName) {
        if (!(await gateEula())) return;
        const title = (docName ? docName + ".docx" : "Strategic_Synergy_Memo.docx") + " — Meridian Docs";
        openWindow({
            id: "docs", title, icon: ICO.docs, width: 760, height: 560,
            build(body) {
                body.appendChild(el("div", { class: "menubar", html: ["File", "Home", "Insert", "Layout", "References", "Review", "View"].map(m => `<span class="mi">${m}</span>`).join("") }));
                const tools = el("div", { class: "toolbar" });
                const fontsel = el("select", { class: "fontsel" });
                ["Calibri", "Times New Roman", "Arial", "Georgia", "Garamond"].forEach(f => fontsel.appendChild(el("option", { text: f })));
                const sizesel = el("select", { class: "fontsel" });
                ["11", "12", "14", "18"].forEach(s => sizesel.appendChild(el("option", { text: s, selected: s === "12" ? "" : null })));
                const cmd = (c) => el("button", { class: "tbtn", onmousedown: e => { e.preventDefault(); document.execCommand(c); } });
                const b = cmd("bold"); b.innerHTML = "<b>B</b>";
                const it = cmd("italic"); it.innerHTML = "<i>I</i>";
                const un = cmd("underline"); un.innerHTML = "<u>U</u>";
                tools.append(
                    el("button", { class: "tbtn", text: "Save", onclick: () => savedToast("Document") }),
                    el("div", { class: "tsep" }), fontsel, sizesel, el("div", { class: "tsep" }), b, it, un,
                    el("div", { class: "tsep" }),
                    el("button", { class: "tbtn", html: "&#9776;", title: "Align", onmousedown: e => { e.preventDefault(); document.execCommand("justifyFull"); } }),
                    el("div", { class: "tgrow" }),
                    el("button", { class: "tbtn", text: "Share", onclick: () => toast({ kind: "info", title: "Share", body: "A sharing link was copied to your clipboard (not really)." }) }),
                );
                body.appendChild(tools);
                fontsel.addEventListener("change", () => { const p = $(".page", body); if (p) p.style.fontFamily = fontsel.value; });

                const scroll = el("div", { class: "doc-scroll" });
                const page = el("div", { class: "page", contenteditable: "true", spellcheck: "false" });
                page.innerHTML =
                    `<h1>Q3 Strategic Synergy Initiative</h1>` +
                    `<div class="subtle">INTERNAL MEMORANDUM &nbsp;·&nbsp; Prepared by Alex Morgan, Regional Synergy Lead &nbsp;·&nbsp; CONFIDENTIAL</div>` +
                    `<p><b>Executive Summary.</b> Following a comprehensive review of cross-functional alignment opportunities, this memorandum outlines our roadmap for unlocking next-generation synergies across all verticals. By leveraging our core competencies and operationalizing best-in-class frameworks, we are positioned to deliver measurable, scalable, and frankly disruptive value to all stakeholders.</p>` +
                    `<h2>1. Strategic Context</h2>` +
                    `<p>In today's rapidly evolving paradigm, organizations that fail to proactively ideate around holistic value creation risk being left behind. Our Q2 results, while directionally positive, surfaced several actionable learnings. We must now pivot from a siloed mindset toward a culture of radical collaboration and continuous synergy realization.</p>` +
                    `<h2>2. Key Workstreams</h2>` +
                    `<p>The initiative will be delivered through three mutually reinforcing workstreams: (1) <b>Alignment</b> &mdash; harmonizing our north-star metrics; (2) <b>Activation</b> &mdash; empowering champions at every level to drive grassroots momentum; and (3) <b>Acceleration</b> &mdash; double-clicking on the highest-leverage opportunities to move the needle.</p>` +
                    `<h2>3. Next Steps</h2>` +
                    `<p>A working group will convene to socialize this framework and circle back with a finalized action register. Please review ahead of Thursday's sync and come prepared to take ownership of your respective deliverables. Let's make this happen.</p>` +
                    `<p style="color:#7a838f;font-style:italic">[ This document is auto-saved to OneVault every 30 seconds. ]</p>`;
                scroll.appendChild(page); body.appendChild(scroll);
                body.appendChild(el("div", { class: "statusbar", html: `Page 1 of 1 &nbsp;·&nbsp; 312 words &nbsp;·&nbsp; English (United States) &nbsp;·&nbsp; <span style="color:#1e7f4f">● Saved</span>` }));
            }
        });
    }

    /* ----- Notepad (plain-text viewer) ------------------------------------- */
    // A lightweight Notepad for .txt files. One window per file (id keyed on the
    // file id) so several notes can sit open and content never goes stale. The
    // pay-off home for it is the resignation letter sitting in the Recycle Bin.
    function openNotepad(file) {
        const fname = file.name + "." + file.ext;
        const text = file.text != null ? file.text : "";
        openWindow({
            id: "note:" + file.id, title: fname + " — Notepad", icon: ICO.note, width: 600, height: 480,
            build(body) {
                body.appendChild(el("div", { class: "menubar", html: ["File", "Edit", "Format", "View", "Help"].map(m => `<span class="mi">${m}</span>`).join("") }));
                const tools = el("div", { class: "toolbar" });
                const wrapBtn = el("button", { class: "tbtn", text: "Word Wrap", onclick: () => { area.classList.toggle("wrap"); wrapBtn.classList.toggle("primary"); } });
                tools.append(
                    el("button", { class: "tbtn", text: "Save", onclick: () => savedToast("Note") }),
                    el("div", { class: "tsep" }),
                    wrapBtn,
                    el("div", { class: "tgrow" }),
                );
                body.appendChild(tools);
                const area = el("textarea", { class: "notepad", spellcheck: "false" });
                area.value = text;
                body.appendChild(area);
                const status = el("div", { class: "statusbar" });
                const refresh = () => {
                    const v = area.value, lines = v.split("\n").length, chars = v.length;
                    status.innerHTML = `${lines} line${lines === 1 ? "" : "s"} &nbsp;·&nbsp; ${chars} character${chars === 1 ? "" : "s"} &nbsp;·&nbsp; Plain Text &nbsp;·&nbsp; UTF-8`;
                };
                area.addEventListener("input", refresh);
                body.appendChild(status); refresh();
            }
        });
    }

    /* ----- Mail ------------------------------------------------------------ */
    const mails = [
        { from: "IT Service Desk", when: "9:14 AM", subj: "ACTION REQUIRED: Mandatory password reset", unread: true, body: "<p>Dear valued team member,</p><p>Our records indicate your network password will expire in <b>0 days</b>. To avoid disruption, please reset it immediately using the self-service portal. Your new password must contain at least 14 characters, three emoji, and may not resemble any of your previous 24 passwords.</p><p>Thank you for helping keep Meridian secure.</p><p>— IT Service Desk</p>" },
        { from: "Synergy Committee", when: "8:47 AM", subj: "Reminder: Q3 All-Hands Synergy Sync (mandatory)", unread: true, body: "<p>Hi everyone,</p><p>A friendly reminder that attendance at Thursday's All-Hands is <b>strongly mandatory</b>. We'll be unveiling the new mission statement, the new new values, and a refreshed approach to refreshing our approach.</p><p>Breakfast will not be provided, but enthusiasm is required.</p><p>Warmly,<br>The Synergy Committee</p>" },
        { from: "OneVault Backup", when: "Yesterday", subj: "Your backup completed with 1 warning", unread: false, body: "<p>Your scheduled backup completed successfully.</p><p><b>Warning:</b> 1 file could not be backed up because it was open in another application for the past 6 weeks. Please close \"Untitled-1\" at your earliest convenience.</p>" },
        { from: "Jordan Ellis (Manager)", when: "Yesterday", subj: "quick sync?", unread: false, body: "<p>Hey,</p><p>Do you have 15 minutes to align on the alignment doc before we align with the broader alignment group? Want to make sure we're aligned.</p><p>Thanks!<br>Jordan</p>" },
        { from: "Meridian Wellness", when: "Mon", subj: "🧘 5-minute mindfulness for peak productivity", unread: false, body: "<p>Feeling overwhelmed? Studies show that taking a deliberate 5-minute breathing break can boost output by up to 4%.</p><p>Please log your mindfulness minutes in the Wellness Portal so they can be tracked against your quarterly targets.</p>" },
    ];
    const MAIL_FOLDERS = ["Inbox", "Sent", "Drafts", "Synergy", "Spam", "Deleted Items"];
    function mailCount(name) {
        if (name === "Inbox") return mails.filter(m => m.unread).length;
        if (name === "Drafts") return drafts.length;
        if (name === "Spam") return 47;
        return 0;
    }
    async function openMail(startFolder) {
        if (!(await gateEula())) return;
        openWindow({
            id: "mail", title: "Meridian Mail", icon: ICO.mail, width: 820, height: 560,
            build(body, w) {
                const tools = el("div", { class: "toolbar" });
                tools.append(
                    el("button", { class: "tbtn primary", html: "&#9993; New Message", onclick: compose }),
                    el("button", { class: "tbtn", text: "Reply", onclick: () => toast({ kind: "info", title: "Reply", body: "Drafting is disabled in offline mode." }) }),
                    el("button", { class: "tbtn", text: "Archive", onclick: () => toast({ kind: "ok", title: "Archived", body: "Message moved to Archive." }) }),
                );
                body.appendChild(tools);
                const mail = el("div", { class: "mail" });
                const side = el("div", { class: "mail-side" });
                const list = el("div", { class: "mail-list" });
                const read = el("div", { class: "mail-read" });
                mail.append(side, list, read); body.appendChild(mail);

                let cur = MAIL_FOLDERS.includes(startFolder) ? startFolder : "Inbox";
                const itemsFor = name => name === "Inbox" ? mails : name === "Drafts" ? drafts : [];

                function renderSide() {
                    side.innerHTML = "";
                    MAIL_FOLDERS.forEach(name => {
                        const n = mailCount(name);
                        const it = el("div", { class: "side-item" + (name === cur ? " active" : ""), onclick: () => { cur = name; render(); } });
                        it.innerHTML = `<span>${name}</span>` + (n ? `<span style="margin-left:auto;color:var(--ink-soft)">${n}</span>` : "");
                        side.appendChild(it);
                    });
                }
                function render() {
                    renderSide();
                    list.innerHTML = "";
                    const items = itemsFor(cur);
                    if (!items.length) {
                        list.appendChild(el("div", { class: "empty-note", text: cur === "Drafts" ? "No drafts." : "This folder is empty." }));
                        read.innerHTML = `<div class="mail-empty">Nothing to read here.</div>`;
                        return;
                    }
                    const isDraft = cur === "Drafts";
                    items.forEach(m => {
                        const who = isDraft ? "To: " + m.to : m.from;
                        const when = isDraft ? "Draft" : m.when;
                        const it = el("div", { class: "mail-item" + (!isDraft && m.unread ? " unread" : "") });
                        it.innerHTML = `<div class="from"><span>${!isDraft && m.unread ? '<span class="dot"></span>' : ""}${who}</span><span class="when">${when}</span></div><div class="subj">${m.subj}</div><div class="prev">${m.body.replace(/<[^>]+>/g, " ").trim().slice(0, 60)}…</div>`;
                        it.addEventListener("click", () => {
                            list.querySelectorAll(".mail-item").forEach(n => n.classList.remove("active")); it.classList.add("active");
                            if (isDraft) showDraft(m);
                            else { if (m.unread) { m.unread = false; it.classList.remove("unread"); renderSide(); } showMsg(m); }
                        });
                        list.appendChild(it);
                    });
                    // open the first item by default
                    const first = list.querySelector(".mail-item");
                    if (first) { first.classList.add("active"); isDraft ? showDraft(items[0]) : showMsg(items[0]); }
                }
                function showMsg(m) {
                    read.innerHTML = `<h2>${m.subj}</h2><div class="meta"><b>${m.from}</b> &lt;${m.from.toLowerCase().replace(/[^a-z]+/g, ".").replace(/^\.|\.$/g, "")}@meridian-dynamics.com&gt;<br>To: alex.morgan@meridian-dynamics.com &nbsp;·&nbsp; ${m.when}</div><div class="body">${m.body}</div>`;
                }
                function showDraft(d) {
                    read.innerHTML = `<h2>${d.subj || "(no subject)"}</h2><div class="meta">From: <b>Alex Morgan</b> &lt;alex.morgan@meridian-dynamics.com&gt;<br>To: ${d.to} &lt;${d.toAddr}&gt; &nbsp;·&nbsp; <span style="color:var(--danger)">Draft — not sent</span></div><div class="body">${d.body}</div>`;
                    const row = el("div", { class: "draft-actions" });
                    row.append(
                        el("button", { class: "btn primary", html: "&#9993;&nbsp; Send", onclick: () => sendDraft(d) }),
                        el("button", { class: "btn danger", text: "Delete", onclick: () => deleteDraft(d) }),
                    );
                    read.appendChild(row);
                }
                function sendDraft(d) {
                    const i = drafts.indexOf(d); if (i < 0) return;
                    drafts.splice(i, 1);
                    toast({ kind: "ok", title: "Message sent", body: `“${d.subj}” has been delivered to ${d.to}.` });
                    scheduleIncomingCall(d);   // the boss tends to "quick sync" right back…
                    render();
                }
                async function deleteDraft(d) {
                    const ok = await showDialog({
                        title: "Delete draft", icon: "warn",
                        html: `Move the draft <b>“${d.subj}”</b> to the Recycle Bin?`,
                        buttons: [{ label: "Delete", value: true, kind: "danger" }, { label: "Cancel", value: false, primary: true }], escValue: false,
                    });
                    if (!ok) return;
                    const i = drafts.indexOf(d); if (i < 0) return;
                    drafts.splice(i, 1);
                    recycleBin.push({ kind: "mail", mail: d, fromName: "Mail" });
                    refreshBinIcon();
                    toast({ kind: "ok", title: "Moved to Recycle Bin", body: `“${d.subj}”` });
                    render();
                }

                w.render = render;
                mailRefresh = render;
                w._onClose = () => { if (mailRefresh === render) mailRefresh = null; };
                render();
            }
        });
    }

    // --- Incoming call (STUB — to be expanded later) -----------------------
    // Sending the binned draft "summons" the manager: 5 minutes later an incoming
    // Meridian Meet call slides in. For now it's a popup with Accept/Decline; the
    // actual in-call experience is a follow-up. Console: eaIncomingCall() to preview.
    const INCOMING_CALL_DELAY = 5 * 60 * 1000;   // 5 minutes
    function scheduleIncomingCall(draft) {
        setTimeout(() => incomingCall(draft), INCOMING_CALL_DELAY);
    }
    function incomingCall(draft) {
        if ($("#zoom-call")) return;   // never stack two calls
        const caller = (draft && draft.to) || "Jordan Ellis";
        const initials = caller.split(/\s+/).map(s => s[0]).join("").slice(0, 2).toUpperCase();
        const card = el("div", { class: "zoom-call", id: "zoom-call" });
        card.innerHTML =
            `<div class="zc-head"><span class="zc-dot"></span>Incoming Meridian Meet call…</div>` +
            `<div class="zc-who"><div class="zc-avatar">${initials}</div><div><div class="zc-name">${caller}</div><div class="zc-sub">Manager · wants to “quick sync”</div></div></div>`;
        const row = el("div", { class: "zc-actions" });
        row.append(
            el("button", { class: "zc-btn decline", text: "Decline", onclick: () => { card.remove(); toast({ kind: "warn", title: "Call declined", body: `${caller} will “find time on your calendar.”` }); } }),
            el("button", { class: "zc-btn accept", text: "Accept", onclick: () => { card.remove(); toast({ kind: "info", title: "Meridian Meet", body: "Connecting you to the call… (coming soon)" }); } }),
        );
        card.appendChild(row);
        document.body.appendChild(card);
    }
    window.eaIncomingCall = incomingCall;   // dev hook: preview without the 5-min wait
    async function compose() {
        const v = await showDialog({
            title: "New Message", titleIcon: false, icon: null,
            html: `<div style="display:flex;flex-direction:column;gap:8px">
                     <input class="searchbox" style="width:100%" placeholder="To:" value="">
                     <input class="searchbox" style="width:100%" placeholder="Subject:" value="">
                     <textarea class="searchbox" style="width:100%;height:120px;resize:none;font-family:var(--font)" placeholder="Write your message…"></textarea>
                   </div>`,
            buttons: [{ label: "Discard", value: false }, { label: "Send", value: true, primary: true }],
            escValue: false,
        });
        if (v) toast({ kind: "ok", title: "Message sent", body: "Your message has been delivered." });
    }
    // Read-only peek at the binned email (double-click in the Recycle Bin). To send
    // or delete it, the player restores it to Drafts first.
    function previewBinMail(m) {
        showDialog({
            title: "Draft (in Recycle Bin)", titleIcon: false, icon: null,
            html: `<div class="meta" style="margin-bottom:10px">To: ${m.to} &lt;${m.toAddr}&gt;<br>Subject: <b>${m.subj}</b></div><div class="body">${m.body}</div><div class='sub'>Restore this draft to Mail → Drafts to send or delete it.</div>`,
            buttons: [{ label: "Close", value: null, primary: true }],
        });
    }

    /* ----- Recycle Bin ----------------------------------------------------- */
    function openBin() {
        openWindow({
            id: "bin", title: "Recycle Bin", icon: () => (recycleBin.length ? ICO.binFull() : ICO.bin()), width: 640, height: 440,
            build(body, w) {
                const tools = el("div", { class: "toolbar" });
                const restoreBtn = el("button", { class: "tbtn", text: "Restore selected", disabled: "" });
                const emptyBtn = el("button", { class: "tbtn danger", text: "Empty Recycle Bin" });
                emptyBtn.classList.add("tbtn"); emptyBtn.style.color = "var(--danger)";
                tools.append(restoreBtn, el("div", { class: "tgrow" }), emptyBtn);
                body.appendChild(tools);
                const view = el("div", { class: "binview" });
                const grid = el("div", { class: "filegrid" });
                view.appendChild(grid); body.appendChild(view);
                const status = el("div", { class: "statusbar" });
                body.appendChild(status);
                let sel = null;
                const binName = entry => entry.kind === "mail" ? entry.mail.subj : entry.file.name + "." + entry.file.ext;
                function render() {
                    grid.innerHTML = ""; sel = null; restoreBtn.disabled = true;
                    if (!recycleBin.length) { grid.appendChild(el("div", { class: "empty-note", text: "Recycle Bin is empty." })); }
                    else recycleBin.forEach(entry => {
                        const isMail = entry.kind === "mail";
                        const name = binName(entry);
                        const node = el("div", { class: "file", title: `${name} (from ${entry.fromName})` });
                        node.innerHTML = `<div class="ico">${isMail ? BIGICO.mail : fileGlyph(entry.file.ext)}</div><div class="nm">${name}</div>`;
                        node.addEventListener("click", () => { grid.querySelectorAll(".file.sel").forEach(n => n.classList.remove("sel")); node.classList.add("sel"); sel = entry; restoreBtn.disabled = false; });
                        node.addEventListener("dblclick", () => isMail ? previewBinMail(entry.mail) : openFileItem(entry.file));
                        grid.appendChild(node);
                    });
                    status.textContent = `${recycleBin.length} item${recycleBin.length === 1 ? "" : "s"}`;
                    const ico = recycleBin.length ? ICO.binFull() : ICO.bin();
                    if (w.taskBtn) w.taskBtn.querySelector("svg").outerHTML = ico;
                }
                restoreBtn.addEventListener("click", () => {
                    if (!sel) return;
                    const i = recycleBin.indexOf(sel); if (i < 0) return;
                    recycleBin.splice(i, 1);
                    if (sel.kind === "mail") {
                        // an email restores into Mail → Drafts, where it can be sent or re-deleted
                        drafts.push(sel.mail);
                        if (mailRefresh) mailRefresh();
                        toast({ kind: "ok", title: "Restored to Drafts", body: `“${sel.mail.subj}” → Drafts`, action: { label: "Open Mail", fn: () => openMail("Drafts") } });
                    } else {
                        // documents land back in a (randomly chosen) docs folder
                        const keys = ["documents", "reports", "archive"];
                        const destKey = keys[Math.floor(Math.random() * keys.length)];
                        folders[destKey].items.push(sel.file);
                        refreshAllFileViews();
                        toast({ kind: "ok", title: "Restored", body: `${sel.file.name}.${sel.file.ext} → ${folders[destKey].name}` });
                    }
                    render(); refreshBinIcon();
                });
                emptyBtn.addEventListener("click", async () => {
                    if (!recycleBin.length) return;
                    const ok = await showDialog({
                        title: "Empty Recycle Bin", icon: "warn",
                        html: `Are you sure you want to permanently delete these <b>${recycleBin.length}</b> item${recycleBin.length === 1 ? "" : "s"}?<div class='sub'>This action cannot be undone.</div>`,
                        buttons: [{ label: "Yes, delete permanently", value: true, kind: "danger" }, { label: "Cancel", value: false, primary: true }], escValue: false,
                    });
                    if (ok) { recycleBin.length = 0; render(); refreshBinIcon(); toast({ kind: "ok", title: "Recycle Bin emptied", body: "All items permanently deleted." }); }
                });
                w.render = render; render();
            }
        });
    }

    /* ----- Games launcher -------------------------------------------------- */
    // The "boss key" pay-off: a Games folder you "slack off" with at work. Evil
    // Aliens launches back into THIS game (../); Legends of Amara launches its own
    // deployment; ProtoFighter is eternally "installing" and can't be booted yet.
    // Sibling paths resolve from the site root (RETURN_TO_GAME = the game's base),
    // so they map to coamithra.github.io/<repo>/ on Pages exactly like the user's
    // "../rudybrunsman" / "../protofighter" notation.
    // Real cover art lifted from each game's own repo (downscaled into office/covers/
    // by a one-off Pillow pass). Each cover is a full-bleed background plus an optional
    // logo/character overlay.
    const COVER = {
        evilaliens: `<div class="cover-art cover-contain" style="background-image:url('covers/evilaliens.png');background-color:#1a0610"></div>`,
        amara: `<div class="cover-art" style="background-image:url('covers/amara_map.png');background-color:#0a1018"></div><img class="cover-hero" src="covers/amara_hero.png" alt="" draggable="false">`,
        protofighter: `<div class="cover-art" style="background-image:url('covers/protofighter_bg.jpg')"></div><img class="cover-logo" src="covers/protofighter_logo.png" alt="" draggable="false">`,
    };
    const GAMES = [
        { id: "evilaliens", title: "Revenge of the Evil Aliens", genre: "Arcade Shoot-'em-up", blurb: "A recovered 2008 XBLIG twin-stick blaster. Three worlds, up to 4-player couch co-op.", cover: COVER.evilaliens, status: "installed", url: RETURN_TO_GAME },
        { id: "amara", title: "Legends of Amara", genre: "Action Adventure", blurb: "A top-down adventure through the kingdom of Amara — swords, secrets and dungeons await.", cover: COVER.amara, status: "installed", url: "https://notzelda.haraldmaassen.com/" },
        { id: "protofighter", title: "ProtoFighter", genre: "Arena Fighter", blurb: "A prototype arena brawler. Setup is taking a little longer than expected…", cover: COVER.protofighter, status: "installing", url: new URL("../protofighter/", RETURN_TO_GAME).href },
    ];

    function openGames() {
        openWindow({
            id: "games", title: "Meridian Games", icon: ICO.games, width: 768, height: 540,
            build(body, w) {
                const installed = GAMES.filter(g => g.status === "installed").length;
                body.appendChild(el("div", {
                    class: "toolbar", html:
                        `<span style="font-weight:700">Meridian Games</span><span style="color:var(--ink-soft);margin-left:7px">Library</span>` +
                        `<span class="tgrow"></span><span style="color:var(--ink-soft);font-size:12px">${GAMES.length} titles &middot; ${installed} installed</span>`
                }));
                const grid = el("div", { class: "games-grid" });
                const timers = [];
                GAMES.forEach(g => grid.appendChild(gameCard(g, timers)));
                body.appendChild(grid);
                body.appendChild(el("div", { class: "statusbar", html: "Double-click a title to launch it &nbsp;·&nbsp; Saves are stored locally per game" }));
                w._onClose = () => timers.forEach(clearInterval);   // stop the fake installers
            }
        });
    }
    function gameCard(g, timers) {
        const card = el("div", { class: "game-card", tabindex: "0" });
        const installing = g.status === "installing";
        card.innerHTML =
            `<div class="game-cover">${g.cover}<div class="game-badge ${installing ? "inst" : "ok"}">${installing ? "INSTALLING" : "INSTALLED"}</div></div>` +
            `<div class="game-info"><div class="game-title">${g.title}</div><div class="game-genre">${g.genre}</div>` +
            `<div class="game-blurb">${g.blurb}</div><div class="game-foot"></div></div>`;
        const foot = card.querySelector(".game-foot");
        if (installing) {
            foot.classList.add("installing");
            foot.innerHTML = `<div class="game-progrow"><div class="game-prog"><i></i></div><div class="game-prog-txt"><b>0%</b></div></div>`;
            const btn = el("button", { class: "btn", style: { width: "100%" }, html: "&#8635; Installing&hellip;" });
            foot.appendChild(btn);
            const bar = foot.querySelector(".game-prog i"), txt = foot.querySelector(".game-prog-txt b");
            // Perpetual installer: a random walk that creeps up but never finishes
            // (and occasionally drops, the classic "97%… 14%…" stuck-install gag).
            let p = 12 + Math.random() * 26;
            const draw = () => { bar.style.width = p.toFixed(0) + "%"; txt.textContent = p.toFixed(0) + "%"; };
            draw();
            timers.push(setInterval(() => {
                if (Math.random() < 0.18) p = Math.max(6, p - (6 + Math.random() * 24));
                else p = Math.min(98, p + Math.random() * 4);
                draw();
            }, 950));
            btn.addEventListener("click", () => protoStillInstalling(g));
            card.addEventListener("dblclick", () => protoStillInstalling(g));
        } else {
            const btn = el("button", { class: "btn primary", style: { width: "100%" }, html: "&#9654;&nbsp; Play" });
            btn.addEventListener("click", () => launchGame(g));
            foot.appendChild(btn);
            card.addEventListener("dblclick", () => launchGame(g));
        }
        return card;
    }
    function protoStillInstalling(g) {
        const eta = ["calculating&hellip;", "a few more moments&hellip;", "about 2 hours", "47 minutes", "longer than the heat death of the universe", "soon™"];
        showDialog({
            title: "ProtoFighter Setup", icon: "info",
            html: `<b>${g.title}</b> is still installing and can't be launched yet.<div class='sub'>Estimated time remaining: ${eta[Math.floor(Math.random() * eta.length)]}</div>`,
            buttons: [{ label: "Run in background", value: 1, primary: true }],
        });
    }
    function launchGame(g) {
        // Hand the tab off to the chosen game (leaves the office entirely).
        const ov = el("div", { style: { position: "fixed", inset: "0", zIndex: "9999", background: "radial-gradient(900px 600px at 50% 40%, #16223f, #080d1f)", color: "#cfe0ff", display: "grid", placeItems: "center", opacity: "0", transition: "opacity .4s ease", textAlign: "center", font: "300 18px var(--font)" } });
        ov.innerHTML = `<div><div class="boot-spinner" style="margin-bottom:18px"><i></i><i></i><i></i><i></i><i></i></div><div>Launching <b style="font-weight:600">${g.title}</b>&hellip;</div></div>`;
        document.body.appendChild(ov);
        requestAnimationFrame(() => ov.style.opacity = "1");
        setTimeout(() => { window.location.href = g.url; }, 950);
    }

    /* ------------------------------------------------------- ambient nags -- */
    function scheduleNags() {
        setTimeout(() => toast({
            kind: "info", title: "Meridian Update Assistant", body: "3 important updates are ready to install.", ttl: 9000,
            action: { label: "Install", fn: fakeUpdate }
        }), 7000);
        setTimeout(() => showDialog({
            title: "Activate Meridian Office", icon: "info",
            html: "Your Meridian Office Suite trial expires in <b>3 days</b>.<div class='sub'>Activate now to keep your synergies flowing without interruption.</div>",
            buttons: [{ label: "Remind me later", value: 0 }, { label: "Activate", value: 1, primary: true }],
        }).then(v => { if (v) toast({ kind: "warn", title: "Activation", body: "Could not reach the licensing server. Please try again later." }); }), 24000);
        setTimeout(() => toast({ kind: "warn", title: "OneVault Backup", body: "Last successful backup: 14 days ago. Consider backing up now." }), 46000);
        setTimeout(() => toast({ kind: "info", title: "Wellness reminder", body: "You've been productive for a while. Consider a 5-minute mindfulness break." }), 78000);
    }
    function fakeUpdate() {
        showDialog({
            title: "Installing updates", icon: "info",
            html: "Please do not turn off your computer.<div class='sub' id='upst'>Downloading update 1 of 3…</div><div class='dlg-progress'><i></i></div>",
            buttons: [{ label: "Hide", value: null }],
        });
        const bar = $("#dialog-layer .dlg-progress i"); let p = 0, step = 1;
        const t = setInterval(() => {
            p += 6 + Math.random() * 9; if (bar) bar.style.width = Math.min(p, 100) + "%";
            const st = $("#upst"); if (st) st.textContent = p < 100 ? `Downloading update ${Math.min(3, Math.ceil(p / 34))} of 3…` : "Finalizing…";
            if (p >= 100) { clearInterval(t); dlgLayer.classList.add("hidden"); dlgLayer.innerHTML = ""; toast({ kind: "ok", title: "Updates installed", body: "A restart is recommended to complete installation." }); }
        }, 320);
    }

    /* ------------------------------------------------- shutdown -> game ---- */
    async function shutdown(kind) {
        const verb = kind === "restart" ? "Restart" : kind === "out" ? "Sign out" : "Shut Down";
        const ok = await showDialog({
            title: verb, icon: "question",
            html: `${verb} Meridian Workspace now?<div class='sub'>Any unsaved changes will be lost.</div>`,
            buttons: [{ label: "Cancel", value: false }, { label: verb, value: true, primary: true }], escValue: false,
        });
        if (!ok) return;
        // "Shutting down" overlay, then return to the game.
        const ov = el("div", { style: { position: "fixed", inset: "0", zIndex: "9999", background: "radial-gradient(900px 600px at 50% 40%, #16223f, #080d1f)", color: "#cfe0ff", display: "grid", placeItems: "center", opacity: "0", transition: "opacity .4s ease", textAlign: "center", font: "300 18px var(--font)" } });
        ov.innerHTML = `<div><div class="boot-spinner" style="margin-bottom:18px"><i></i><i></i><i></i><i></i><i></i></div><div>${kind === "out" ? "Signing out" : kind === "restart" ? "Restarting" : "Shutting down"}…</div><div style="margin-top:8px;font-size:12px;color:#7c93bd">Please wait while Meridian Workspace closes.</div></div>`;
        document.body.appendChild(ov);
        requestAnimationFrame(() => ov.style.opacity = "1");
        setTimeout(() => { ov.style.background = "#000"; ov.innerHTML = ""; }, 1600);
        setTimeout(() => { window.location.href = RETURN_TO_GAME; }, 2100);
    }

    // Keyboard nicety: Esc closes Start menu when nothing else handles it.
    document.addEventListener("keydown", e => { if (e.key === "Escape" && dlgLayer.classList.contains("hidden")) toggleStart(false); });
})();
