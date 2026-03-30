using System.Net;
using HtmlAgilityPack;
using Microsoft.Extensions.Caching.Memory;

namespace JakeBallReference.Services;

public class StandingsScraper
{
    private readonly HttpClient _http;
    private readonly IMemoryCache _cache;
    private readonly ILogger<StandingsScraper> _logger;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(15);

    public StandingsScraper(HttpClient http, IMemoryCache cache, ILogger<StandingsScraper> logger)
    {
        _http = http;
        _cache = cache;
        _logger = logger;
    }

    public async Task<object> GetStandingsAsync()
    {
        const string cacheKey = "nba_standings";
        if (_cache.TryGetValue(cacheKey, out object? cached) && cached != null)
            return cached;

        var url = "https://www.espn.com.au/nba/standings";
        _logger.LogInformation("Fetching standings: {Url}", url);

        var response = await _http.GetAsync(url);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var east = ParseConference(doc, 0);
        var west = ParseConference(doc, 1);

        var result = new { east, west };
        _cache.Set(cacheKey, result, CacheDuration);
        return result;
    }

    private List<Dictionary<string, string>> ParseConference(HtmlDocument doc, int index)
    {
        var teams = new List<Dictionary<string, string>>();

        // ESPN standings tables - look for the standings table sections
        var tables = doc.DocumentNode.SelectNodes("//table[contains(@class,'Table')]");
        if (tables == null || tables.Count < 2) return teams;

        // ESPN has pairs of tables: team names table + stats table per conference
        // Index 0,1 = first conference pair; 2,3 = second conference pair
        var nameTableIdx = index * 2;
        var statTableIdx = index * 2 + 1;

        if (nameTableIdx >= tables.Count || statTableIdx >= tables.Count) return teams;

        var nameRows = tables[nameTableIdx].SelectNodes(".//tbody/tr");
        var statRows = tables[statTableIdx].SelectNodes(".//tbody/tr");
        var statHeaders = tables[statTableIdx].SelectNodes(".//thead/tr[last()]/th");

        if (nameRows == null || statRows == null) return teams;

        var headers = new List<string>();
        if (statHeaders != null)
        {
            foreach (var th in statHeaders)
                headers.Add(WebUtility.HtmlDecode(th.InnerText.Trim()));
        }

        for (int i = 0; i < nameRows.Count && i < statRows.Count; i++)
        {
            var team = new Dictionary<string, string>();

            // Team name - try multiple selectors for robustness
            var teamName = $"Team {i + 1}";

            // Try: all anchor tags in the name row, pick the one with the longest text (full team name)
            var allLinks = nameRows[i].SelectNodes(".//a");
            if (allLinks != null)
            {
                var best = allLinks
                    .Select(a => WebUtility.HtmlDecode(a.InnerText.Trim()))
                    .Where(t => t.Length > 2)
                    .OrderByDescending(t => t.Length)
                    .FirstOrDefault();
                if (!string.IsNullOrEmpty(best))
                    teamName = best;
            }

            // Fallback: just get all text from the row
            if (teamName.StartsWith("Team "))
            {
                var rowText = WebUtility.HtmlDecode(nameRows[i].InnerText.Trim());
                // Clean up - remove rank numbers and extra whitespace
                var cleaned = System.Text.RegularExpressions.Regex.Replace(rowText, @"^\d+", "").Trim();
                if (cleaned.Length > 2)
                    teamName = cleaned.Split('\n')[0].Trim();
            }

            team["rank"] = (i + 1).ToString();
            team["team"] = teamName;

            // Stats from the stat table
            var cells = statRows[i].SelectNodes(".//td");
            if (cells != null)
            {
                for (int j = 0; j < cells.Count && j < headers.Count; j++)
                {
                    team[headers[j].ToUpper()] = WebUtility.HtmlDecode(cells[j].InnerText.Trim());
                }
            }

            teams.Add(team);
        }

        return teams;
    }

    public async Task<object> GetPlayoffBracketAsync()
    {
        const string cacheKey = "nba_playoff_picture";
        if (_cache.TryGetValue(cacheKey, out object? cached) && cached != null)
            return cached;

        // Build projected playoff picture from current standings
        var standings = await GetStandingsAsync();
        var eastProp = standings.GetType().GetProperty("east");
        var westProp = standings.GetType().GetProperty("west");
        var east = eastProp?.GetValue(standings) as List<Dictionary<string, string>> ?? new();
        var west = westProp?.GetValue(standings) as List<Dictionary<string, string>> ?? new();

        object buildConference(List<Dictionary<string, string>> teams)
        {
            var playoff = teams.Take(6).Select((t, i) => new { seed = i + 1, team = t.GetValueOrDefault("team", "TBD"), record = t.GetValueOrDefault("W", "0") + "-" + t.GetValueOrDefault("L", "0") }).ToList();
            var playIn = teams.Skip(6).Take(4).Select((t, i) => new { seed = i + 7, team = t.GetValueOrDefault("team", "TBD"), record = t.GetValueOrDefault("W", "0") + "-" + t.GetValueOrDefault("L", "0") }).ToList();

            // Projected first round matchups: 1v8, 2v7, 3v6, 4v5
            var firstRound = new List<object>();
            if (teams.Count >= 8)
            {
                firstRound.Add(new { higher = $"(1) {teams[0].GetValueOrDefault("team", "TBD")}", lower = $"(8) {teams[7].GetValueOrDefault("team", "TBD")}" });
                firstRound.Add(new { higher = $"(2) {teams[1].GetValueOrDefault("team", "TBD")}", lower = $"(7) {teams[6].GetValueOrDefault("team", "TBD")}" });
                firstRound.Add(new { higher = $"(3) {teams[2].GetValueOrDefault("team", "TBD")}", lower = $"(6) {teams[5].GetValueOrDefault("team", "TBD")}" });
                firstRound.Add(new { higher = $"(4) {teams[3].GetValueOrDefault("team", "TBD")}", lower = $"(5) {teams[4].GetValueOrDefault("team", "TBD")}" });
            }

            return new { playoff, playIn, firstRound };
        }

        var result = new
        {
            type = "projected",
            east = buildConference(east),
            west = buildConference(west)
        };

        _cache.Set(cacheKey, (object)result, CacheDuration);
        return result;
    }
}
