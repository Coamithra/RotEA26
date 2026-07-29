# Revenge of the Evil Aliens — Web Port

Reviving a 2008 **Xbox Live Indie Game** (XNA 3.x / C#) as a browser-playable game.
The original source was lost; it was recovered by decompiling the shipped Xbox 360
package, and is ported to run in the browser via **KNI** (an XNA-4.0-compatible
engine with a Blazor WebAssembly / WebGL backend) — output is a static site.

**Play it live:** https://coamithra.github.io/RotEA26/ — that is the current public
build, published 2026-07-23 and by now hundreds of commits stale. Hosting is
mid-migration to https://haraldmaassen.com/RotEA26/, which does not exist until the
first deploy runs there; see [`docs/DEPLOY.md`](docs/DEPLOY.md).

## Status

- ✅ **Single-player game is complete and live.** Content pipeline, first boot,
  shaders, audio (XACT tracks re-decoded, speech re-cast), saves (localStorage for
  the settings/unlockables XML, IndexedDB for the level-select screenshots),
  hosting, the unified hi-res render path, custom in-game font, a sci-fi menu
  reskin, laser VFX, and the YouTube trailer embeds are all shipped.
- 🚧 **Online co-op multiplayer (couch co-op over the network)** — distributed-authority
  state replication over WebRTC, *not* lockstep
  (design: [`plans/stage11-online-coop.md`](plans/stage11-online-coop.md)). Merged:
  the replication skeleton, ship mirroring, host world authority (enemy puppets,
  kill claims, score sync), level-script/checkpoint/victory replication, the real
  WebRTC transport with a room-code signaling server on a VPS, and the "Join Online
  Game" browser (pick a public game from a list). Local couch co-op works alongside
  it. There is no TURN relay yet, so a minority of NAT pairs will fail to connect.
  **Build from source to get all of it:** the published build has **Host / Join by
  code** but predates the game browser and the hardening after it, and peers only
  ever see each other when both are on the same published build — so the gap closes
  at the next deploy, not at merge.

**See [plans/plan.md](plans/plan.md) for the archived multi-stage development plan**
(historical record of how each stage was built — it predates the menu reskin,
trailers, and all of the multiplayer work above, so treat it as background, not
current status). Active task tracking lives on a local Trello board, not in this
repo; see [`CLAUDE.md`](CLAUDE.md) for the dev workflow if you're contributing.

## Layout

| Path | What |
|---|---|
| `web/EvilAliensWeb/` | the port (KNI Blazor WASM project) — `Game/` is the ported game code, `Compat/` the XNA-3.x→4.0 + Xbox-API shims, `Compat/Net/` the multiplayer/replication layer |
| `web/DevServer/` | no-cache dev static host — run this, not the WASM client directly |
| `src_decompiled/` | decompiled reference source (read-only) |
| `extracted/584E07D1/Content/` | game assets unpacked from the Xbox package |
| `tools/` | reproducible Python asset pipelines (audio, shaders, textures/DXT, font, ...) that generated `Game/` and `wwwroot/Content` |

## Build & run

```sh
dotnet build web/EvilAliensWeb -c Debug
dotnet run --project web/DevServer -c Debug --urls http://localhost:5280   # then open the URL
```

Requires the .NET 8 SDK with the `wasm-tools` workload
(`dotnet workload install wasm-tools`).

Serve via `web/DevServer`, not `dotnet run` directly on the WASM client — the
dev server disables caching so assets never go stale while iterating; see
[`CLAUDE.md`](CLAUDE.md) for why.

**Nothing deploys on merge.** Publishing is a separate manual act, and there are
two of them (the static site and the signaling server) — the runbook is
[`docs/DEPLOY.md`](docs/DEPLOY.md). `python tools/deploy_web.py` ships the site to
the Hetzner host; the still-live GitHub Pages route is a `workflow_dispatch` run of
[`.github/workflows/deploy.yml`](.github/workflows/deploy.yml), and stays that way
until the first Hetzner deploy is verified.
