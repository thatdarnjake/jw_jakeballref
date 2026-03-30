using System.Net;
using System.Text.RegularExpressions;
using System.Web;
using HtmlAgilityPack;
using JakeBallReference.Models;
using Microsoft.Extensions.Caching.Memory;

namespace JakeBallReference.Services;

public class BballRefScraper
{
    private readonly HttpClient _http;
    private readonly IMemoryCache _cache;
    private readonly ILogger<BballRefScraper> _logger;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);

    // Basketball-reference uses rate limiting; be respectful
    private static readonly SemaphoreSlim _throttle = new(1, 1);
    private static DateTime _lastRequest = DateTime.MinValue;
    private static readonly TimeSpan _minDelay = TimeSpan.FromSeconds(3);

    public BballRefScraper(HttpClient http, IMemoryCache cache, ILogger<BballRefScraper> logger)
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

    public async Task<List<PlayerSearchResult>> SearchPlayersAsync(string query)
    {
        var cacheKey = $"search:{query.ToLower()}";
        if (_cache.TryGetValue(cacheKey, out List<PlayerSearchResult>? cached) && cached != null)
            return cached;

        var encoded = HttpUtility.UrlEncode(query);
        var url = $"https://www.basketball-reference.com/search/search.fcgi?search={encoded}";
        var doc = await FetchPageAsync(url);

        var results = new List<PlayerSearchResult>();

        // Check if we got redirected directly to a player page
        var playerInfo = doc.DocumentNode.SelectSingleNode("//div[@id='info']");
        if (playerInfo != null)
        {
            var name = doc.DocumentNode.SelectSingleNode("//h1/span")?.InnerText?.Trim() ?? query;
            // Extract player ID from the page content
            var metaLink = doc.DocumentNode.SelectSingleNode("//link[@rel='canonical']");
            var canonical = metaLink?.GetAttributeValue("href", "") ?? "";
            var idMatch = Regex.Match(canonical, @"/players/\w/(\w+)\.html");
            if (idMatch.Success)
            {
                results.Add(new PlayerSearchResult
                {
                    Name = WebUtility.HtmlDecode(name),
                    PlayerId = idMatch.Groups[1].Value,
                    Url = canonical,
                    YearsActive = ExtractYearsActive(doc)
                });
            }
        }
        else
        {
            // Parse search results page
            var searchItems = doc.DocumentNode.SelectNodes("//div[@id='players']//div[@class='search-item']");
            if (searchItems != null)
            {
                foreach (var item in searchItems.Take(10))
                {
                    var link = item.SelectSingleNode(".//div[@class='search-item-name']//a");
                    if (link == null) continue;

                    var href = link.GetAttributeValue("href", "");
                    var idMatch = Regex.Match(href, @"/players/\w/(\w+)\.html");
                    var nameText = WebUtility.HtmlDecode(link.InnerText.Trim());
                    var urlText = item.SelectSingleNode(".//div[@class='search-item-url']")?.InnerText?.Trim() ?? "";

                    if (idMatch.Success)
                    {
                        results.Add(new PlayerSearchResult
                        {
                            Name = nameText,
                            PlayerId = idMatch.Groups[1].Value,
                            Url = $"https://www.basketball-reference.com{href}",
                            YearsActive = urlText
                        });
                    }
                }
            }
        }

        _cache.Set(cacheKey, results, CacheDuration);
        return results;
    }

    public async Task<PlayerProfile?> GetPlayerProfileAsync(string playerId)
    {
        var cacheKey = $"profile:{playerId}";
        if (_cache.TryGetValue(cacheKey, out PlayerProfile? cached) && cached != null)
            return cached;

        var letter = playerId[0];
        var url = $"https://www.basketball-reference.com/players/{letter}/{playerId}.html";
        var doc = await FetchPageAsync(url);

        var profile = new PlayerProfile { PlayerId = playerId };

        // Name
        profile.Name = WebUtility.HtmlDecode(
            doc.DocumentNode.SelectSingleNode("//h1/span")?.InnerText?.Trim() ?? playerId);

        // Image
        var img = doc.DocumentNode.SelectSingleNode("//div[@id='meta']//img[@class='no_highlight']");
        if (img == null)
            img = doc.DocumentNode.SelectSingleNode("//div[@id='meta']//img");
        profile.ImageUrl = img?.GetAttributeValue("src", "") ?? "";

        // Bio info from meta section
        var metaDiv = doc.DocumentNode.SelectSingleNode("//div[@id='meta']");
        if (metaDiv != null)
        {
            var metaText = metaDiv.InnerText;
            var posMatch = Regex.Match(metaText, @"Position:\s*([^\n]+)");
            if (posMatch.Success)
                profile.Position = posMatch.Groups[1].Value.Trim().Split("▪")[0].Trim();

            // Height/Weight from the meta paragraphs
            var paragraphs = metaDiv.SelectNodes(".//p");
            if (paragraphs != null)
            {
                foreach (var p in paragraphs)
                {
                    var text = p.InnerText.Trim();
                    var hwMatch = Regex.Match(text, @"(\d+-\d+)\s*,\s*(\d+lb)");
                    if (hwMatch.Success)
                    {
                        profile.Height = hwMatch.Groups[1].Value;
                        profile.Weight = hwMatch.Groups[2].Value;
                    }

                    if (text.Contains("Born:"))
                    {
                        var bornMatch = Regex.Match(text, @"Born:\s*(.+?)(?:\s*in\s|$)");
                        if (bornMatch.Success)
                            profile.BirthDate = bornMatch.Groups[1].Value.Trim();
                    }

                    if (text.Contains("College:"))
                    {
                        var collegeLink = p.SelectSingleNode(".//a[contains(@href,'college')]");
                        profile.College = collegeLink?.InnerText?.Trim() ?? "";
                    }

                    if (text.Contains("Draft:"))
                    {
                        var draftText = Regex.Match(text, @"Draft:(.+?)(?:\n|$)");
                        if (draftText.Success)
                            profile.Draft = draftText.Groups[1].Value.Trim();
                    }
                }
            }
        }

        // Accolades from the leaderboard/bling section
        profile.Accolades = ExtractAccolades(doc);

        // Seasons list from per_game_stats table
        profile.Seasons = ExtractSeasons(doc);

        // Stat tables - basketball-reference uses per_game_stats/totals_stats (not per_game/totals)
        // bball-ref table IDs: per_game, totals, advanced
        // (some older pages used per_game_stats / totals_stats, so try both)
        profile.PerGameStats = ExtractStatTable(doc, "per_game") ?? ExtractStatTable(doc, "per_game_stats");
        profile.TotalStats = ExtractStatTable(doc, "totals") ?? ExtractStatTable(doc, "totals_stats");
        profile.AdvancedStats = ExtractStatTable(doc, "advanced");

        _cache.Set(cacheKey, profile, CacheDuration);
        return profile;
    }

    private string ExtractYearsActive(HtmlDocument doc)
    {
        var metaText = doc.DocumentNode.SelectSingleNode("//div[@id='meta']")?.InnerText ?? "";
        var match = Regex.Match(metaText, @"(\d{4}-\d{2,4}\s+to\s+\d{4}-\d{2,4})");
        return match.Success ? match.Value : "";
    }

    private List<Accolade> ExtractAccolades(HtmlDocument doc)
    {
        var accolades = new List<Accolade>();

        // Basketball-reference lists accolades in the bling section in the info div
        var blingList = doc.DocumentNode.SelectNodes("//ul[@id='bling']/li");
        if (blingList != null)
        {
            foreach (var li in blingList)
            {
                var text = WebUtility.HtmlDecode(li.InnerText.Trim());
                if (!string.IsNullOrWhiteSpace(text))
                {
                    // Parse things like "6x All-Star" or "2x NBA Champ" or "MVP"
                    var countMatch = Regex.Match(text, @"^(\d+)x\s+(.+)$");
                    if (countMatch.Success)
                    {
                        accolades.Add(new Accolade
                        {
                            Name = countMatch.Groups[2].Value.Trim(),
                            Count = int.Parse(countMatch.Groups[1].Value)
                        });
                    }
                    else
                    {
                        accolades.Add(new Accolade { Name = text, Count = 1 });
                    }
                }
            }
        }

        return accolades;
    }

    private List<string> ExtractSeasons(HtmlDocument doc)
    {
        var seasons = new List<string>();
        var rows = doc.DocumentNode.SelectNodes("//table[@id='per_game_stats']/tbody/tr[not(contains(@class,'thead'))]");
        if (rows != null)
        {
            foreach (var row in rows)
            {
                var seasonCell = row.SelectSingleNode(".//th[@data-stat='season']");
                var season = seasonCell?.InnerText?.Trim();
                if (!string.IsNullOrWhiteSpace(season) && !seasons.Contains(season))
                    seasons.Add(season);
            }
        }
        return seasons;
    }

    private StatTable? ExtractStatTable(HtmlDocument doc, string tableId)
    {
        // Basketball-reference sometimes wraps tables in comments for deferred loading
        var table = doc.DocumentNode.SelectSingleNode($"//table[@id='{tableId}']");

        if (table == null)
        {
            // Check inside HTML comments
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

        var statTable = new StatTable();

        // Headers
        var headerRow = table.SelectSingleNode(".//thead/tr[last()]");
        if (headerRow != null)
        {
            foreach (var th in headerRow.SelectNodes(".//th") ?? Enumerable.Empty<HtmlNode>())
            {
                statTable.Headers.Add(th.InnerText.Trim());
            }
        }

        // Data rows (skip sub-headers within tbody)
        var dataRows = table.SelectNodes(".//tbody/tr[not(contains(@class,'thead')) and not(contains(@class,'partial_table'))]");
        if (dataRows != null)
        {
            foreach (var row in dataRows)
            {
                var rowData = new Dictionary<string, string>();
                var th = row.SelectSingleNode(".//th");
                if (th != null && statTable.Headers.Count > 0)
                {
                    rowData[statTable.Headers[0]] = WebUtility.HtmlDecode(th.InnerText.Trim());
                }

                var cells = row.SelectNodes(".//td");
                if (cells != null)
                {
                    for (int i = 0; i < cells.Count && i + 1 < statTable.Headers.Count; i++)
                    {
                        rowData[statTable.Headers[i + 1]] = WebUtility.HtmlDecode(cells[i].InnerText.Trim());
                    }
                }

                if (rowData.Count > 0)
                    statTable.Rows.Add(rowData);
            }
        }

        // Career row from tfoot
        var footRow = table.SelectSingleNode(".//tfoot/tr");
        if (footRow != null)
        {
            var careerData = new Dictionary<string, string>();
            var th = footRow.SelectSingleNode(".//th");
            if (th != null && statTable.Headers.Count > 0)
            {
                careerData[statTable.Headers[0]] = WebUtility.HtmlDecode(th.InnerText.Trim());
            }

            var cells = footRow.SelectNodes(".//td");
            if (cells != null)
            {
                for (int i = 0; i < cells.Count && i + 1 < statTable.Headers.Count; i++)
                {
                    careerData[statTable.Headers[i + 1]] = WebUtility.HtmlDecode(cells[i].InnerText.Trim());
                }
            }

            if (careerData.Count > 0)
                statTable.Rows.Add(careerData);
        }

        return statTable;
    }
}
