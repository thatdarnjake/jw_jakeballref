using System.Net;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Microsoft.Extensions.Caching.Memory;

namespace JakeBallReference.Services;

public class LeadersScraper
{
    private readonly HttpClient _http;
    private readonly IMemoryCache _cache;
    private readonly ILogger<LeadersScraper> _logger;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);
    private static readonly SemaphoreSlim _throttle = new(1, 1);
    private static DateTime _lastRequest = DateTime.MinValue;
    private static readonly TimeSpan _minDelay = TimeSpan.FromSeconds(3);

    public LeadersScraper(HttpClient http, IMemoryCache cache, ILogger<LeadersScraper> logger)
    {
        _http = http;
        _cache = cache;
        _logger = logger;
    }

    private async Task<HtmlDocument> FetchPageAsync(string url)
    {
        await _throttle.WaitAsync();
        try
        {
            var elapsed = DateTime.UtcNow - _lastRequest;
            if (elapsed < _minDelay)
                await Task.Delay(_minDelay - elapsed);

            _logger.LogInformation("Fetching: {Url}", url);
            var response = await _http.GetAsync(url);
            response.EnsureSuccessStatusCode();
            var html = await response.Content.ReadAsStringAsync();
            _lastRequest = DateTime.UtcNow;

            var doc = new HtmlDocument();
            doc.LoadHtml(html);
            return doc;
        }
        finally
        {
            _throttle.Release();
        }
    }

    public async Task<object> GetLeadersAsync(int? season = null)
    {
        var year = season ?? 2026;
        var cacheKey = $"leaders:{year}";
        if (_cache.TryGetValue(cacheKey, out object? cached) && cached != null)
            return cached;

        var url = $"https://www.basketball-reference.com/leagues/NBA_{year}_leaders.html";
        var doc = await FetchPageAsync(url);

        var categories = new List<object>();

        // bball-ref leaders page has div.data_grid_box containers
        // Each contains an h4 title and a table or ordered list of leaders
        var boxes = doc.DocumentNode.SelectNodes("//div[contains(@class,'data_grid_box')]");

        if (boxes != null)
        {
            foreach (var box in boxes)
            {
                var titleNode = box.SelectSingleNode(".//caption|.//h4");
                var title = WebUtility.HtmlDecode(titleNode?.InnerText?.Trim() ?? "");
                if (string.IsNullOrEmpty(title)) continue;

                var entries = new List<object>();

                // Try table rows first (bball-ref uses tables with tbody/tr)
                var rows = box.SelectNodes(".//table//tbody//tr|.//table//tr[td]");
                if (rows != null)
                {
                    foreach (var row in rows.Take(10))
                    {
                        var cells = row.SelectNodes(".//td");
                        if (cells == null || cells.Count < 2) continue;

                        // First cell usually has rank + player name link, last cell has stat value
                        var playerLink = row.SelectSingleNode(".//a");
                        var playerName = WebUtility.HtmlDecode(playerLink?.InnerText?.Trim() ?? "");
                        if (string.IsNullOrEmpty(playerName))
                        {
                            playerName = WebUtility.HtmlDecode(cells[0].InnerText.Trim());
                        }

                        // Get player ID from href
                        var href = playerLink?.GetAttributeValue("href", "") ?? "";
                        var idMatch = Regex.Match(href, @"/players/\w/(\w+)\.html");
                        var playerId = idMatch.Success ? idMatch.Groups[1].Value : "";

                        // Stat value is typically the last cell
                        var statValue = WebUtility.HtmlDecode(cells[cells.Count - 1].InnerText.Trim());

                        // Clean player name - remove rank numbers
                        playerName = Regex.Replace(playerName, @"^\d+\.\s*", "").Trim();

                        if (!string.IsNullOrEmpty(playerName))
                        {
                            entries.Add(new
                            {
                                rank = entries.Count + 1,
                                player = playerName,
                                playerId,
                                value = statValue
                            });
                        }
                    }
                }

                // Fallback: try ordered list items
                if (entries.Count == 0)
                {
                    var listItems = box.SelectNodes(".//ol/li|.//p");
                    if (listItems != null)
                    {
                        foreach (var li in listItems.Take(10))
                        {
                            var link = li.SelectSingleNode(".//a");
                            var name = WebUtility.HtmlDecode(link?.InnerText?.Trim() ?? "");
                            var href = link?.GetAttributeValue("href", "") ?? "";
                            var idMatch = Regex.Match(href, @"/players/\w/(\w+)\.html");
                            var text = WebUtility.HtmlDecode(li.InnerText.Trim());

                            // Extract stat value (usually a number at the end)
                            var valMatch = Regex.Match(text, @"([\d.]+)\s*$");
                            var value = valMatch.Success ? valMatch.Groups[1].Value : "";

                            if (!string.IsNullOrEmpty(name))
                            {
                                entries.Add(new
                                {
                                    rank = entries.Count + 1,
                                    player = name,
                                    playerId = idMatch.Success ? idMatch.Groups[1].Value : "",
                                    value
                                });
                            }
                        }
                    }
                }

                if (entries.Count > 0)
                {
                    categories.Add(new { title, entries });
                }
            }
        }

        var result = new { season = $"{year - 1}-{year.ToString()[2..]}", categories };
        _cache.Set(cacheKey, (object)result, CacheDuration);
        return result;
    }
}
