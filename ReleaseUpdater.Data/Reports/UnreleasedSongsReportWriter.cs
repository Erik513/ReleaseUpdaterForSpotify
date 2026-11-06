using System.Globalization;
using System.Text;
using ReleaseUpdater.Core.Abstractions;
using ReleaseUpdater.Core.Models;
using ReleaseUpdater.Core.Services;
using ReleaseUpdater.Data.Storage;

namespace ReleaseUpdater.Data.Reports;

public sealed class UnreleasedSongsReportWriter : IUnreleasedSongsReportWriter
{
    private readonly AppDataPaths paths;

    public UnreleasedSongsReportWriter(AppDataPaths paths)
    {
        this.paths = paths;
    }

    public string ReportPath => paths.UnreleasedSongsReportPath;

    public string WriteReport(IReadOnlyList<SongMatchResult> results)
    {
        paths.EnsureDirectory();

        string html = BuildReportHtml(results, DateTime.Now);

        AtomicFile.WriteAllText(paths.UnreleasedSongsReportPath, html);

        return paths.UnreleasedSongsReportPath;
    }

    private static string BuildReportHtml(
        IReadOnlyList<SongMatchResult> results,
        DateTime generatedAt)
    {
        List<SongMatchResult> found = results.Where(result => result.IsFound).ToList();
        List<SongMatchResult> possible = results.Where(result => result.IsPossible).ToList();
        List<SongMatchResult> notFound = results.Where(result => !result.IsFound && !result.IsPossible).ToList();

        StringBuilder html = new();

        html.AppendLine("<!DOCTYPE html>");
        html.AppendLine("<html lang=\"en\">");
        html.AppendLine("<head>");
        html.AppendLine("  <meta charset=\"utf-8\">");
        html.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        html.AppendLine("  <title>Spotify Song Check Report</title>");
        html.AppendLine(ReportTheme.GetStyles());
        html.AppendLine(ReportTheme.GetScript());
        html.AppendLine("</head>");
        html.AppendLine("<body>");
        AppendHeader(html, results.Count, found.Count, possible.Count, notFound.Count, generatedAt);
        AppendToolbar(html);
        AppendFoundSection(html, found);
        AppendPossibleSection(html, possible);
        AppendNotFoundSection(html, notFound);
        html.AppendLine("</body>");
        html.AppendLine("</html>");

        return html.ToString();
    }

