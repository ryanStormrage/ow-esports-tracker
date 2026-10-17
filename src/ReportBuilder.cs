using System.Net;
using System.Text;
using System.Text.Json;

namespace OwEsportsTracker;

public static class ReportBuilder
{
    public static Report Build(string tournament, IEnumerable<Match> all)
    {
        // The same match can appear on a parent page and a sub-page; de-duplicate.
        var matches = all
            .GroupBy(m => (m.Team1, m.Team2, m.Date, m.DateText))
            .Select(g => g.OrderByDescending(m => m.IsFinished).First())
            .ToList();

        var table = new Dictionary<string, Standing>();
        Standing Get(string team) => table.TryGetValue(team, out var s) ? s : table[team] = new Standing { Team = team };

        foreach (var m in matches.Where(m => m.IsFinished && m.Team1 != "TBD" && m.Team2 != "TBD")
                                 .OrderByDescending(m => m.Date ?? DateTime.MinValue))
        {
            var a = Get(m.Team1);
            var b = Get(m.Team2);
            a.MapWins += m.Score1 ?? 0; a.MapLosses += m.Score2 ?? 0;
            b.MapWins += m.Score2 ?? 0; b.MapLosses += m.Score1 ?? 0;
            if (m.Winner == 1) { a.SeriesWins++; b.SeriesLosses++; }
            else if (m.Winner == 2) { b.SeriesWins++; a.SeriesLosses++; }
            if (a.Form.Count < 5) a.Form.Add(m.Winner == 1 ? 'W' : m.Winner == 2 ? 'L' : 'D');
            if (b.Form.Count < 5) b.Form.Add(m.Winner == 2 ? 'W' : m.Winner == 1 ? 'L' : 'D');
        }

        var standings = table.Values
            .OrderByDescending(s => s.SeriesWins)
            .ThenBy(s => s.SeriesLosses)
            .ThenByDescending(s => s.MapDiff)
            .ThenBy(s => s.Team)
            .ToList();

        var recent = matches.Where(m => m.IsFinished)
            .OrderByDescending(m => m.Date ?? DateTime.MinValue).Take(12).ToList();
        var upcoming = matches.Where(m => !m.IsFinished)
            .OrderBy(m => m.Date ?? DateTime.MaxValue).Take(12).ToList();

        return new Report(tournament, DateTime.UtcNow, standings, recent, upcoming, matches.Count);
    }

