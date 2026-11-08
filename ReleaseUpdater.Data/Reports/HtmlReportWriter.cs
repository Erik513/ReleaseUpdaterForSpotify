using System.Globalization;
using System.Text;
using ReleaseUpdater.Core.Abstractions;
using ReleaseUpdater.Core.Models;
using ReleaseUpdater.Data.Storage;

namespace ReleaseUpdater.Data.Reports;

public sealed class HtmlReportWriter : IReportWriter
{
    private readonly AppDataPaths paths;

    public HtmlReportWriter(AppDataPaths paths)
    {
        this.paths = paths;
    }

    public string ReportPath => paths.ReportPath;

    /// <summary>
    /// Writes a standalone, sortable HTML report into the user profile.
    /// </summary>
    public string WriteReport(
        IReadOnlyList<ReleaseTrack> uniqueTracks,
        IReadOnlyList<ReleaseTrack> duplicateTracks,
        int releaseLookbackDays)
    {
        paths.EnsureDirectory();

        string html = BuildReportHtml(
            uniqueTracks,
            duplicateTracks,
            releaseLookbackDays,
            DateTime.Now);

        AtomicFile.WriteAllText(paths.ReportPath, html);

        return paths.ReportPath;
    }

    private static string BuildReportHtml(
        IReadOnlyList<ReleaseTrack> uniqueTracks,
        IReadOnlyList<ReleaseTrack> duplicateTracks,
        int releaseLookbackDays,
        DateTime generatedAt)
    {
        StringBuilder html = new();

        html.AppendLine("<!DOCTYPE html>");
        html.AppendLine("<html lang=\"en\">");
        html.AppendLine("<head>");
        html.AppendLine("  <meta charset=\"utf-8\">");
        html.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        html.AppendLine("  <title>Spotify Release Report</title>");
        html.AppendLine(ReportTheme.GetStyles());
        html.AppendLine(ReportTheme.GetScript());
        html.AppendLine("</head>");
        html.AppendLine("<body>");
        AppendHeader(html, uniqueTracks.Count, duplicateTracks.Count, releaseLookbackDays, generatedAt);
        AppendToolbar(html);
        AppendSection(html, "Added songs", uniqueTracks, "Songs written to the Spotify playlist.");
        AppendSection(html, "Duplicates", duplicateTracks, "Songs found more than once and skipped.");
        html.AppendLine("</body>");
        html.AppendLine("</html>");

        return html.ToString();
    }

