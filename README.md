# Overwatch Esports Tracker (C# / .NET)

A .NET console app that tracks an Overwatch Champions Series (OWCS) stage, or any Overwatch tournament on
Liquipedia, and produces:

- **standings**: series record, map record, map differential and last-5 form
- **recent results** and **upcoming matches**
- `docs/index.html`, a static page you can host on GitHub Pages, plus `docs/data.json`

**Stack:** C# 12, .NET 8 (no NuGet packages), Liquipedia MediaWiki API, HTML/CSS

## Run it

You need the [.NET 8 SDK or newer](https://dotnet.microsoft.com/download).

```bash
dotnet run                                                       # OWCS 2026 NA Stage 3
dotnet run -- Overwatch_Champions_Series/2026/EMEA/Stage_3
dotnet run -- Overwatch_Champions_Series/2026/World_Finals --refresh
```

The argument is the part of a Liquipedia URL after `liquipedia.net/overwatch/`. The app also picks up the
page's sub-pages, such as `/Regular_Season` and `/Playoffs`.

## How it works

| file | job |
|---|---|
| `src/LiquipediaClient.cs` | `HttpClient` wrapper that follows Liquipedia's API rules: a custom User-Agent, gzip, ≥2 s between requests, and a 1-hour disk cache |
| `src/WikitextParser.cs` | finds every `{{Match …}}` template in the page source and reads the opponents, scores, map winners and date. If no score was typed in, it counts map wins instead |
| `src/ReportBuilder.cs` | removes duplicate matches, builds standings, and writes the console table, JSON and HTML |
| `src/Program.cs` | command-line arguments and orchestration |

Reading the page's **wikitext** instead of scraping its HTML means a redesign of the website won't break the
parser. Only a change to the match template format would.

## Data & licence

Match data comes from [Liquipedia](https://liquipedia.net/overwatch/) under
[CC BY-SA 3.0](https://creativecommons.org/licenses/by-sa/3.0/). Please keep the attribution in the generated page,
and respect the [API terms of use](https://liquipedia.net/api-terms-of-use): don't remove the rate limit or cache.
