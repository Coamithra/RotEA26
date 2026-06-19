"""Constructor-signature fixes (XNA 3.x -> 4.0), all unique single-line literals."""
import glob

DST = r'C:\Programming\RotEA26\web\EvilAliensWeb\Game'

REPLACEMENTS = [
    # GameTime 4-arg (real+game) -> 4.0 2-arg (game only), keeping the scaled values
    ('new GameTime(gameTime.TotalGameTime, gameTime.ElapsedGameTime, new TimeSpan((long)((float)gameTime.TotalGameTime.Ticks * num2)), new TimeSpan((long)((float)gameTime.ElapsedGameTime.Ticks * num2)))',
     'new GameTime(new TimeSpan((long)((float)gameTime.TotalGameTime.Ticks * num2)), new TimeSpan((long)((float)gameTime.ElapsedGameTime.Ticks * num2)))'),
    # RenderTarget2D 3.x (numberLevels:int [, usage]) -> 4.0 (mipMap:bool, fmt, DepthFormat[, msaa, usage])
    ('new RenderTarget2D(base.GraphicsDevice, backBufferWidth, backBufferHeight, 1, backBufferFormat)',
     'new RenderTarget2D(base.GraphicsDevice, backBufferWidth, backBufferHeight, false, backBufferFormat, DepthFormat.None)'),
    ('new RenderTarget2D(base.GraphicsDevice, 800, 600, 1, (SurfaceFormat)2, (RenderTargetUsage)1)',
     'new RenderTarget2D(base.GraphicsDevice, 800, 600, false, (SurfaceFormat)2, DepthFormat.None, 0, (RenderTargetUsage)1)'),
    ('new RenderTarget2D(base.GraphicsDevice, presentationParameters.BackBufferWidth, presentationParameters.BackBufferHeight, 1, (SurfaceFormat)1, (RenderTargetUsage)1)',
     'new RenderTarget2D(base.GraphicsDevice, presentationParameters.BackBufferWidth, presentationParameters.BackBufferHeight, false, (SurfaceFormat)1, DepthFormat.None, 0, (RenderTargetUsage)1)'),
    # Quad: 4.0 vertex declaration comes from the vertex type
    ('vertexdecl = new VertexDeclaration(graphicsDevice, VertexPositionNormalTexture.VertexElements);',
     'vertexdecl = VertexPositionNormalTexture.VertexDeclaration;'),
]

counts = {old: 0 for old, _ in REPLACEMENTS}
for f in glob.glob(DST + r'\**\*.cs', recursive=True):
    src = open(f, encoding='utf-8').read()
    orig = src
    for old, new in REPLACEMENTS:
        c = src.count(old)
        if c:
            counts[old] += c
            src = src.replace(old, new)
    if src != orig:
        open(f, 'w', encoding='utf-8').write(src)

for old, n in counts.items():
    print(f"{n:3d}  {old[:60]!r}")
