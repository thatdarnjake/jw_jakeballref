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

            // Team name from the name table
            var nameCell = nameRows[i].SelectSingleNode(".//span[contains(@class,'TeamLink')]//a")
                ?? nameRows[i].SelectSingleNode(".//a")
                ?? nameRows[i].SelectSingleNode(".//td");

            var teamName = WebUtility.HtmlDecode(nameCell?.InnerText?.Trim() ?? $"Team {i + 1}");
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
        const string cacheKey = "nba_playoffs";
        if (_cache.TryGetValue(cacheKey, out object? cached) && cached != null)
            return cached;

        var url = "https://www.espn.com/nba/bracket";
        _logger.LogInformation("Fetching playoff bracket: {Url}", url);

        var response = await _http.GetAsync(url);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var matchups = new List<Dictionary<string, string>>();

        // ESPN bracket uses matchup containers
        var series = doc.DocumentNode.SelectNodes("//div[contains(@class,'bracket')]//section|//div[contains(@class,'matchup')]|//li[contains(@class,'matchup')]");

        if (series != null)
        {
            foreach (var s in series)
            {
                var teams = s.SelectNodes(".//span[contains(@class,'team-name')]|.//a[contains(@class,'team')]|.//div[contains(@class,'competitor')]");
                var scores = s.SelectNodes(".//span[contains(@class,'score')]|.//div[contains(@class,'wins')]");

                if (teams != null && teams.Count >= 2)
                {
                    var matchup = new Dictionary<string, string>
                    {
                        ["team1"] = WebUtility.HtmlDecode(teams[0].InnerText.Trim()),
                        ["team2"] = WebUtility.HtmlDecode(teams[1].InnerText.Trim()),
                        ["score1"] = scores?.Count > 0 ? scores[0].InnerText.Trim() : "",
                        ["score2"] = scores?.Count > 1 ? scores[1].InnerText.Trim() : ""
                    };
                    matchups.Add(matchup);
                }
            }
        }

        // If scraping fails or playoffs haven't started
        if (matchups.Count == 0)
        {
            var result = new
            {
                status = "Playoffs have not yet started for the 2025-26 season.",
                champion = (string?)null
            };
            _cache.Set(cacheKey, (object)result, CacheDuration);
            return result;
        }

        _cache.Set(cacheKey, (object)matchups, CacheDuration);
        return matchups;
    }
}
