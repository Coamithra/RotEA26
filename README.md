# Revenge of the Evil Aliens — Web Port

Reviving a 2008 **Xbox Live Indie Game** (XNA 3.x / C#) as a browser-playable game.
The original source was lost; it was recovered by decompiling the shipped Xbox 360
package, and is ported to run in the browser via **KNI** (an XNA-4.0-compatible
engine with a Blazor WebAssembly / WebGL backend) — a static site deployed to
GitHub Pages.

**Play it live:** https://coamithra.github.io/RotEA26/

## Status

- ✅ **Single-player game is complete and live.** Content pipeline, first boot,
  shaders, audio (XACT tracks re-decoded, speech re-cast), saves (localStorage),
  GitHub Pages hosting, the unified hi-res render path, custom in-game font, a
  sci-fi menu reskin, laser VFX, and the YouTube trailer embeds are all shipped.
- 🚧 **Online co-op multiplayer (couch co-op over the network)** is playable on the
  live site — distributed-authority state replication over WebRTC, *not* lockstep
  (design: [`plans/stage11-online-coop.md`](plans/stage11-online-coop.md)). Shipped:
  the replication skeleton, ship mirroring, host world authority (enemy puppets,
  kill claims, score sync), level-script/checkpoint/victory replication, and the
  real WebRTC transport with a room-code signaling server on a VPS. Two people on
  the live site can pair with **Host / Join by code** today, and local couch co-op
  works alongside it. There is no TURN relay yet, so a minority of NAT pairs will
  fail to connect. The "Join Online Game" browser (pick a public game from a list)
  is merged but not yet in the published build — that needs a Pages redeploy.

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

Deploys to GitHub Pages are manual (`workflow_dispatch` on
[`.github/workflows/deploy.yml`](.github/workflows/deploy.yml)), not automatic on
push to `main`.
