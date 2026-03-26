using JakeBallReference.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMemoryCache();
builder.Services.AddHttpClient<BballRefScraper>(client =>
{
    client.DefaultRequestHeaders.Add("User-Agent",
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
    client.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
    client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.5");
    client.Timeout = TimeSpan.FromSeconds(15);
});

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

// Search players
app.MapGet("/api/players/search", async (string q, BballRefScraper scraper) =>
{
    if (string.IsNullOrWhiteSpace(q) || q.Length < 2)
        return Results.BadRequest("Query must be at least 2 characters");

    try
    {
        var results = await scraper.SearchPlayersAsync(q);
        return Results.Ok(results);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Search failed: {ex.Message}");
    }
});

// Get full player profile
app.MapGet("/api/players/{playerId}", async (string playerId, BballRefScraper scraper) =>
{
    try
    {
        var profile = await scraper.GetPlayerProfileAsync(playerId);
        if (profile == null)
            return Results.NotFound();
        return Results.Ok(profile);
    }
    catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
    {
        return Results.NotFound();
    }
    catch (Exception ex)
    {
        return Results.Problem($"Failed to load player: {ex.Message}");
    }
});

app.Run();
