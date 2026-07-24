# Remove comment noise from the decompilation pass

Card `432a31e9`.

## Context

`web/EvilAliensWeb/Game/` was derived from `src_decompiled/` (ILSpy output of the recovered
Xbox build). ILSpy annotates every construct it could not type-resolve with a per-IL-offset
warning comment. Those comments rode along into `Game/` and are now pure noise: they say
nothing about the code, they bury the hand-written porting comments that *do* matter, and
they cost tokens on every read.

**4020 lines across 130 files** — about one in every twenty lines of `Game/`.

## What is actually there

Every artifact comment matches `^\s*//IL_<hex>: <message>` with exactly four messages:

| Count | Message |
|---|---|
| 3964 | `Unknown result type (might be due to invalid IL or missing references)` |
| 38 | `Expected O, but got Unknown` |
| 15 | `Invalid comparison between Unknown and I4` |
| 3 | `Expected I4, but got Unknown` |

They are ILSpy's "this expression's type came back as `Unknown`" markers — emitted wholesale
because the decompile ran without the XNA 3.1 reference assemblies, so every `Vector2` /
`Color` / `Rectangle` expression tripped one. They carry no information about the port.

Two things are **not** noise and are deliberately left alone:

- **57 bare `//` lines.** Every one is a paragraph separator inside a hand-written block
  comment (`BrainAura.cs`, `ProceduralStarfield.cs`, `Blast.cs`, ...). Not decompiler output.
- **`src_decompiled/`.** It is the read-only reference copy of the raw ILSpy output; it stays
  verbatim so the derivation is still auditable.

## Design

One mechanical pass, `tools/strip_il_comments.py`, kept in `tools/` alongside the other
already-applied derivation scripts (`fix_apis*.py`, `fix_ctors.py`, ...) as the record of what
was done. It walks `web/EvilAliensWeb/Game/**/*.cs` and:

1. **Drops** any line that is *only* an IL comment (4017 lines).
2. **Trims** the comment off the 3 lines where an earlier pass glued one onto real code
   (`}//IL_0002: ...` in `StorageDeviceManager.cs` ×2, `SubMenuAwardments.cs` ×1) — the `}`
   stays, the comment and its trailing whitespace go.
3. **Collapses** runs of 2+ blank lines to 1. There are exactly 3 such runs in the whole tree
   and all 3 are debris from those same glued lines, so this touches nothing else.

Preserves CRLF line endings and UTF-8-without-BOM encoding (43 of the 130 files are
non-ASCII).

Safe as a line-based transform, on both code paths:

- **Dropping** a whole line could only misfire if a `//IL_`-looking line were string content;
  that needs a multi-line verbatim string, and `Game/` has zero of them.
- **Trimming** a trailing comment is the riskier path — it fires on any line merely
  *containing* `//IL_`, so a single-line literal such as `"//IL_0001: x"` would be truncated
  along with everything after it on that line. Checked: the only 3 lines in `Game/` that
  contain `//IL_` without starting with it are the glued `}//IL_...` braces.

## Verification

No behavioural change is possible — C# comments are inert — so the gate is *proving* nothing
but comments moved:

1. **Diff shape**: `git diff` must be deletions only, apart from the 3 `}//IL...` → `}` lines.
2. **Comment-blind equivalence**: strip *all* `//`-comment lines and blank lines from every
   touched file before and after; the two must be byte-identical.
3. **`grep -rc '//IL_' Game/` == 0.**
4. Clean `dotnet build -c Debug`.
5. Boot the game in real Chrome (dev server, port 5287) — renders, zero console exceptions.
   This is the "final smoke check" case from root `CLAUDE.md`: no purpose-built tool applies
   to a comment-only change.

## The card's second question: what else did decompilation wreck?

Answered here so it does not have to be re-derived:

- **Preserved** (they live in assembly metadata, so ILSpy recovers them exactly): namespaces,
  type names, method names, **parameter names**, field names, property names, event names,
  enum member names, constant values, string literals.
- **Lost**: **local variable names.** The recovered binary is a Release build with no PDB, so
  every local was renamed by ILSpy to `num`, `num2`, `val`, `flag`, `array`, `text`, ...
  Rough census in `Game/`: ~1560 `num*`, ~650 `val*`, ~380 `flag*`, ~330 `text*`, ~106
  `array*`.
- **Also lost**: the original comments, `#region`s, and formatting (all compile-time), plus
  anything under `#if WINDOWS` / `[Conditional]` — see root `CLAUDE.md`.

Renaming ~3000 locals is a large, judgement-heavy, per-method job with real regression risk,
so it is **out of scope** for this card and filed as a follow-up.

## Out of scope

- Renaming decompiled locals (follow-up card).
- Touching `src_decompiled/`.
- Removing or rewording any hand-written comment.
