"""Show the SEMANTIC diff of a refactor, by decompiling both assemblies and diffing the C#.

The companion to verify_il_identical.py, for the changes that oracle cannot cover.

verify_il_identical.py proves a change is invisible to the compiler. That covers renames, and
(with --optimize) a deleted dead temporary -- but it is all-or-nothing, and some genuinely
behaviour-preserving refactors DO change the emitted code:

  * collapsing `bool num = held; held = num | X;` to `held |= X` reads `held` at a different
    point, so the ldloc moves across the neighbouring struct-property read;
  * collapsing four `powerup.Position - base.Position` recomputations into one local removes
    three get_Position calls -- Roslyn cannot prove a property getter is pure, so it never CSEs
    them away by itself.

For those, the question is not "is the assembly identical" but "is the difference CONFINED to
the methods I edited, and is it the difference I intended". This script answers that.

Why decompiled C# rather than raw IL: deleting a local RENUMBERS every later local slot in that
method, so a raw IL diff of a 360-line method reports hundreds of changed lines and drags in
code you never touched (measured: 317 changed IL lines in PlayerShip.DoAIMove, most of it
wall-navigation code that was untouched -- the diff aligner simply mispaired `ldloc.s 53` with
`ldloc.s 51`). Decompiling first makes slot numbers vanish, because ILSpy re-derives names from
structure. The same change reads as 82 lines of exactly the intended edits.

Read the output as a REVIEW AID, not a proof. It shows what moved; a human decides whether that
was intended. Prefer verify_il_identical.py whenever the change is supposed to be invisible --
a byte-identical hash is strictly stronger than reading a diff.

CAVEAT, and it is a sharp one: ILSpy NORMALISES. Its transform pipeline rewrites recognised
patterns back to idiomatic C#, so a difference the compiler really did emit can decompile to
identical source and silently vanish from this diff. Measured on card 0c624f9d: collapsing
`bool num = held; held = num | X;` to `held |= X` genuinely moves the `ldloc` in the IL, yet
both render as `held |= X` here and the method does not appear in the report at all. Treat an
absent method as "ILSpy considers these the same construct", never as "the IL is identical" --
verify_il_identical.py is the only tool that answers the latter. Corollary: this decompiles the
assembly IN PLACE, in its build output directory, so ILSpy can resolve its references. Copying
the dll somewhere isolated first makes ILSpy fall back to unresolved-type output
(`((GamePadState)(ref state)).Buttons`) with its transforms disabled -- a different, noisier
answer that is easy to mistake for a real finding.

Both sides build optimized, for the same reason --optimize exists over there: unoptimized output
keeps every dead store, which buries the real change in noise.

Needs `ilspycmd` on PATH (see root CLAUDE.md -- run as `DOTNET_ROLL_FORWARD=LatestMajor`; this
script sets that itself).

Usage, from anywhere in the repo:

    python tools/verify_decompiled_diff.py --ref main     # a whole branch vs its branch point
    python tools/verify_decompiled_diff.py                # uncommitted edits vs HEAD
    python tools/verify_decompiled_diff.py --ref main --full   # print the whole diff, not a summary

Exits 0 when the decompiled C# is identical, 1 when it differs (expected for this class of
change -- a 1 is "now go read it", not a failure), 2 on a build or plumbing failure.
"""
import argparse
import collections
import difflib
import io
import os
import re
import shutil
import subprocess
import sys
import tempfile

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import verify_il_identical as ili

