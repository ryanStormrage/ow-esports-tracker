using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace OwEsportsTracker;

/// <summary>
/// Pulls {{Match ...}} templates out of Liquipedia wikitext.
///
/// A typical match looks like:
///   {{Match
///     |date=October 10, 2026 - 12:00 {{Abbr/PDT}}
///     |bestof=5
///     |opponent1={{TeamOpponent|team liquid|score=}}
///     |opponent2={{TeamOpponent|spacestation}}
///     |map1={{Map|map=Lijiang Tower|mode=Control|score1=2|score2=1|winner=1}}
///     ...
///   }}
/// The parser is deliberately forgiving: it reads explicit scores when present,
/// otherwise counts map winners, and skips anything it can't make sense of.
/// </summary>
public static partial class WikitextParser
{
    [GeneratedRegex(@"\{\{\s*Match\s*(?=[|}\r\n])")]
    private static partial Regex MatchStart();

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex Comments();

    [GeneratedRegex(@"\{\{[^{}]*\}\}")]
    private static partial Regex InnerTemplate();

    [GeneratedRegex(@"^map\d+$")]
    private static partial Regex MapKey();

    public static List<Match> ParseMatches(string page, string wikitext)
    {
        var text = Comments().Replace(wikitext, "");
        var matches = new List<Match>();
        // Liquipedia often writes |bestof= only on the first match of a list; later matches inherit it.
        var inheritedBestOf = 0;

        foreach (System.Text.RegularExpressions.Match m in MatchStart().Matches(text))
        {
            var body = ExtractTemplate(text, m.Index);
            if (body is null) continue;
            var (_, _, named) = SplitTemplate(body);

            var (team1, explicit1) = ParseOpponent(named.GetValueOrDefault("opponent1"));
            var (team2, explicit2) = ParseOpponent(named.GetValueOrDefault("opponent2"));
            if (team1 == "TBD" && team2 == "TBD") continue;

            // Count map wins in case scores weren't typed in by hand.
            int mapWins1 = 0, mapWins2 = 0;
            foreach (var (key, value) in named)
            {
                if (!MapKey().IsMatch(key)) continue;
                var (_, _, mapParams) = SplitTemplate(StripOuterBraces(value));
                switch (mapParams.GetValueOrDefault("winner")?.Trim())
                {
                    case "1": mapWins1++; break;
                    case "2": mapWins2++; break;
                }
            }

            int? score1 = explicit1 ?? (mapWins1 + mapWins2 > 0 ? mapWins1 : null);
            int? score2 = explicit2 ?? (mapWins1 + mapWins2 > 0 ? mapWins2 : null);

            int? winner = null;
            var winnerParam = named.GetValueOrDefault("winner")?.Trim();
            var finished = named.GetValueOrDefault("finished")?.Trim().ToLowerInvariant() is "true" or "1" or "yes";
            if (int.TryParse(named.GetValueOrDefault("bestof")?.Trim(), out var explicitBestOf) && explicitBestOf > 0)
                inheritedBestOf = explicitBestOf;
            var bestOf = inheritedBestOf;
            var date = ParseDate(named.GetValueOrDefault("date"));
            // A match more than 6 hours in the past with maps played is over, even if nobody set |finished=.
            if (!finished && date is not null && date < DateTime.Now.AddHours(-6) && mapWins1 + mapWins2 > 0)
                finished = true;

            if (winnerParam is "1" or "2" or "0")
                winner = int.Parse(winnerParam);
            else if (score1 is not null && score2 is not null)
            {
                var needed = bestOf > 0 ? bestOf / 2 + 1 : int.MaxValue;
                if (score1 >= needed) winner = 1;
                else if (score2 >= needed) winner = 2;
                else if (finished) winner = score1 > score2 ? 1 : score2 > score1 ? 2 : 0;
            }

            var dateText = named.GetValueOrDefault("date");
            matches.Add(new Match(page, team1, team2, score1, score2, winner, date, Clean(dateText)));
        }
        return matches;
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>Returns the template text starting at <paramref name="start"/> with balanced braces, or null.</summary>
    private static string? ExtractTemplate(string text, int start)
    {
        int depth = 0;
        for (int i = start; i < text.Length - 1; i++)
        {
            if (text[i] == '{' && text[i + 1] == '{') { depth++; i++; }
            else if (text[i] == '}' && text[i + 1] == '}')
            {
                depth--; i++;
                if (depth == 0) return text.Substring(start + 2, i - start - 3); // without outer {{ }}
            }
        }
        return null;
    }

    private static string StripOuterBraces(string s)
    {
        s = s.Trim();
        return s.StartsWith("{{") && s.EndsWith("}}") ? s[2..^2] : s;
    }

    /// <summary>Splits "Name|a|b|key=value" at top-level pipes into name, positional args and named args.</summary>
    private static (string name, List<string> positional, Dictionary<string, string> named) SplitTemplate(string inner)
    {
        var parts = new List<string>();
        var sb = new StringBuilder();
        int curly = 0, square = 0;
        for (int i = 0; i < inner.Length; i++)
        {
            char c = inner[i];
            char next = i + 1 < inner.Length ? inner[i + 1] : '\0';
            if (c == '{' && next == '{') { curly++; sb.Append("{{"); i++; continue; }
            if (c == '}' && next == '}') { curly--; sb.Append("}}"); i++; continue; }
            if (c == '[' && next == '[') { square++; sb.Append("[["); i++; continue; }
            if (c == ']' && next == ']') { square--; sb.Append("]]"); i++; continue; }
            if (c == '|' && curly == 0 && square == 0) { parts.Add(sb.ToString()); sb.Clear(); continue; }
            sb.Append(c);
        }
        parts.Add(sb.ToString());

        var name = parts[0].Trim();
        var positional = new List<string>();
        var named = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in parts.Skip(1))
        {
            var eq = p.IndexOf('=');
            // '=' inside a nested template doesn't make this a named arg
            var brace = p.IndexOf("{{", StringComparison.Ordinal);
            if (eq > 0 && (brace < 0 || eq < brace))
                named[p[..eq].Trim()] = p[(eq + 1)..].Trim();
            else
                positional.Add(p.Trim());
        }
        return (name, positional, named);
    }

