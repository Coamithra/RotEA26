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
// Dev-only write endpoint for the ?texviewer texture-format viewer (Compat/TexViewerScene.cs).
// The in-browser viewer can't touch disk, so its "Save" button POSTs the per-sprite decision here
// and this upserts the line in tools/textures/textures.config. DevServer is never shipped to Pages
// (CI publishes web/EvilAliensWeb directly), so this write path is local-dev only. Body JSON:
//   { "asset": "gfx/sprites/x", "format": "dxt|raw|png", "cols": 1, "rows": 1 }
// format "png" removes the asset's line (PNG is the default = no precompiled sibling).
app.MapPost("/api/texdecide", async (HttpContext ctx) =>
{
    TexDecision req;
    try
    {
        req = await System.Text.Json.JsonSerializer.DeserializeAsync<TexDecision>(
            ctx.Request.Body,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { ok = false, error = "bad JSON: " + ex.Message });
    }
    if (req == null || string.IsNullOrWhiteSpace(req.Asset) || string.IsNullOrWhiteSpace(req.Format))
        return Results.BadRequest(new { ok = false, error = "asset + format required" });

    string asset = req.Asset.Trim().ToLowerInvariant();
    string fmt = req.Format.Trim().ToLowerInvariant();
    if (fmt != "dxt" && fmt != "raw" && fmt != "png")
        return Results.BadRequest(new { ok = false, error = "format must be dxt|raw|png" });

    string cfg = FindTexturesConfig(app.Environment.ContentRootPath);
    if (cfg == null)
        return Results.Json(new { ok = false, error = "textures.config not found (walked up from " + app.Environment.ContentRootPath + ")" }, statusCode: 500);

    try
    {
        string line = UpsertConfig(cfg, asset, fmt, Math.Max(1, req.Cols), Math.Max(1, req.Rows));
        return Results.Json(new { ok = true, asset, format = fmt, line });
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = ex.Message }, statusCode: 500);
    }
});

app.MapFallbackToFile("index.html");

app.Run();

// Walk up from the DevServer's content root to the repo root (the dir holding
// tools/textures/textures.config). Works from the root checkout AND any worktree.
static string FindTexturesConfig(string startDir)
{
    var dir = new DirectoryInfo(startDir);
    for (int i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
    {
        string candidate = Path.Combine(dir.FullName, "tools", "textures", "textures.config");
        if (File.Exists(candidate))
            return candidate;
    }
    return null;
}

// Upsert one asset's decision in textures.config, preserving comments, blank lines and order.
// dxt -> "<asset>  dxt  <cols> <rows>"; raw -> "<asset>  raw"; png -> remove the line entirely
// (PNG = the default, no precompiled sibling). Returns the written line ("" when removed).
static string UpsertConfig(string path, string asset, string fmt, int cols, int rows)
{
    string[] lines = File.ReadAllLines(path);
    string newLine = fmt == "dxt" ? $"{asset}  dxt  {cols} {rows}" : (fmt == "raw" ? $"{asset}  raw" : "");
    var outLines = new List<string>(lines.Length + 1);
    bool replaced = false;
    foreach (string raw in lines)
    {
        string code = raw.Split('#', 2)[0].Trim();
        string firstToken = code.Length == 0 ? "" : code.Split((char[])null, StringSplitOptions.RemoveEmptyEntries)[0].ToLowerInvariant();
        if (firstToken == asset)
        {
            replaced = true;
            if (newLine.Length > 0)
                outLines.Add(newLine);   // replace with the new decision
            // else: drop the line (png)
            continue;
        }
        outLines.Add(raw);
    }
    if (!replaced && newLine.Length > 0)
    {
        if (outLines.Count > 0 && outLines[outLines.Count - 1].Trim().Length > 0)
            outLines.Add("");
        outLines.Add(newLine);
    }
    // Drop any trailing blank lines so repeated add/remove can't accumulate them, then end
    // with exactly one newline.
    while (outLines.Count > 0 && outLines[outLines.Count - 1].Trim().Length == 0)
        outLines.RemoveAt(outLines.Count - 1);
    File.WriteAllText(path, string.Join("\n", outLines) + "\n");
    return newLine;
}

// POST body for /api/texdecide.
record TexDecision(string Asset, string Format, int Cols, int Rows);
