namespace OwEsportsTracker;

/// <summary>One series between two teams (a Bo3, Bo5, etc.).</summary>
public sealed record Match(
    string Page,
    string Team1,
    string Team2,
    int? Score1,
    int? Score2,
    int? Winner,            // 1, 2, 0 (draw) or null if not played yet
    DateTime? Date,
    string? DateText)
{
    public bool IsFinished => Winner is not null;
    public string? WinnerName => Winner switch { 1 => Team1, 2 => Team2, _ => null };
}

/// <summary>Aggregated record for a team across every tracked match.</summary>
public sealed class Standing
{
    public required string Team { get; init; }
    public int SeriesWins { get; set; }
    public int SeriesLosses { get; set; }
    public int MapWins { get; set; }
    public int MapLosses { get; set; }
    public List<char> Form { get; } = new();   // most recent first, e.g. W L W

    public int MapDiff => MapWins - MapLosses;
    public double WinPct => SeriesWins + SeriesLosses == 0 ? 0 : (double)SeriesWins / (SeriesWins + SeriesLosses);
}

public sealed record Report(
    string Tournament,
    DateTime GeneratedUtc,
    IReadOnlyList<Standing> Standings,
    IReadOnlyList<Match> Recent,
    IReadOnlyList<Match> Upcoming,
    int MatchesParsed);