# ILSpy stamps every method with the source IL offsets it could not type. They shift whenever
# anything above them in the method changes size, so they are pure noise for this comparison.
IL_COMMENT_RE = re.compile(r'^\s*//IL_[0-9a-fA-F]+')
# `} // end of method Class::Name` does not exist in C# output; types/methods are found instead
# by the decompiler's own declaration lines. Attribution has to cover CONSTRUCTORS, PROPERTIES
# and EXPLICIT INTERFACE IMPLEMENTATIONS as well as plain methods: a `modifiers returnType Name(`
# pattern alone matches none of those, so a changed line inside one gets blamed on whichever
# method happened to be declared above it -- which turns the report's headline rule ("a member
# you did not touch appearing here is the finding") into either a false alarm or, worse, a real
# change hiding behind a familiar name.
MODIFIERS = (r'(?:public|private|protected|internal|static|virtual|override|sealed|abstract|'
             r'partial|readonly|unsafe|extern|async|new|const|volatile)')
# At least ONE modifier is REQUIRED, and that is load-bearing, not incidental: it is the only
# thing separating a member declaration from an ordinary statement. Relaxing it to zero-or-more
# makes `else if (...)` parse as a member named `if`, and every hunk downstream gets filed under
# it. ILSpy writes an explicit access modifier on virtually everything, so requiring one costs
# almost no coverage.
MEMBER_RE = re.compile(
    r'^(?P<indent>\s*)(?:' + MODIFIERS + r'\s+)+'
    r'(?:'
    # method / property / expression-bodied member: returnType Name
    r'[\w<>\[\],.?]+\s+(?P<name>[\w<>]+)\s*(?:\(|\{|=>|$)'
    r'|'
    # constructor / finalizer: no return type, name is the enclosing type
    r'~?(?P<ctor>[\w<>]+)\s*\('
    r')')
# Explicit interface implementations carry no access modifier, so they need their own pattern.
# The dotted qualifier is most of what keeps this from matching a statement -- but NOT all of it,
# because a call statement has the same `word word.Name(` shape: `return MyMath.AngleToVector(x);`
# otherwise parses as an impl named `AngleToVector` (143 such lines in this assembly), and every
# hunk after it files under that phantom. This is the same trap the mandatory modifier guards
# against in MEMBER_RE, and it needs its own guard here because a modifier cannot be required.
# The return type must START with a word character: the trailing class allows `?`, `,` and `.` for
# nullable/array/generic/qualified types, and without the anchor a wrapped continuation line
# (`? MyMath.AngleToVector(x)`) matches as a member -- one that STATEMENT_HEAD_RE cannot see,
# because its head is punctuation rather than a keyword.
EXPLICIT_IMPL_RE = re.compile(
    r'^(?P<indent>\s*)\w[\w<>\[\],.?]*\s+[\w<>]+(?:\.[\w<>]+)*\.(?P<name>[\w<>]+)\s*(?:\(|\{|=>)')
# The first token of that pattern is a RETURN TYPE, so it can only be an identifier or a builtin
# type keyword. Any other keyword there means the line is a statement, not a declaration.
STATEMENT_HEAD_RE = re.compile(
    r'^\s*(?:return|else|yield|throw|await|case|lock|using|fixed|checked|unchecked|new|do|goto|'
    r'break|continue|while|for|foreach|switch|if|try|catch|finally|base|this)\b')
TYPE_RE = re.compile(r'^(?P<indent>\s*)(?:public|private|protected|internal|sealed|abstract|'
                     r'static|partial|\s)*(?:class|struct|record|interface|enum)\s+([\w<>]+)')
# A closing brace at or above a member's or a type's own indent ends it, so the next changed line
# is not still attributed to whatever happened to precede it. TYPES NEED THIS AS MUCH AS MEMBERS:
# ilspycmd emits a nested type FIRST in its outer type's body, ahead of the fields, whatever the
# source order -- so a type scope that never closes does not mis-name an occasional trailing hunk,
# it mis-names EVERY member of the outer type (measured before the scope stack existed: 68 of this
# assembly's types, 48.8% of its decompiled lines).
CLOSE_RE = re.compile(r'^(?P<indent>\s*)\}')

EXIT_IDENTICAL = 0
EXIT_DIFFERENT = 1
EXIT_ERROR = 2