    /// <summary>{{TeamOpponent|team liquid|score=3}} → ("Team Liquid", 3)</summary>
    private static (string team, int? score) ParseOpponent(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return ("TBD", null);
        var (_, positional, named) = SplitTemplate(StripOuterBraces(value));
        var raw = positional.FirstOrDefault(p => p.Length > 0)
                  ?? named.GetValueOrDefault("template")
                  ?? named.GetValueOrDefault("name")
                  ?? "";
        raw = raw.Trim();
        int? score = int.TryParse(named.GetValueOrDefault("score")?.Trim(), out var s) ? s : null;
        if (raw.Length == 0 || raw.Equals("tbd", StringComparison.OrdinalIgnoreCase)) return ("TBD", score);
        return (PrettyTeam(raw), score);
    }

    /// <summary>Liquipedia team keys are lowercase ("team liquid"); make them readable.</summary>
    private static string PrettyTeam(string key)
    {
        if (key.Any(char.IsUpper)) return key;        // already a display name
        var words = key.Replace('_', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Length <= 2 && w is not ("of" or "to")
                ? w.ToUpperInvariant()                          // "gk" -> "GK"
                : char.ToUpperInvariant(w[0]) + w[1..]);        // "liquid" -> "Liquid"
        return string.Join(' ', words);
    }

    private static string? Clean(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var t = s;
        while (InnerTemplate().IsMatch(t)) t = InnerTemplate().Replace(t, "");
        return t.Trim().TrimEnd('-').Trim();
    }

    private static readonly string[] DateFormats =
    {
        "MMMM d, yyyy - HH:mm", "MMMM d, yyyy HH:mm", "MMMM d, yyyy", "MMM d, yyyy - HH:mm", "MMM d, yyyy",
        "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-dd - HH:mm", "yyyy-MM-dd",
    };

    private static DateTime? ParseDate(string? raw)
    {
        var s = Clean(raw);
        if (s is null) return null;
        if (DateTime.TryParseExact(s, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var d))
            return d;
        return DateTime.TryParse(s.Replace(" - ", " "), CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out d)
            ? d : null;
    }
}
