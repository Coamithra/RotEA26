"""Final ctor fixes in ScreenshotSaver.cs (XNA 3.x -> 4.0)."""
f = r'C:\Programming\RotEA26\web\EvilAliensWeb\Game\EvilAliens\ScreenshotSaver.cs'
src = open(f, encoding='utf-8').read()
src = src.replace(
    'new RenderTarget2D(graphicsDevice, (int)SIZE.X, (int)SIZE.Y, 0, graphicsDevice.PresentationParameters.BackBufferFormat)',
    'new RenderTarget2D(graphicsDevice, (int)SIZE.X, (int)SIZE.Y, false, graphicsDevice.PresentationParameters.BackBufferFormat, DepthFormat.None)')
src = src.replace(
    'new Texture2D(graphicsDevice, (int)SIZE.X, (int)SIZE.Y, 1, (TextureUsage)0, graphicsDevice.PresentationParameters.BackBufferFormat)',
    'new Texture2D(graphicsDevice, (int)SIZE.X, (int)SIZE.Y, false, graphicsDevice.PresentationParameters.BackBufferFormat)')
open(f, 'w', encoding='utf-8').write(src)
print("ScreenshotSaver ctors fixed")
