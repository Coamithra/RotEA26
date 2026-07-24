# `?noattract` out of `DebugFlags.Active`

Card `af63f958`. Branch `fix/noattract-outside-active`, worktree `wt6` (port 5286).

## Context

A joiner going through the real menu path (Online Co-op → Join Online Game) must boot
FLAG-CLEAN: `NetSession.HandleHello` rejects on
`menuSession && (peer debug bit || DebugFlags.Active)` ([NetSession.cs:1257](web/EvilAliensWeb/Compat/Net/NetSession.cs:1257)),
and the joiner IS a menu session, so its own `Active` bit rejects its own pairing.
`?noattract` sits in the `Active` expression
([DebugFlags.cs:2003](web/EvilAliensWeb/Compat/DebugFlags.cs:2003)), so a joiner cannot pass it —
and its main menu therefore keeps getting yanked into the idle attract demo mid-navigation.
That is documented as "JIP pass trap 2" in `web/EvilAliensWeb/CLAUDE.md` and made the
two-window JIP pass substantially harder to drive.

## What `?noattract` actually does

Exactly one thing: `MenuScene.Initialize` skips
`mainMenu.OnTimeOut += mainMenu_DemoSelected` ([MenuScene.cs:255](web/EvilAliensWeb/Game/EvilAliens/MenuScene.cs:255)).
It is the ONLY read of `DebugFlags.NoAttract` in the tree. It cannot touch gameplay,
difficulty, unlocks, score or fairness — which is precisely what the `Active` gate exists
to police. It belongs with `?hitboxes` / `?metalscore` / `?slowmotrail` / the Juice knobs,
which are already out of `Active` for exactly this reason.

## Safety check (the card asks for it explicitly)

`DebugFlags.Active` has exactly three consumers:

| Consumer | Effect | Impact of dropping `NoAttract` |
|---|---|---|
| `NetSession.LocalHelloFlags()` ([:634](web/EvilAliensWeb/Compat/Net/NetSession.cs:634)) | sets the `HelloFlagDebugActive` bit | a `?noattract` boot stops advertising itself as debug-flagged — correct, it isn't |
| `NetSession.HandleHello` ([:1257](web/EvilAliensWeb/Compat/Net/NetSession.cs:1257)) | menu-session pairing reject | a `?noattract` joiner can now pair — the point of the card |
| `NetListing.ComputeEligible` ([:146](web/EvilAliensWeb/Compat/Net/NetListing.cs:146)) | a debug-flagged host refuses to list | a `?noattract` host may now list |

No cheating boot slips through: every gameplay-affecting flag stays in the expression
(`SkipSplash` `AutoStart` `Level` `UnlockAll` `Invuln` `Harness` the showcase/fast-boot flags
`NetRole` `AIPlayer` `NetScript` `GameBrowser` `NetJip` `NetKickShot` `AiFastForward`), and
`Settings.CheckForCheats()` independently covers Turbo/Friends in `ComputeEligible`.

The third row is worth stating plainly: a `?noattract` single-player game becomes listable.
That is *desirable and strictly conservative* — `ComputeEligible` already refuses to list
while the level is `Demo1/2/3` ([NetListing.cs:136](web/EvilAliensWeb/Compat/Net/NetListing.cs:136)),
i.e. while an attract demo is running. `?noattract` only makes that state less reachable;
it can never make a demo listable, and a listed `?noattract` host offers a joiner a game
identical to a listed unflagged host's.

## Design

`web/EvilAliensWeb/Compat/DebugFlags.cs`
- Drop `NoAttract ||` from the `Active` expression, with a comment stating why it is
  deliberately out — it is the flag a menu-session joiner must be able to pass, and it
  unwires one menu hook rather than altering play. (Mirrors the existing comment above the
  expression that justifies why the level fast-boots ARE in.)
- Keep `noAttract=` in the `[debug] flags active` line: `?level=X&noattract` still reports it.
- `Hint()`: "no debug flags" → "no boot-hijacking debug flags". A bare `?noattract` boot now
  falls into this branch, and the old wording flatly contradicts the URL. This was already
  inaccurate for the whole out-of-`Active` class (`?hitboxes`, `?shake=`, `?wallfog=` …); the
  new wording is honest for all of them and makes "flag-clean joiner" the console's own verdict.
- Header comment for `?noattract`: note it is deliberately out of `Active` so an online
  joiner can pass it.

`web/EvilAliensWeb/CLAUDE.md`
- "JIP pass trap 2": the claim "the joiner cannot pass `?noattract`" is now false. Add
  `?noattract` to the flags open to a joiner and drop the attract-demo consequence sentence,
  keeping the trap itself (the flags that *do* reject are unchanged, `?netsim` included).
- JIP pass recipe: add `&noattract` to the joiner's URL.
- The `DebugFlags.Active` convention bullet (~line 145): list `?noattract` as an
  out-of-`Active` example, since it is the one whose exclusion is load-bearing for net tests.

Nothing else changes. No new flag, no new API, no protocol change (the hello flags byte's
*meaning* is unchanged — only whether a `?noattract` boot sets it).

## Verification

The change is a boot-time predicate, so it is verified as DATA, not as a frame.

1. `dotnet build -c Debug` clean.
2. `?noattract` (bare) in real Chrome → console prints the hint line (`Active == false`),
   NOT `[debug] flags active`. That IS the reject-expression input: `HandleHello`'s
   `DebugFlags.Active` term is what rejected the joiner, so proving it false proves the fix.
3. `?level=Level1&noattract` → `[debug] flags active: … noAttract=True …` still prints
   (`Level` keeps it Active; the readout did not regress).
4. Behaviour regression check, both static-state (no motion timing involved):
   - `?menu&noattract`, idle > 20s → still on the main menu (suppression still works).
   - `?menu` alone, idle > 20s → attract demo starts (default wiring untouched).
5. Zero console exceptions on every boot above.

## Out of scope

- The other four JIP pass traps (window visibility, the busy-slot grant desync, the local
  signaling rig, picking a non-ending host fight) — separate cards.
- Re-running the two-window JIP pass itself (card `c0398370` already ran it; this card only
  removes one obstacle from it).
- Auditing the rest of the `Active` expression. Card `5186df83` (in flight, worktree `wt13`,
  branch `fix/fastboot-flags-active`) is adding `?spiders`/`?marsboss` to this same
  expression — adjacent, and a likely textual merge conflict on that one line. Resolution
  rule if it lands first: keep their additions, re-apply only the `NoAttract ||` removal.
