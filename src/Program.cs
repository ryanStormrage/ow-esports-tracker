using OwEsportsTracker;

// ------------------------------------------------------------------ arguments
const string DefaultPage = "Overwatch_Champions_Series/2026/NA/Stage_3";
const string UserAgent = "ow-esports-tracker/1.0 (https://github.com/ryanstormrage/ow-esports-tracker)";

var page = DefaultPage;
var outDir = "docs";
var refresh = false;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--refresh": refresh = true; break;
        case "--out" when i + 1 < args.Length: outDir = args[++i]; break;
        case "-h" or "--help":
            Console.WriteLine("""
                Usage: dotnet run -- [liquipedia page] [--refresh] [--out folder]

                  liquipedia page   path after liquipedia.net/overwatch/
                                    default: Overwatch_Champions_Series/2026/NA/Stage_3
                  --refresh         ignore the 1-hour cache and re-download
                  --out             where to write index.html + data.json (default: docs)

                Examples:
                  dotnet run
                  dotnet run -- Overwatch_Champions_Series/2026/EMEA/Stage_3
                  dotnet run -- Overwatch_Champions_Series/2026/World_Finals --refresh
                """);
            return 0;
        default:
            if (!args[i].StartsWith("--")) page = args[i].Replace("https://liquipedia.net/overwatch/", "").Replace(' ', '_');
            break;
    }
}

// ------------------------------------------------------------------ run
Console.WriteLine($"Tracking {page}");
using var client = new LiquipediaClient(UserAgent, Path.Combine(".cache"), refresh ? TimeSpan.Zero : TimeSpan.FromHours(1));

try
{
    var pages = await client.GetPageFamilyAsync(page);
    Console.WriteLine($"  found {pages.Count} page(s): {string.Join(", ", pages.Select(p => p.Replace(page, "~")))}");

    var texts = await client.GetWikitextAsync(pages);
    if (texts.Count == 0)
    {
        Console.Error.WriteLine($"No page called '{page}' on Liquipedia. Check the spelling (it's the part after liquipedia.net/overwatch/).");
        return 1;
    }

    var matches = texts.SelectMany(kv => WikitextParser.ParseMatches(kv.Key, kv.Value)).ToList();
    var report = ReportBuilder.Build(page, matches);

    ReportBuilder.PrintToConsole(report);

    Directory.CreateDirectory(outDir);
    ReportBuilder.WriteJson(report, Path.Combine(outDir, "data.json"));
    ReportBuilder.WriteHtml(report, Path.Combine(outDir, "index.html"));
    Console.WriteLine($"\nWrote {Path.Combine(outDir, "index.html")} and data.json");
    return 0;
}
catch (HttpRequestException ex)
{
    Console.Error.WriteLine($"Couldn't reach Liquipedia: {ex.Message}");
    return 2;
}