def build_dll(tree, label):
    print(f'  building {label} ...', flush=True)
    proc = ili.msbuild(tree, [])
    if proc.returncode != 0:
        sys.stderr.write(proc.stdout + proc.stderr)
        ili.die(f'BUILD FAILED for {label} -- fix the build before verifying.')
    dll = os.path.join(tree, *ili.PROJECT.split('/'), *ili.DLL_TAIL)
    if not os.path.isfile(dll):
        ili.die(f'no assembly at {dll}')
    return dll


def decompile(dll, outdir, label):
    print(f'  decompiling {label} ...', flush=True)
    env = dict(os.environ, DOTNET_ROLL_FORWARD='LatestMajor')
    proc = subprocess.run(['ilspycmd', '-o', outdir, dll],
                          capture_output=True, text=True, env=env)
    if proc.returncode != 0:
        sys.stderr.write(proc.stdout + proc.stderr)
        ili.die('ilspycmd failed -- is it installed and on PATH?')
    produced = [f for f in os.listdir(outdir) if f.endswith('.cs')]
    if not produced:
        ili.die(f'ilspycmd wrote no .cs into {outdir}')
    path = os.path.join(outdir, produced[0])
    with io.open(path, encoding='utf-8', errors='replace') as fh:
        return [l for l in fh.read().split('\n') if not IL_COMMENT_RE.match(l)]


def completes_on_one_line(line):
    """Is this declaration's whole body on this line, so that it opens no scope?

    `=> expr;` and `{ get; set; }` for a member, `enum Mode { off, on }` for a type. Leaving a scope
    open for one of those files every following declaration under it, all the way to the ENCLOSING
    type's closing brace (measured for the member case: 220 lines across 25 sites, worst run 80).
    """
    stripped = line.rstrip()
    return (line.count('{') == line.count('}')
            and (stripped.endswith(';') or stripped.endswith('}')))


def member_at(line):
    """-> (name, body_indent) for a member DECLARATION on this line, else None.

    `body_indent` is the indent whose closing brace ends the member, or None when the declaration
    completes on its own line.
    """
    m = MEMBER_RE.match(line)
    if m is None and not STATEMENT_HEAD_RE.match(line):
        m = EXPLICIT_IMPL_RE.match(line)
    if m is None:
        return None
    # `new` is both a modifier and an expression head, and it is the ONE statement head that can
    # reach MEMBER_RE. An array-initializer element (`new Color(46, 125, 201),`) therefore matches
    # the constructor alternative as a member named `Color` -- 84 such lines in this assembly. A
    # constructor can never be declared `new`, so a ctor-branch match with a statement head is
    # always an object creation.
    if m.groupdict().get('ctor') and STATEMENT_HEAD_RE.match(line):
        return None
    name = m.group('name') or m.groupdict().get('ctor')
    # A tuple type breaks the return-type character class, so
    # `private readonly List<(int index, Rectangle rect)> hits = ...;` matches the CONSTRUCTOR
    # alternative as a member named `List<`. No real member name carries a lone angle bracket.
    if name is None or name.count('<') != name.count('>'):
        return None
    return name, (None if completes_on_one_line(line) else m.group('indent'))


