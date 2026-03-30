using System.Net;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Microsoft.Extensions.Caching.Memory;

namespace JakeBallReference.Services;

public class TeamScraper
{
    private readonly HttpClient _http;
    private readonly IMemoryCache _cache;
    private readonly ILogger<TeamScraper> _logger;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);
    private static readonly SemaphoreSlim _throttle = new(1, 1);
    private static DateTime _lastRequest = DateTime.MinValue;
    private static readonly TimeSpan _minDelay = TimeSpan.FromSeconds(3);

    public TeamScraper(HttpClient http, IMemoryCache cache, ILogger<TeamScraper> logger)
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

    public static readonly Dictionary<string, string> NbaTeams = new()
    {
        ["ATL"] = "Atlanta Hawks",
        ["BOS"] = "Boston Celtics",
        ["BRK"] = "Brooklyn Nets",
        ["CHO"] = "Charlotte Hornets",
        ["CHI"] = "Chicago Bulls",
        ["CLE"] = "Cleveland Cavaliers",
        ["DAL"] = "Dallas Mavericks",
        ["DEN"] = "Denver Nuggets",
        ["DET"] = "Detroit Pistons",
        ["GSW"] = "Golden State Warriors",
        ["HOU"] = "Houston Rockets",
        ["IND"] = "Indiana Pacers",
        ["LAC"] = "Los Angeles Clippers",
        ["LAL"] = "Los Angeles Lakers",
        ["MEM"] = "Memphis Grizzlies",
        ["MIA"] = "Miami Heat",
        ["MIL"] = "Milwaukee Bucks",
        ["MIN"] = "Minnesota Timberwolves",
        ["NOP"] = "New Orleans Pelicans",
        ["NYK"] = "New York Knicks",
        ["OKC"] = "Oklahoma City Thunder",
        ["ORL"] = "Orlando Magic",
        ["PHI"] = "Philadelphia 76ers",
        ["PHO"] = "Phoenix Suns",
        ["POR"] = "Portland Trail Blazers",
        ["SAC"] = "Sacramento Kings",
        ["SAS"] = "San Antonio Spurs",
        ["TOR"] = "Toronto Raptors",
        ["UTA"] = "Utah Jazz",
        ["WAS"] = "Washington Wizards"
    };

    public List<object> GetTeamList()
    {
        return NbaTeams.Select(kv => (object)new { code = kv.Key, name = kv.Value }).ToList();
    }

    public async Task<object?> GetTeamProfileAsync(string teamCode, int? season = null)
    {
        var year = season ?? 2026;
        var cacheKey = $"team:{teamCode}:{year}";
        if (_cache.TryGetValue(cacheKey, out object? cached) && cached != null)
            return cached;

        var url = $"https://www.basketball-reference.com/teams/{teamCode.ToUpper()}/{year}.html";
        var doc = await FetchPageAsync(url);

        // Team name and record from page title / meta
        var teamName = NbaTeams.GetValueOrDefault(teamCode.ToUpper(), teamCode);
        var record = "";
        var metaP = doc.DocumentNode.SelectNodes("//div[@id='meta']//p");
        if (metaP != null)
        {
            foreach (var p in metaP)
            {
                var text = p.InnerText.Trim();
                var recMatch = Regex.Match(text, @"Record:\s*(\d+-\d+)");
                if (recMatch.Success)
                    record = recMatch.Groups[1].Value;
            }
        }

        // Roster table
        var roster = ExtractTable(doc, "roster");

        // Per game stats
        var perGame = ExtractTable(doc, "per_game");

        // Totals
        var totals = ExtractTable(doc, "totals");

        var result = new
        {
            teamCode = teamCode.ToUpper(),
            teamName,
            season = $"{year - 1}-{year.ToString()[2..]}",
            record,
            roster,
            perGame,
            totals
        };

        _cache.Set(cacheKey, (object)result, CacheDuration);
        return result;
    }

    private object? ExtractTable(HtmlDocument doc, string tableId)
    {
        var table = doc.DocumentNode.SelectSingleNode($"//table[@id='{tableId}']");

        // Check inside HTML comments (bball-ref defers some tables)
        if (table == null)
        {
            var comments = doc.DocumentNode.SelectNodes("//comment()");
            if (comments != null)
            {
                foreach (var comment in comments)
                {
                    if (comment.InnerHtml.Contains($"id=\"{tableId}\""))
                    {
                        var commentDoc = new HtmlDocument();
                        commentDoc.LoadHtml(comment.InnerHtml);
                        table = commentDoc.DocumentNode.SelectSingleNode($"//table[@id='{tableId}']");
                        if (table != null) break;
                    }
                }
            }
        }

        if (table == null) return null;

        var headers = new List<string>();
        var headerRow = table.SelectSingleNode(".//thead/tr[last()]");
        if (headerRow != null)
        {
            foreach (var th in headerRow.SelectNodes(".//th") ?? Enumerable.Empty<HtmlNode>())
                headers.Add(WebUtility.HtmlDecode(th.InnerText.Trim()));
        }

        var rows = new List<Dictionary<string, string>>();
        var dataRows = table.SelectNodes(".//tbody/tr[not(contains(@class,'thead'))]");
        if (dataRows != null)
        {
            foreach (var row in dataRows)
            {
                var rowData = new Dictionary<string, string>();
                var th = row.SelectSingleNode(".//th");
                if (th != null && headers.Count > 0)
                    rowData[headers[0]] = WebUtility.HtmlDecode(th.InnerText.Trim());

                var cells = row.SelectNodes(".//td");
                if (cells != null)
                {
                    for (int i = 0; i < cells.Count && i + 1 < headers.Count; i++)
                        rowData[headers[i + 1]] = WebUtility.HtmlDecode(cells[i].InnerText.Trim());
                }

                if (rowData.Count > 0)
                    rows.Add(rowData);
            }
        }

        return new { headers, rows };
    }
}