    private static void AppendHeader(
        StringBuilder html,
        int checkedCount,
        int foundCount,
        int possibleCount,
        int notFoundCount,
        DateTime generatedAt)
    {
        html.AppendLine("  <header class=\"report-header\">");
        html.AppendLine("    <div>");
        html.AppendLine("      <p class=\"eyebrow\">Unreleased songs check</p>");
        html.AppendLine("      <h1>Spotify Song Check Report</h1>");
        html.AppendLine("    </div>");
        html.AppendLine("    <dl class=\"report-meta\">");
        AppendMetaItem(
            html,
            "Created",
            generatedAt.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture));
        AppendMetaItem(html, "Checked", checkedCount.ToString(CultureInfo.InvariantCulture));
        AppendMetaItem(html, "Found", foundCount.ToString(CultureInfo.InvariantCulture));
        AppendMetaItem(html, "Possible", possibleCount.ToString(CultureInfo.InvariantCulture));
        AppendMetaItem(html, "Not found", notFoundCount.ToString(CultureInfo.InvariantCulture));
        html.AppendLine("    </dl>");
        html.AppendLine("  </header>");
    }

    private static void AppendMetaItem(
        StringBuilder html,
        string label,
        string value)
    {
        html.AppendLine("      <div>");
        html.AppendLine($"        <dt>{ReportTheme.Encode(label)}</dt>");
        html.AppendLine($"        <dd>{ReportTheme.Encode(value)}</dd>");
        html.AppendLine("      </div>");
    }

    private static void AppendToolbar(StringBuilder html)
    {
        html.AppendLine("  <div class=\"toolbar\">");
        html.AppendLine("    <label class=\"search-label\" for=\"reportSearch\">Search</label>");
        html.AppendLine("    <input id=\"reportSearch\" type=\"search\" placeholder=\"Filter songs or artists\">");
        html.AppendLine("    <span id=\"filterStatus\" aria-live=\"polite\"></span>");
        html.AppendLine("  </div>");
    }

    private static void AppendFoundSection(StringBuilder html, IReadOnlyList<SongMatchResult> results) =>
        AppendMatchSection(
            html,
            results,
            "Found on Spotify",
            "These songs from your list are now available on Spotify.",
            "No matches yet.",
            showEntry: false);

    // Only a hint each: the title or the artist fits, the other does not. The entry of the user is shown next to what Spotify has, so
    // that the two can be compared at a glance. (No section at all when there is none.)
    private static void AppendPossibleSection(StringBuilder html, IReadOnlyList<SongMatchResult> results)
    {
        if (results.Count == 0)
        {
            return;
        }

        AppendMatchSection(
            html,
            results,
            "Possible matches",
            "Only the title or only the artist fits - this may be the song, or another one. Worth a look.",
            string.Empty,
            showEntry: true);
    }

    private static void AppendMatchSection(
        StringBuilder html,
        IReadOnlyList<SongMatchResult> results,
        string heading,
        string description,
        string emptyText,
        bool showEntry)
    {
        html.AppendLine("  <section class=\"report-section\">");
        html.AppendLine("    <div class=\"section-heading\">");
        html.AppendLine("      <div>");
        html.AppendLine($"        <h2>{ReportTheme.Encode(heading)} <span>{results.Count}</span></h2>");
        html.AppendLine($"        <p>{ReportTheme.Encode(description)}</p>");
        html.AppendLine("      </div>");
        html.AppendLine("    </div>");

        if (results.Count == 0)
        {
            html.AppendLine($"    <p class=\"empty-state\">{ReportTheme.Encode(emptyText)}</p>");
            html.AppendLine("  </section>");
            return;
        }

        html.AppendLine("    <div class=\"table-wrap\">");
        html.AppendLine("      <table data-sortable=\"true\">");
        html.AppendLine("        <colgroup>");
        html.AppendLine("          <col class=\"col-song\">");
        html.AppendLine("          <col class=\"col-album\">");
        html.AppendLine("          <col class=\"col-date\">");
        html.AppendLine("          <col class=\"col-match\">");
        html.AppendLine("        </colgroup>");
        html.AppendLine("        <thead>");
        html.AppendLine("          <tr>");
        html.AppendLine("            <th>Song</th>");
        html.AppendLine("            <th>Album</th>");
        html.AppendLine("            <th>Release date</th>");
        html.AppendLine("            <th>Match</th>");
        html.AppendLine("          </tr>");
        html.AppendLine("        </thead>");
        html.AppendLine("        <tbody>");

        foreach (SongMatchResult result in results)
        {
            AppendFoundRow(html, result, showEntry);
        }

        html.AppendLine("        </tbody>");
        html.AppendLine("      </table>");
        html.AppendLine("    </div>");
        html.AppendLine("  </section>");
    }

    private static void AppendFoundRow(StringBuilder html, SongMatchResult result, bool showEntry)
    {
        SpotifyTrackItem match = result.Match!;
        string albumName = match.Album?.Name ?? string.Empty;
        string albumImageUrl = match.Album?.ImageUrl ?? string.Empty;
        DateOnly? releaseDate = match.Album?.ReleaseDate;
        string displayDate = releaseDate.HasValue
            ? releaseDate.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
            : string.Empty;
        string sortDate = releaseDate.HasValue
            ? releaseDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : string.Empty;
        string artistNames = ReportTheme.JoinArtistNames(match.Artists);
        string searchText = string.Join(" ", match.Title, artistNames, albumName);

        html.AppendLine($"          <tr data-search=\"{ReportTheme.EncodeAttribute(searchText)}\">");
        html.AppendLine($"            <td class=\"song-cell\" data-sort=\"{ReportTheme.EncodeAttribute(match.Title)}\">");
        html.AppendLine("              <div class=\"song\">");
        html.AppendLine(ReportTheme.BuildCoverMarkup(albumImageUrl, match.Title));
        html.AppendLine("                <div class=\"song-info\">");
        html.AppendLine(
            $"                  <div class=\"song-title\"><a class=\"track-link\" href=\"{ReportTheme.EncodeAttribute(match.SpotifyUrl)}\" target=\"_blank\" rel=\"noopener\">{ReportTheme.Encode(match.Title)}</a></div>");
        html.AppendLine($"                  <div class=\"song-artists\">{ReportTheme.BuildArtistLinksMarkup(match.Artists)}</div>");

        if (showEntry)
        {
            html.AppendLine(
                $"                  <div class=\"song-artists\">Your entry: {ReportTheme.Encode(result.Song.Title)} - {ReportTheme.Encode(result.Song.Artist)}</div>");
        }

        AppendSourceLink(html, result.Song);

        html.AppendLine("                </div>");
        html.AppendLine("              </div>");
        html.AppendLine("            </td>");
        html.AppendLine($"            <td data-sort=\"{ReportTheme.EncodeAttribute(albumName)}\">{ReportTheme.Encode(albumName)}</td>");
        html.AppendLine($"            <td data-sort=\"{sortDate}\">{displayDate}</td>");

        bool isConfident = result.Confidence == MatchConfidence.Confident;
        string confidenceText = result.Confidence switch
        {
            MatchConfidence.Confident => "Confident",
            MatchConfidence.Partial => result.Hint == MatchHint.ArtistOnly ? "Artist matches" : "Title matches",
            _ => result.Hint == MatchHint.SwappedFields ? "Similar - title and artist swapped" : "Similar"
        };
        string confidenceCssClass = isConfident ? "confidence-confident" : "confidence-similar";
        html.AppendLine(
            $"            <td data-sort=\"{confidenceText}\"><span class=\"source-badge {confidenceCssClass}\">{confidenceText}</span></td>");
        html.AppendLine("          </tr>");
    }

    // Where the user found the song (the link of the entry): a link in the report when it is a web address - nothing else is ever made
    // a link, whatever was typed - shown with the name of the site.
    private static void AppendSourceLink(StringBuilder html, TrackedSong song)
    {
        string? address = TrackedSongLinks.Normalize(song.Link);

        if (address is null)
        {
            return;
        }

        string site = new Uri(address).Host;

        html.AppendLine(
            $"                  <div class=\"song-artists\">Source: <a class=\"track-link\" href=\"{ReportTheme.EncodeAttribute(address)}\" target=\"_blank\" rel=\"noopener\">{ReportTheme.Encode(site)}</a></div>");
    }

    private static void AppendNotFoundSection(StringBuilder html, IReadOnlyList<SongMatchResult> results)
    {
        html.AppendLine("  <section class=\"report-section\">");
        html.AppendLine("    <div class=\"section-heading\">");
        html.AppendLine("      <div>");
        html.AppendLine($"        <h2>Not found <span>{results.Count}</span></h2>");
        html.AppendLine("        <p>No confident Spotify match yet - still worth checking manually.</p>");
        html.AppendLine("      </div>");
        html.AppendLine("    </div>");

        if (results.Count == 0)
        {
            html.AppendLine("    <p class=\"empty-state\">Every tracked song was found.</p>");
            html.AppendLine("  </section>");
            return;
        }

        html.AppendLine("    <div class=\"table-wrap\">");
        html.AppendLine("      <table data-sortable=\"true\">");
        html.AppendLine("        <colgroup>");
        html.AppendLine("          <col class=\"col-song\">");
        html.AppendLine("        </colgroup>");
        html.AppendLine("        <thead>");
        html.AppendLine("          <tr>");
        html.AppendLine("            <th>Song</th>");
        html.AppendLine("          </tr>");
        html.AppendLine("        </thead>");
        html.AppendLine("        <tbody>");

        foreach (SongMatchResult result in results)
        {
            string title = result.Song.Title;
            string artist = result.Song.Artist;
            string searchText = string.Join(" ", title, artist);

            html.AppendLine($"          <tr data-search=\"{ReportTheme.EncodeAttribute(searchText)}\">");
            html.AppendLine($"            <td class=\"song-cell\" data-sort=\"{ReportTheme.EncodeAttribute(title)}\">");
            html.AppendLine("              <div class=\"song\">");
            html.AppendLine(ReportTheme.BuildCoverMarkup(null, title));
            html.AppendLine("                <div class=\"song-info\">");
            html.AppendLine($"                  <div class=\"song-title\">{ReportTheme.Encode(title)}</div>");
            html.AppendLine($"                  <div class=\"song-artists\">{ReportTheme.Encode(artist)}</div>");
            AppendSourceLink(html, result.Song);
            html.AppendLine("                </div>");
            html.AppendLine("              </div>");
            html.AppendLine("            </td>");
            html.AppendLine("          </tr>");
        }

        html.AppendLine("        </tbody>");
        html.AppendLine("      </table>");
        html.AppendLine("    </div>");
        html.AppendLine("  </section>");
    }
}