def enclosing(lines):
    """-> per-line 'Type.Member' attribution, so a hunk can be named.

    Types NEST, so they need a scope stack rather than one current name; a nested type's members
    read as the qualified 'Outer.Nested.Member'. Everything outside a nested type keeps exactly the
    'Type.Member' spelling it has always had.
    """
    out = []
    types = []                              # [(name, indent_len)], innermost last
    cur_member = '<declarations>'
    member_indent = None

    def attribution():
        return '.'.join([n for n, _ in types] or ['<none>']) + f'.{cur_member}'

    for line in lines:
        t = TYPE_RE.match(line)
        if t:
            indent = len(t.group('indent'))
            while types and indent <= types[-1][1]:
                types.pop()
            types.append((t.group(2), indent))
            cur_member, member_indent = '<declarations>', None
            out.append(attribution())
            if completes_on_one_line(line):
                types.pop()             # `public enum Mode { off, on }` -- no body follows
            continue
        c = CLOSE_RE.match(line)
        if c:
            indent = len(c.group('indent'))
            ends_member = member_indent is not None and indent <= len(member_indent)
            ends_type = bool(types) and indent <= types[-1][1]
            if ends_member or ends_type:
                out.append(attribution())   # the brace itself belongs to what it closes
                if ends_type:
                    while types and indent <= types[-1][1]:
                        types.pop()
                cur_member, member_indent = '<declarations>', None
                continue
        m = member_at(line)
        if m:
            cur_member, member_indent = m
            out.append(attribution())
            if member_indent is None:
                cur_member = '<declarations>'   # the declaration line WAS the whole member
            continue
        out.append(attribution())
    return out


# Every DECLARATION SHAPE the patterns have to tell apart, and every statement they must not
# mistake for one. Hand-written so the awkward cases sit next to each other, but the individual
# lines are lifted from real ilspycmd output -- the tuple-typed field, the expression-bodied
# property and event pair, the `return X.Y(...)` call, the expression-bodied explicit impl.
SHAPES_SOURCE = '''\
public class Widget
{
\tprivate int count;

\tprivate readonly List<(int index, Rectangle rect)> entryHitBounds = new List<(int, Rectangle)>();

\tprivate Vector2 lastMousePos;

\tprivate static readonly Color[] Palette = new Color[2]
\t{
\t\tnew Color(46, 125, 201),
\t\tnew Color(49, 77, 176)
\t};

\tprivate int paletteIndex;

\tpublic int Count => count;

\tpublic bool IsEntering => count == 0;

\tpublic event ExitMenu OnExit;

\tpublic string Name
\t{
\t\tget
\t\t{
\t\t\treturn "w";
\t\t}
\t}

\tpublic Widget(int n)
\t{
\t\tcount = n;
\t}

\tpublic void Step()
\t{
\t\tif (count > 0)
\t\t{
\t\t\tcount--;
\t\t}
\t\telse if (count < 0)
\t\t{
\t\t\tcount++;
\t\t}
\t}

\tpublic Vector2 Facing()
\t{
\t\treturn MyMath.AngleToVector(_direction);
\t}

\tpublic Vector2 Wrapped(bool flag)
\t{
\t\treturn flag
\t\t\t? MyMath.AngleToVector(a)
\t\t\t: MyMath.AngleToVector(b);
\t}

\tpublic struct Inner
\t{
\t\tpublic float Depth;

\t\tpublic enum Mode
\t\t{
\t\t\toff,
\t\t\ton
\t\t}

\t\tpublic void Reset()
\t\t{
\t\t\tDepth = 0f;
\t\t}
\t}

\tpublic void After()
\t{
\t\tcount = 7;
\t}

\tpublic enum Flag { none, some }

\tpublic void AfterFlag()
\t{
\t\tcount = 9;
\t}

\tOracle IOracleService.Oracle => this;

\tvoid IDisposable.Dispose()
\t{
\t\tcount = 0;
\t}
}
'''

