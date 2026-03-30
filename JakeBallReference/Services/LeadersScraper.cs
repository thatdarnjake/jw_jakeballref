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

    // The per-game leader categories we want to show
    private static readonly string[] PerGameCategories = new[]
    {
        "leaders_pts_per_g", "leaders_trb_per_g", "leaders_ast_per_g",
        "leaders_stl_per_g", "leaders_blk_per_g",
        "leaders_fg_pct", "leaders_ft_pct", "leaders_fg3_pct", "leaders_efg_pct"
    };

    // Total categories
    private static readonly string[] TotalCategories = new[]
    {
        "leaders_pts", "leaders_trb", "leaders_ast",
        "leaders_stl", "leaders_blk", "leaders_orb", "leaders_drb"
    };

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

        var perGame = new List<object>();
        foreach (var id in PerGameCategories)
        {
            var cat = ParseLeaderDiv(doc, id);
            if (cat != null) perGame.Add(cat);
        }

        var totals = new List<object>();
        foreach (var id in TotalCategories)
        {
            var cat = ParseLeaderDiv(doc, id);
            if (cat != null) totals.Add(cat);
        }

        var result = new
        {
            season = $"{year - 1}-{year.ToString()[2..]}",
            perGame,
            totals
        };

        _cache.Set(cacheKey, (object)result, CacheDuration);
        return result;
    }

    // ---- All-Time Leaders (single query) ----

    private static readonly Dictionary<string, string> StatNames = new()
    {
        ["pts"] = "Points", ["trb"] = "Rebounds", ["ast"] = "Assists",
        ["stl"] = "Steals", ["blk"] = "Blocks", ["fg"] = "Field Goals",
        ["fg3"] = "3-Pointers", ["ft"] = "Free Throws",
        ["g"] = "Games Played", ["mp"] = "Minutes Played"
    };

    public async Task<object> GetSingleAllTimeLeaderAsync(string stat, string type)
    {
        var cacheKey = $"alltime:{stat}_{type}";
        if (_cache.TryGetValue(cacheKey, out object? cached) && cached != null)
            return cached;

        // type maps to URL suffix: career, season, career_p, active
        var url = $"https://www.basketball-reference.com/leaders/{stat}_{type}.html";
        var doc = await FetchPageAsync(url);
        var entries = ParseAllTimeTable(doc);

        var title = StatNames.GetValueOrDefault(stat, stat);
        var typeLabel = type switch
        {
            "career" => "Career (Reg Season)",
            "season" => "Single Season",
            "career_p" => "Career (Playoffs)",
            "active" => "Active Players",
            _ => type
        };

        var result = new { title, typeLabel, stat, type, entries };
        _cache.Set(cacheKey, (object)result, TimeSpan.FromHours(6));
        return result;
    }

    private List<object> ParseAllTimeTable(HtmlDocument doc)
    {
        var entries = new List<object>();

        // Rows are inside div#div_nba as tr elements with player links
        var rows = doc.DocumentNode.SelectNodes("//div[@id='div_nba']//tr[.//a[contains(@href,'/players/')]]");
        if (rows == null) return entries;

        foreach (var row in rows.Take(25))
        {
            var cells = row.SelectNodes(".//td|.//th");
            if (cells == null || cells.Count < 2) continue;

            // First cell: rank (may include ".")
            var rankText = cells[0].InnerText.Trim().TrimEnd('.');
            int.TryParse(rankText, out var rank);

            // Player link
            var link = row.SelectSingleNode(".//a[contains(@href,'/players/')]");
            var playerName = WebUtility.HtmlDecode(link?.InnerText?.Trim() ?? "");
            var href = link?.GetAttributeValue("href", "") ?? "";
            var idMatch = Regex.Match(href, @"/players/\w/(\w+)\.html");

            // Stat value: last cell
            var value = WebUtility.HtmlDecode(cells[cells.Count - 1].InnerText.Trim());

            if (!string.IsNullOrEmpty(playerName))
            {
                entries.Add(new
                {
                    rank = rank > 0 ? rank : entries.Count + 1,
                    player = playerName.TrimEnd('*'), // remove HOF marker
                    hof = playerName.EndsWith("*"),
                    playerId = idMatch.Success ? idMatch.Groups[1].Value : "",
                    value
                });
            }
        }

        return entries;
    }

    private object? ParseLeaderDiv(HtmlDocument doc, string divId)
    {
        var div = doc.DocumentNode.SelectSingleNode($"//div[@id='{divId}']");
        if (div == null) return null;

        // Title from h4
        var titleNode = div.SelectSingleNode(".//h4");
        var title = WebUtility.HtmlDecode(titleNode?.InnerText?.Trim() ?? divId);

        var entries = new List<object>();

        // Each entry is a div with span.rank, span.who (contains a link + team), span.value
        var entryDivs = div.SelectNodes(".//div[span[@class='rank']]");
        if (entryDivs != null)
        {
            foreach (var entry in entryDivs.Take(10))
            {
                var rankText = entry.SelectSingleNode(".//span[@class='rank']")?.InnerText?.Trim().TrimEnd('.');
                var playerLink = entry.SelectSingleNode(".//span[@class='who']//a");
                var playerName = WebUtility.HtmlDecode(playerLink?.InnerText?.Trim() ?? "");
                var teamSpan = entry.SelectSingleNode(".//span[@class='who']//span[@class='desc']");
                var team = WebUtility.HtmlDecode(teamSpan?.InnerText?.Trim() ?? "");
                var value = WebUtility.HtmlDecode(entry.SelectSingleNode(".//span[@class='value']")?.InnerText?.Trim() ?? "");

                var href = playerLink?.GetAttributeValue("href", "") ?? "";
                var idMatch = Regex.Match(href, @"/players/\w/(\w+)\.html");

                if (!string.IsNullOrEmpty(playerName))
                {
                    entries.Add(new
                    {
                        rank = int.TryParse(rankText, out var r) ? r : entries.Count + 1,
                        player = playerName,
                        team,
                        playerId = idMatch.Success ? idMatch.Groups[1].Value : "",
                        value
                    });
                }
            }
        }

        return entries.Count > 0 ? new { title, entries } : null;
    }
}