    public static void PrintToConsole(Report r)
    {
        Console.WriteLine();
        Console.WriteLine($"{r.Tournament}  ({r.MatchesParsed} matches parsed)");
        Console.WriteLine(new string('-', 58));
        Console.WriteLine($"{"#",-3}{"Team",-26}{"Series",-10}{"Maps",-10}{"Form",-8}");
        int i = 1;
        foreach (var s in r.Standings)
            Console.WriteLine($"{i++,-3}{Trunc(s.Team, 24),-26}{$"{s.SeriesWins}-{s.SeriesLosses}",-10}{$"{s.MapWins}-{s.MapLosses}",-10}{new string(s.Form.ToArray()),-8}");

        if (r.Upcoming.Count > 0)
        {
            Console.WriteLine("\nNext up:");
            foreach (var m in r.Upcoming.Take(5))
                Console.WriteLine($"  {m.DateText ?? "TBD",-26} {m.Team1} vs {m.Team2}");
        }
    }

    private static string Trunc(string s, int n) => s.Length <= n ? s : s[..(n - 1)] + "…";

    public static void WriteJson(Report r, string path)
    {
        var opts = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        File.WriteAllText(path, JsonSerializer.Serialize(r, opts));
    }

    public static void WriteHtml(Report r, string path)
    {
        static string E(string? s) => WebUtility.HtmlEncode(s ?? "");
        var sb = new StringBuilder();

        int rank = 1;
        foreach (var s in r.Standings)
        {
            var form = string.Concat(s.Form.Select(f => $"<i class=\"f{f}\">{f}</i>"));
            sb.Append($"<tr><td>{rank++}</td><td class=\"team\">{E(s.Team)}</td><td>{s.SeriesWins}–{s.SeriesLosses}</td>" +
                      $"<td>{s.MapWins}–{s.MapLosses}</td><td class=\"{(s.MapDiff >= 0 ? "pos" : "neg")}\">{s.MapDiff:+0;-0;0}</td><td>{form}</td></tr>");
        }
        var standingsRows = sb.ToString(); sb.Clear();

        string MatchRow(Match m)
        {
            var w1 = m.Winner == 1 ? " win" : ""; var w2 = m.Winner == 2 ? " win" : "";
            var score = m.IsFinished ? $"{m.Score1}<span>:</span>{m.Score2}" : "vs";
            return $"<li><span class=\"when\">{E(m.DateText ?? "TBD")}</span><span class=\"t1{w1}\">{E(m.Team1)}</span>" +
                   $"<b class=\"score\">{score}</b><span class=\"t2{w2}\">{E(m.Team2)}</span></li>";
        }

        var recent = string.Concat(r.Recent.Select(MatchRow));
        var upcoming = string.Concat(r.Upcoming.Select(MatchRow));
        var empty = "<li class=\"muted\">Nothing here yet.</li>";

        var html = $$"""
<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>{{E(r.Tournament)}} · Esports Tracker</title>
<style>
:root{--bg:#0f0d17;--panel:#1a1726;--line:#2e2a40;--text:#f1eefa;--muted:#9a94b0;--accent:#a78bfa;--win:#34d399;--loss:#f87171}
*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--text);font:15px/1.5 system-ui,Segoe UI,sans-serif}
.wrap{max-width:1080px;margin:0 auto;padding:32px 16px 60px}
.kicker{color:var(--accent);font:600 .78rem ui-monospace,Consolas,monospace;letter-spacing:.08em;text-transform:uppercase}
h1{margin:6px 0 4px;font-size:clamp(1.6rem,4vw,2.4rem);letter-spacing:-.02em}.muted{color:var(--muted)}
.grid{display:grid;grid-template-columns:1.3fr 1fr;gap:16px;margin-top:26px}
.panel{background:var(--panel);border:1px solid var(--line);border-radius:14px;padding:18px;overflow-x:auto}
h2{margin:0 0 12px;font-size:.8rem;text-transform:uppercase;letter-spacing:.08em;color:var(--muted)}
table{width:100%;border-collapse:collapse;min-width:420px}th,td{padding:8px 6px;text-align:left;border-bottom:1px solid var(--line)}
th{font-size:.72rem;color:var(--muted);text-transform:uppercase;letter-spacing:.06em}td.team{font-weight:600}
tr:last-child td{border-bottom:0}.pos{color:var(--win)}.neg{color:var(--loss)}
i{font-style:normal;display:inline-grid;place-items:center;width:20px;height:20px;border-radius:4px;font-size:.7rem;font-weight:700;margin-right:3px;color:#111}
.fW{background:var(--win)}.fL{background:var(--loss)}.fD{background:var(--muted)}
ul{list-style:none;margin:0;padding:0}li{display:grid;grid-template-columns:1fr auto 60px auto 1fr;gap:8px;align-items:center;padding:9px 0;border-bottom:1px solid var(--line)}
li:last-child{border-bottom:0}li .when{grid-column:1/-1;font-size:.75rem;color:var(--muted)}
li .t1{text-align:right;grid-column:1/3}li .t2{grid-column:4/6}li .score{text-align:center;font-variant-numeric:tabular-nums}
li .score span{color:var(--muted);margin:0 2px}li .win{color:var(--win);font-weight:700}
.stack{display:grid;gap:16px}footer{margin-top:26px;color:var(--muted);font-size:.85rem}a{color:var(--accent)}
@media(max-width:800px){.grid{grid-template-columns:1fr}}
</style></head><body><div class="wrap">
<div class="kicker">Overwatch esports tracker</div>
<h1>{{E(r.Tournament.Replace('_', ' ').Replace("/", " · "))}}</h1>
<div class="muted">{{r.MatchesParsed}} matches · updated {{r.GeneratedUtc:yyyy-MM-dd HH:mm}} UTC</div>
<div class="grid">
  <section class="panel"><h2>Standings</h2>
    <table><thead><tr><th>#</th><th>Team</th><th>Series</th><th>Maps</th><th>Diff</th><th>Last 5</th></tr></thead>
    <tbody>{{(standingsRows.Length > 0 ? standingsRows : "<tr><td colspan=6 class=muted>No completed matches yet.</td></tr>")}}</tbody></table>
  </section>
  <div class="stack">
    <section class="panel"><h2>Upcoming</h2><ul>{{(upcoming.Length > 0 ? upcoming : empty)}}</ul></section>
    <section class="panel"><h2>Recent results</h2><ul>{{(recent.Length > 0 ? recent : empty)}}</ul></section>
  </div>
</div>
<footer>Match data from <a href="https://liquipedia.net/overwatch/">Liquipedia</a>, licensed under
<a href="https://creativecommons.org/licenses/by-sa/3.0/">CC BY-SA 3.0</a>. Built with C# / .NET by
<a href="https://github.com/ryanstormrage">ryanstormrage</a>.</footer>
</div></body></html>
""";
        File.WriteAllText(path, html);
    }
}