SHAPES_EXPECT = {
    'count = n;': 'Widget.Widget',
    'count--;': 'Widget.Step',
    'count++;': 'Widget.Step',
    'count = 0;': 'Widget.Dispose',
    'return "w";': 'Widget.Name',
    # a call statement is not an explicit interface implementation
    'return MyMath.AngleToVector(_direction);': 'Widget.Facing',
    # ... nor is a wrapped continuation line, whose head is punctuation rather than a keyword
    '? MyMath.AngleToVector(a)': 'Widget.Wrapped',
    # ... while a real one, expression-bodied and modifier-less, still is
    'Oracle IOracleService.Oracle => this;': 'Widget.Oracle',
    # neither a tuple-typed field nor an array-initializer element is a constructor
    'private readonly List<(int index, Rectangle rect)> entryHitBounds = new List<(int, Rectangle)>();':
        'Widget.<declarations>',
    'new Color(46, 125, 201),': 'Widget.<declarations>',
    # ... and the declaration after each is untouched. These two are BELT, not braces: because both
    # phantoms above complete on their own line they never open a scope, so a single reverted guard
    # is caught by the needle above or by the whitelist, not here. They would catch the pair of
    # guards failing together.
    'private Vector2 lastMousePos;': 'Widget.<declarations>',
    'private int paletteIndex;': 'Widget.<declarations>',
    # a declaration that completes on its own line does not swallow the next one
    'public event ExitMenu OnExit;': 'Widget.<declarations>',
    # ... which is as true of a one-line TYPE as of a one-line member
    'public enum Flag { none, some }': 'Widget.Flag.<declarations>',
    'count = 9;': 'Widget.AfterFlag',
    # a nested type's own members are qualified by it ...
    'public float Depth;': 'Widget.Inner.<declarations>',
    'Depth = 0f;': 'Widget.Inner.Reset',
    'on': 'Widget.Inner.Mode.<declarations>',
    # ... and the outer type gets its name back afterwards, which is the whole card
    'count = 7;': 'Widget.After',
}

SHAPES_ALLOWED = {
    # the trailing line past the class' closing brace -- present because the type scope really does
    # close, which is the fault this fixture exists to catch
    '<none>.<declarations>',
    'Widget.<declarations>', 'Widget.Count', 'Widget.IsEntering', 'Widget.Name', 'Widget.Widget',
    'Widget.Step', 'Widget.Facing', 'Widget.Wrapped', 'Widget.After', 'Widget.Oracle',
    'Widget.Dispose',
    'Widget.Inner.<declarations>', 'Widget.Inner.Reset', 'Widget.Inner.Mode.<declarations>',
    'Widget.Flag.<declarations>', 'Widget.AfterFlag',
}

# VERBATIM ilspycmd output (ILSpy 8.2.0, EvilAliensWeb.dll, decompiled lines 1959-2012 = the whole
# of `AudioData`). Do not reflow, retype or tidy it: what it pins is ILSpy's own LAYOUT choice --
# the nested `StreamState` is emitted FIRST, ahead of the fields, though `AudioData.cs` declares it
# after them. That is what makes an unclosed type scope mis-attribute the entire outer type rather
# than a trailing member or two, and a hand-authored fixture would only encode our belief about it.
ILSPY_NESTED_TYPE_SOURCE = '''\
\tinternal class AudioData
\t{
\t\tpublic enum StreamState
\t\t{
\t\t\tpending,
\t\t\tplaying,
\t\t\tfadeIn,
\t\t\tfadeOut
\t\t}

\t\tprivate const int Volume_Silent = -5000;

\t\tprivate const int Volume_Normal = -1200;

\t\tprivate const float FadeSpeed = 1.5f;

\t\tprivate const int E_ABORT = -2147467260;

\t\tprivate float volume;

\t\tprivate StreamState state;

\t\tpublic StreamState State => state;

\t\tpublic AudioData()
\t\t{
\t\t\tstate = StreamState.pending;
\t\t\tNewGraph();
\t\t}

\t\tprivate void ResetGraph()
\t\t{
\t\t}

\t\tprivate void NewGraph()
\t\t{
\t\t}

\t\tpublic void Update(GameTime gameTime)
\t\t{
\t\t}

\t\tpublic void SetRate(double rate)
\t\t{
\t\t}

\t\tpublic void PlayFile(string filename, bool fadein)
\t\t{
\t\t}

\t\tpublic void Stop(bool fadeout)
\t\t{
\t\t}
\t}
'''

