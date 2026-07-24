---
name: cleanup
description: Tell every other running Claude Code session to close the Chrome tabs and windows it opened and no longer needs. Use when the user is drowning in tabs, says "clean up the tabs", or invokes /cleanup. Broadcasts a housekeeping message to each live session; does not touch their work.
argument-hint: "[here] — 'here' limits the broadcast to sessions in this repo (default: every running session)"
---

# Tab Cleanup Broadcast

Sessions leave Chrome tabs behind. A dozen parallel worktree sessions each doing
visual verification against `localhost:528N` turns into a browser you can't read the
favicons in. This skill sweeps your own tabs, then asks every other live session to
sweep theirs.

There is no broadcast primitive — `mcp__ccd_session_mgmt__send_message` takes one
`session_id` per call, so this is a fan-out.

## Know this before you run it: delivery is end-of-turn

A queued message is processed **after the target's in-flight turn finishes** — the whole
agentic response, not the current tool call. A session working a card is inside one turn
from research through PR. So the broadcast does not clean anything up *now*; each session
tidies whenever it next goes idle, which is roughly when it stops opening tabs anyway.

That makes this skill eventually-consistent housekeeping, not a panic button. If the user
wants tabs gone immediately, don't run it — say so and clean up directly from this session
instead (see Step 1 and the Notes). If the user wants this to happen reliably without
being asked, a `Stop` hook in `.claude/settings.json` fires at the same moment the queued
message would have been read, minus the wait and minus the fan-out; the `update-config`
skill covers wiring one up.

## Step 1: Clean Up Your Own Tabs First

Before asking anyone else, do it yourself. Close the Chrome tabs and tab groups this
session opened for verification and no longer needs (`mcp__claude-in-chrome__tabs_context_mcp`
→ `tabs_close_mcp`), plus any in-app Browser-pane tabs (`mcp__Claude_Browser__tabs_context`
→ `tabs_close`).

Keep anything the current task is still mid-loop on. Leave dev/preview servers running —
this skill is about tabs and windows, not servers.

If this session has running subagents that opened tabs, message them too via `SendMessage`
(one call per agent, same fan-out shape, same message body as Step 3).

## Step 2: Find the Targets

```
mcp__ccd_session_mgmt__list_sessions   # limit: 50
```

Filter to `isRunning: true`. The current session is already excluded by the tool.

**Scope**, from the argument:

- No argument (default) — **every** running session. The tab pile is machine-wide, not
  repo-scoped, so this is usually what the user means.
- `here` — only sessions whose `cwd` starts with `C:\Programming\RotEA26`. That prefix
  covers the per-card worktrees too, since they live at
  `.claude\worktrees\wt<N>` inside the repo.

If nothing is running, say so and stop. Don't send to non-running sessions — they'd
process the message on next resume, long after it's useful.

Show the user the target list (title + short session id) before sending, so a surprise
target is visible. The `/cleanup` invocation is the authorization; don't block on a
second confirmation.

## Step 3: Send

Probe with **one** send first, confirm it queues, then batch the rest. Per the global
sibling-cascade rule, if one call in a parallel batch errors the harness cancels its
siblings — so for a large fleet, send in groups rather than all at once.

Message body, verbatim:

```
Housekeeping request from the user, relayed to every running session: they're drowning in Chrome tabs.

When you reach a safe point in your current task, please close any Chrome tabs and windows YOU opened and are no longer using, and delete any tab group you created for your task. Same for in-app Browser-pane tabs.

Guardrails:
- Keep anything you're still actively using (a verification tab you're mid-loop on, a dev server preview you still need).
- Don't touch tabs you didn't open — those are probably the user's.
- If you're unsure whether a tab is still needed, leave it.
- Leave dev/preview servers running; this is about tabs and windows only.

Don't derail or abandon your current work for this — just tidy up when it's convenient, then carry on.
```

The guardrails are load-bearing, not padding. Sessions running a two-tab co-op test, a
side-by-side visual diff, or a live tuning panel legitimately need their tabs, and a
session that reads "close unused tabs" without them may cheerfully close the user's own
research tabs. Don't trim the body.

## Step 4: Report

Table of who it went to (title + session id), what you closed yourself, and this caveat:
messages are queued and land only after each session's in-flight turn finishes, nothing
reports back, and the tab count is the only feedback. Offer to re-run `list_sessions`
later to see who's still alive.

## Notes

- Delivery is best-effort. The tool queues the message and returns; a session that dies
  mid-turn never processes it. Re-running the skill is safe — the message is idempotent.
- Unattended sessions (scheduled-task and remote-dispatched runs) can neither send nor
  receive. They're excluded automatically; don't try to work around it.
- The message arrives in the target as a user turn labelled "From <this session's title>"
  with a link back. Give this session a recognisable title first if you want the recipients
  (and the user reading their transcripts later) to know where it came from.
- This is housekeeping, not orchestration. Don't extend the body to assign work, ask for
  status, or coordinate tasks across sessions — that's what the cards and the contributing
  runbook are for.
