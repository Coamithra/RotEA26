# Texture pipeline: padtest canary gate + two papercuts (Trello 06c6c741)

Delete this file when the card ships.

## Context

Three independent problems in `tools/textures/`, one PR because they are the same two files.

1. **The build could silently strip the shipped over-pad canary.** Card `51bdd4a9` (PR #188) added a
   final `check_pad_bleed.run()` gate to `build_textures.py`. The cold review noted it guards the
   *second*-worst footgun: `tools/CLAUDE.md` says the committed `.dds` deliberately carry the
   `--padtest 100` over-pad canary, but `--padtest` DEFAULTS TO 0, so a plain
   `python tools/textures/build_textures.py` stripped it off every texture it touched, the diff read
   as a harmless size win, and the new gate then printed a reassuring "ok: all 124 replicate their
   logical edge" over the top of it.
2. `write_manifest_cs` wrote LF into a CRLF working copy, so every run left
   `Compat/PrecompiledTextures.cs` modified in `git status` with an empty content diff.
3. `build_texviewer.py --only` lowercased the pattern and treated a no-match as a silent success.

## Design

### 1. The canary gate (`check_canary`, a PREFLIGHT)

Runs in `main()` over the **selected** entries, after `--only` filtering and **before the build
loop**. Firing before the build is the design choice: nothing bad reaches the working tree (no
revert dance), and the post-build all-clear can never vouch for a run that just dropped the canary.

The rule compares **over-pad** (`padded - pad4(logical)`), never padded dims:

```
pad_over(pw, ph, lw, lh) -> (max(0, pw - pad4(lw)), max(0, ph - pad4(lh)))
FLAG when the .dds on disk has more over-pad than this run would write, on either axis.
```

Measured: all 124 committed `.dds` read exactly `+100/+100` under that measure. Padded dims also
move when the SOURCE PNG is resized, so a padded-dims rule flags a legitimate rebuild of a shrunken
sprite whose canary is intact -- that is the negative control in the selftest.

- **A NEW asset is exempt by construction**: no `.dds` on disk means no prior over-pad to compare
  against. It gets a non-fatal `NOTE` instead when the pad it would be built at differs from the one
  the rest of the fleet agrees on, so the exemption is not a silent hole.
- **Opt-out: `--drop-canary`.** Prints a loud line and proceeds. This is what card `f2621e52`
  (the eventual ship rebuild at `--padtest 0`) needs; without it that card would be impossible.
- **`--dry-run` fires the gate too** -- a dry run that prints a plan for a command that would abort
  has predicted the wrong outcome.
- `build_dxt` and the preflight share one `target_dims()` so the prediction cannot drift from what
  the build actually writes.
- **`--selftest`** pins the rule against an 11-row case table plus the padded-dims negative control,
  in the style of `check_pad_bleed.py --selftest`. No config, no `.dds`, no texconv.

### 2. `write_generated(path, text, dry)`

Writes only when the bytes would change, in the file's OWN line endings (CRLF preserved; a new file
gets LF and git's checkout filter owns the local flavour). Both halves are required: preserving the
endings alone still rewrites (bumping mtime -> needless MSBuild rebuild) when nothing changed, and
skipping on equal content alone never matches, because rendered LF text never equals a CRLF file.
Covered by the same `--selftest`.

### 3. `build_texviewer.py --only`

Hard-fails on no match, like `build_textures.py`. The `.lower()` is dropped for symmetry, but note
that is **cosmetic on this Windows-only toolchain**: `fnmatch.fnmatch` normcases both sides, so an
uppercase pattern already matched. The error is the part that changes behaviour.

## Verification (fully offline; no browser, no game, no dev server)

| # | Check | Result |
|---|---|---|
| 1 | `build_textures.py --selftest` | 28/28 ok |
| 2 | 10 mutants of the rule, the gate and the write policy | all killed (5/1/1/3/2/1 + 4/2/3/1) |
| 3 | `check_pad_bleed.py --selftest` and full 124-asset sweep | ok (after the `read_dds` header split) |
| 3b | manifest damaged, then a canary-refused build | manifest NOT rewritten (the gate precedes every write) |
| 4 | `--only gfx/base/756-v1 --padtest 100` | builds, `check_pad_bleed` green, `.dds` byte-identical to the committed one |
| 5 | `--only gfx/base/756-v1` (bare) | gate fires, exit 1, `git status` clean |
| 6 | `--dry-run` bare | gate fires, exit 1 |
| 7 | `--dry-run --drop-canary` | passes, loud line, exit 0 |
| 8 | `.dds` hidden -> asset looks NEW | exempt (exit 0) + `NOTE`; silent at `--padtest 100` |
| 9 | `--manifest-only` on a clean tree | `(unchanged)`, `git status` clean (was: modified, empty diff) |
| 10 | hand-damaged manifest -> `--manifest-only` | rewritten correctly, 143 CRLF / 0 LF, git clean |
| 11 | `build_texviewer.py --only 'gfx/sprutes/*'` | hard fail, exit 1 |
| 12 | `build_texviewer.py --only 'gfx/base/756-v*'` | `[only] 6 of 154`, exit 0 |

## Out of scope

- No shipped `.dds` committed (the one rebuild reproduced byte-identically anyway).
- Card `f2621e52`'s actual `--padtest 0` ship rebuild -- this only makes it expressible.
- `check_pad_bleed`'s tolerance rule, constants and wording.
- Case-sensitivity of `--only` in either script (`fnmatchcase` would make texviewer stricter than
  `build_textures.py`, the opposite of aligning them).
- No C# or game changes; `PrecompiledTextures.cs` content is byte-for-byte what it was.