ILSPY_NESTED_TYPE_EXPECT = {
    'playing,': 'AudioData.StreamState.<declarations>',
    # the fields ILSpy emits AFTER the hoisted nested type belong to the outer type
    'private StreamState state;': 'AudioData.<declarations>',
    'public StreamState State => state;': 'AudioData.State',
    'state = StreamState.pending;': 'AudioData.AudioData',
    'NewGraph();': 'AudioData.AudioData',
}

ILSPY_NESTED_TYPE_ALLOWED = {
    '<none>.<declarations>',    # the trailing line past the class' closing brace, as above
    'AudioData.<declarations>', 'AudioData.StreamState.<declarations>', 'AudioData.State',
    'AudioData.AudioData', 'AudioData.ResetGraph', 'AudioData.NewGraph', 'AudioData.Update',
    'AudioData.SetRate', 'AudioData.PlayFile', 'AudioData.Stop',
}

Fixture = collections.namedtuple('Fixture', 'label source expect allowed')

SELFTEST_FIXTURES = (
    Fixture('declaration shapes', SHAPES_SOURCE, SHAPES_EXPECT, SHAPES_ALLOWED),
    Fixture('verbatim ilspycmd output', ILSPY_NESTED_TYPE_SOURCE, ILSPY_NESTED_TYPE_EXPECT,
            ILSPY_NESTED_TYPE_ALLOWED),
)


def selftest():
    """Attribution IS this tool's contract, so it gets a test that does not need a build.

    Guards the specific ways it has already been broken: a type scope that never closed, so a
    nested type stole its outer type's name for every member below it; a zero-or-more modifier
    prefix that made `else if (...)` parse as a member named `if`; constructors / properties /
    explicit interface implementations matching no `modifiers returnType Name(` pattern at all;
    and three shapes that matched a pattern they had no business matching (a `return X.Y(...)`
    call, a tuple-typed field, a declaration complete on one line).
    """
    failures = []
    checked = 0
    for fx in SELFTEST_FIXTURES:
        lines = fx.source.split('\n')
        where = enclosing(lines)
        for needle, expected in fx.expect.items():
            checked += 1
            hits = [w for l, w in zip(lines, where) if l.strip() == needle]
            if not hits:
                failures.append(f'  [{fx.label}] {needle!r}: never found in the fixture')
            elif hits[0] != expected:
                failures.append(f'  [{fx.label}] {needle!r}: attributed to {hits[0]}, '
                                f'expected {expected}')
        # An EXACT whitelist, not a blacklist of statement keywords. A phantom member is named
        # after whichever identifier the regex latched onto -- `AngleToVector`, `List<` -- which no
        # keyword list can anticipate, and that is precisely how those two went unnoticed. The
        # `missing` half is not symmetry for its own sake either: it fails a fixture that has been
        # edited until it no longer reaches a construct it is supposed to cover.
        seen = set(where)
        for name in sorted(seen - fx.allowed):
            failures.append(f'  [{fx.label}] attributed lines to {name!r}, which is not a member '
                            f'of this fixture')
        for name in sorted(fx.allowed - seen):
            failures.append(f'  [{fx.label}] nothing attributed to {name!r} -- the fixture no '
                            f'longer reaches it')
    if failures:
        print('SELFTEST FAILED:')
        print('\n'.join(failures))
        return EXIT_DIFFERENT
    print(f'SELFTEST PASSED -- {checked} member attributions correct across '
          f'{len(SELFTEST_FIXTURES)} fixtures, no phantom members.')
    return EXIT_IDENTICAL