    private static void AppendHeader(
        StringBuilder html,
        int addedCount,
        int duplicateCount,
        int releaseLookbackDays,
        DateTime generatedAt)
    {
        html.AppendLine("  <header class=\"report-header\">");
        html.AppendLine("    <div>");
        html.AppendLine("      <p class=\"eyebrow\">Generated playlist report</p>");
        html.AppendLine("      <h1>Spotify Release Report</h1>");
        html.AppendLine("    </div>");
        html.AppendLine("    <dl class=\"report-meta\">");
        AppendMetaItem(
            html,
            "Created",
            generatedAt.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture));
        AppendMetaItem(html, "Lookback", $"Last {releaseLookbackDays} days");
        AppendMetaItem(html, "Added", addedCount.ToString(CultureInfo.InvariantCulture));
        AppendMetaItem(html, "Duplicates", duplicateCount.ToString(CultureInfo.InvariantCulture));
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
        html.AppendLine("    <input id=\"reportSearch\" type=\"search\" placeholder=\"Filter songs, artists, albums, or source\">");
        html.AppendLine("    <span id=\"filterStatus\" aria-live=\"polite\"></span>");
        html.AppendLine("  </div>");
    }

    private static void AppendSection(
        StringBuilder html,
        string heading,
        IReadOnlyList<ReleaseTrack> tracks,
        string description)
    {
        html.AppendLine("  <section class=\"report-section\">");
        html.AppendLine("    <div class=\"section-heading\">");
        html.AppendLine("      <div>");
        html.AppendLine($"        <h2>{ReportTheme.Encode(heading)} <span>{tracks.Count}</span></h2>");
        html.AppendLine($"        <p>{ReportTheme.Encode(description)}</p>");
        html.AppendLine("      </div>");
        html.AppendLine("    </div>");

        if (tracks.Count == 0)
        {
            html.AppendLine("    <p class=\"empty-state\">No songs found.</p>");
            html.AppendLine("  </section>");
            return;
        }

        AppendTrackTable(html, tracks);
        html.AppendLine("  </section>");
    }

    private static void AppendTrackTable(
        StringBuilder html,
        IReadOnlyList<ReleaseTrack> tracks)
    {
        html.AppendLine("    <div class=\"table-wrap\">");
        html.AppendLine("      <table data-sortable=\"true\">");
        html.AppendLine("        <colgroup>");
        html.AppendLine("          <col class=\"col-song\">");
        html.AppendLine("          <col class=\"col-album\">");
        html.AppendLine("          <col class=\"col-date\">");
        html.AppendLine("          <col class=\"col-source\">");
        html.AppendLine("        </colgroup>");
        html.AppendLine("        <thead>");
        html.AppendLine("          <tr>");
        html.AppendLine("            <th>Song</th>");
        html.AppendLine("            <th>Album</th>");
        html.AppendLine("            <th>Date</th>");
        html.AppendLine("            <th>Source</th>");
        html.AppendLine("          </tr>");
        html.AppendLine("        </thead>");
        html.AppendLine("        <tbody>");

        foreach (ReleaseTrack track in tracks)
        {
            AppendTrackRow(html, track);
        }

        html.AppendLine("        </tbody>");
        html.AppendLine("      </table>");
        html.AppendLine("    </div>");
    }

    private static void AppendTrackRow(
        StringBuilder html,
        ReleaseTrack track)
    {
        string sourceText = track.Source == ReleaseTrackSource.Search
            ? "Song search"
            : "Release";
        string sourceCssClass = track.Source == ReleaseTrackSource.Search
            ? "source-search"
            : "source-release";
        string displayDate = track.ReleaseDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        string sortDate = track.ReleaseDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        string artistNames = ReportTheme.JoinArtistNames(track.Artists);
        string searchText = string.Join(
            " ",
            displayDate,
            artistNames,
            track.Title,
            track.Album,
            sourceText);

        string albumUrl = "https://open.spotify.com/album/" + track.AlbumId;

        html.AppendLine($"          <tr data-search=\"{ReportTheme.EncodeAttribute(searchText)}\">");
        html.AppendLine($"            <td class=\"song-cell\" data-sort=\"{ReportTheme.EncodeAttribute(track.Title)}\">");
        html.AppendLine("              <div class=\"song\">");
        html.AppendLine(ReportTheme.BuildCoverMarkup(track.AlbumImageUrl, track.Title));
        html.AppendLine("                <div class=\"song-info\">");
        html.AppendLine(
            $"                  <div class=\"song-title\"><a class=\"track-link\" href=\"{ReportTheme.EncodeAttribute(track.SpotifyUrl)}\" target=\"_blank\" rel=\"noopener\">{ReportTheme.Encode(track.Title)}</a></div>");
        html.AppendLine($"                  <div class=\"song-artists\">{ReportTheme.BuildArtistLinksMarkup(track.Artists)}</div>");
        html.AppendLine("                </div>");
        html.AppendLine("              </div>");
        html.AppendLine("            </td>");
        html.AppendLine(
            $"            <td data-sort=\"{ReportTheme.EncodeAttribute(track.Album)}\"><a class=\"artist-link\" href=\"{ReportTheme.EncodeAttribute(albumUrl)}\" target=\"_blank\" rel=\"noopener\">{ReportTheme.Encode(track.Album)}</a></td>");
        html.AppendLine($"            <td data-sort=\"{sortDate}\">{displayDate}</td>");
        html.AppendLine($"            <td data-sort=\"{sourceText}\"><span class=\"source-badge {sourceCssClass}\">{sourceText}</span></td>");
        html.AppendLine("          </tr>");
    }
}
