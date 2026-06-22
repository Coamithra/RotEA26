# Revenge of the Evil Aliens — Web Port

Reviving a 2008 **Xbox Live Indie Game** (XNA 3.x / C#) as a browser-playable game.
The original source was lost; it was recovered by decompiling the shipped Xbox 360
package, and is being ported to run in the browser via **KNI** (an XNA-4.0-compatible
engine with a Blazor WebAssembly / WebGL backend) — output is a static site for
GitHub Pages.

## Status

- ✅ Toolchain proven (KNI → Blazor → WebGL renders in-browser)
- ✅ The full ~40k-line game **compiles to WebAssembly** (0 errors)
- ⬜ Content pipeline · first boot · shaders · audio · saves · hosting · polish

**See [plans/plan.md](plans/plan.md) for the archived multi-stage development plan** — a historical record of how each
stage was built, kept for reference. Active task tracking now lives on a local Trello
board, not in this repo.

## Layout

| Path | What |
|---|---|
| `web/EvilAliensWeb/` | the port (KNI Blazor WASM project) |
| `web/EvilAliensWeb/Compat/` | XNA-3.x→4.0 + Xbox-API compatibility shims |
| `src_decompiled/` | decompiled reference source (read-only) |
| `extracted/584E07D1/Content/` | game assets unpacked from the Xbox package |
| `tools/` | reproducible Python fix-up scripts |

## Build & run

```sh
cd web/EvilAliensWeb
dotnet build -c Debug
dotnet run -c Debug --urls http://localhost:5280   # then open the URL
```

Requires the .NET 8 SDK with the `wasm-tools` workload
(`dotnet workload install wasm-tools`).