def main():
    ap = argparse.ArgumentParser(
        description='Diff the decompiled C# of a refactor against its branch point.')
    ap.add_argument('--selftest', action='store_true',
                    help='check member attribution against the fixtures; no build, no git')
    ap.add_argument('--ref', default='HEAD', help='git ref to compare against (default HEAD)')
    ap.add_argument('--full', action='store_true',
                    help='print the entire unified diff, not just the per-method summary')
    ap.add_argument('--context', type=int, default=3, help='diff context lines (default 3)')
    args = ap.parse_args()

    if args.selftest:
        return selftest()

    # Same reason as verify_il_identical --optimize: unoptimized output keeps every dead store.
    ili.BUILD_FLAGS.append('-p:Optimize=true')

    root = ili.repo_root()
    # Same exposure as verify_il_identical: this also builds a FRESH checkout against the
    # working tree, so pinned-EOL drift would show up as a phantom BuildRenderTree diff.
    ili.check_pinned_eol(root)
    print(f'Decompiled-C# diff  (repo {root})', flush=True)
    commit = ili.resolve_ref(root, args.ref)
    label = f'{args.ref} ({commit[:8]})'

    head = ili.run(['git', 'rev-parse', 'HEAD'], cwd=root).stdout.strip()
    dirty = bool(ili.run(['git', 'status', '--porcelain'], cwd=root).stdout.strip())
    if commit == head and not dirty:
        ili.die('nothing to compare: the working tree is clean and --ref resolves to HEAD.\n'
                'Pass the branch point instead, e.g. --ref main.')

    scratch = tempfile.mkdtemp(prefix='rotea_csdiff_')
    ref_tree = os.path.join(scratch, 'src')
    try:
        ili.run(['git', 'worktree', 'add', '--detach', ref_tree, commit], cwd=root)
        try:
            ref_cs = decompile(build_dll(ref_tree, f'reference {label}'),
                               os.path.join(scratch, 'ref'), f'reference {label}')
        finally:
            ili.run(['git', 'worktree', 'remove', '--force', ref_tree], cwd=root, check=False)
            ili.run(['git', 'worktree', 'prune'], cwd=root, check=False)

        # The Clean must be in a finally: decompile() can die (ilspycmd missing is the documented
        # prerequisite, so it is the LIKELY path), and this build emitted no PDB. Leaving that
        # assembly behind hands the dev server a binary with no stack traces -- in the one place
        # WASM errors actually surface -- and MSBuild's incremental check will not recompile it,
        # because only a property changed. Same cleanup verify_il_identical does, same reason.
        try:
            work_dll = build_dll(root, 'working tree')
            work_cs = decompile(work_dll, os.path.join(scratch, 'work'), 'working tree')
        finally:
            ili.msbuild(root, ['-t:Clean'])

        if ref_cs == work_cs:
            print('\nIDENTICAL -- the decompiled C# is unchanged.')
            print('(Use verify_il_identical.py for the stronger byte-identical claim.)')
            return EXIT_IDENTICAL

        ref_where, work_where = enclosing(ref_cs), enclosing(work_cs)
        sm = difflib.SequenceMatcher(None, ref_cs, work_cs, autojunk=False)
        touched, removed, added = {}, 0, 0
        for tag, i1, i2, j1, j2 in sm.get_opcodes():
            if tag == 'equal':
                continue
            removed += i2 - i1
            added += j2 - j1
            for i in range(i1, i2):
                touched.setdefault(ref_where[i], [0, 0])[0] += 1
            for j in range(j1, j2):
                touched.setdefault(work_where[j], [0, 0])[1] += 1

        print(f'\nDIFFERENT -- {removed} line(s) removed, {added} added, '
              f'across {len(touched)} member(s):')
        for name, (minus, plus) in sorted(touched.items()):
            print(f'   {name}:  -{minus} / +{plus}')
        print('\nEvery member listed above should be one you deliberately edited.')
        print('A member you did not touch appearing here is the finding.')

        if args.full:
            print()
            for line in difflib.unified_diff(ref_cs, work_cs, fromfile=f'ref {label}',
                                             tofile='working tree', lineterm='',
                                             n=args.context):
                print(line)
        else:
            print('\nRe-run with --full to read the diff itself.')
        return EXIT_DIFFERENT
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


if __name__ == '__main__':
    sys.exit(main())
