namespace JakeBallReference.Models;

public class PlayerSearchResult
{
    public string Name { get; set; } = "";
    public string PlayerId { get; set; } = "";
    public string Url { get; set; } = "";
    public string YearsActive { get; set; } = "";
}

public class PlayerProfile
{
    public string Name { get; set; } = "";
    public string PlayerId { get; set; } = "";
    public string ImageUrl { get; set; } = "";
    public string Position { get; set; } = "";
    public string Height { get; set; } = "";
    public string Weight { get; set; } = "";
    public string BirthDate { get; set; } = "";
    public string College { get; set; } = "";
    public string Draft { get; set; } = "";
    public List<string> Seasons { get; set; } = new();
    public List<Accolade> Accolades { get; set; } = new();
    public StatTable? PerGameStats { get; set; }
    public StatTable? TotalStats { get; set; }
    public StatTable? AdvancedStats { get; set; }
}

public class Accolade
{
    public string Name { get; set; } = "";
    public int Count { get; set; }
    public List<string> Years { get; set; } = new();
}

public class StatTable
{
    public List<string> Headers { get; set; } = new();
    public List<Dictionary<string, string>> Rows { get; set; } = new();
}

public class PlayerStatsRequest
{
    public string? SeasonYear { get; set; }        // e.g. "2022-23"
    public int? CareerYear { get; set; }            // e.g. 2 (second year)
    public string? SeasonRangeStart { get; set; }   // e.g. "2019-20"
    public string? SeasonRangeEnd { get; set; }     // e.g. "2022-23"
    public int? CareerYearStart { get; set; }       // e.g. 1
    public int? CareerYearEnd { get; set; }         // e.g. 5
}
