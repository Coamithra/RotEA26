// Local-dev static host for the Blazor WASM game — see DevServer.csproj for why this exists.
// Forces `no-store` on every response so index.html / wwwroot JS / Content assets are always
// refetched during iteration (never the browser-cached stale copy). Dev-only; CI ignores it.

var builder = WebApplication.CreateBuilder(args);
// The referenced WASM client's wwwroot + _framework are exposed via the static-web-assets
// manifest, which WebApplication only auto-loads in the Development environment. `dotnet run`
// defaults to Production, so force it on regardless of environment (else every file 404s).
builder.WebHost.UseStaticWebAssets();
var app = builder.Build();

// Register a header-stamp BEFORE the file middlewares run. OnStarting fires just before the
// response flushes, so it overrides whatever Cache-Control the static-file middleware set —
// covering index.html, _framework/*, Content/*, and every wwwroot JS in one place.
app.Use(async (ctx, next) =>
{
    ctx.Response.OnStarting(() =>
    {
        var h = ctx.Response.Headers;
        h.CacheControl = "no-store, no-cache, must-revalidate";
        h.Pragma = "no-cache";
        h.Expires = "0";
        return Task.CompletedTask;
    });
    await next();
});

app.UseBlazorFrameworkFiles();   // serves _framework/* from the referenced WASM client
// ServeUnknownFileTypes: the stock blazor-devserver serves any extension, but plain
// UseStaticFiles 404s files whose extension isn't in its MIME map — which is all of the
// game's custom asset types (.mgfxo shaders, .dds/.rtex textures, .dat/.blat, ...). Without
// this the bloom shaders 404 and the game crashes at boot. Serve everything as bytes.
app.UseStaticFiles(new StaticFileOptions
{
    ServeUnknownFileTypes = true,
    DefaultContentType = "application/octet-stream",
});
app.MapFallbackToFile("index.html");

app.Run();
