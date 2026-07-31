using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
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

        File.WriteAllText(paths.ReportPath, html, Encoding.UTF8);

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
        html.AppendLine(GetStyles());
        html.AppendLine(GetScript());
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
        html.AppendLine($"        <dt>{Encode(label)}</dt>");
        html.AppendLine($"        <dd>{Encode(value)}</dd>");
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
        html.AppendLine($"        <h2>{Encode(heading)} <span>{tracks.Count}</span></h2>");
        html.AppendLine($"        <p>{Encode(description)}</p>");
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
        html.AppendLine("          <col class=\"col-spotify\">");
        html.AppendLine("        </colgroup>");
        html.AppendLine("        <thead>");
        html.AppendLine("          <tr>");
        html.AppendLine("            <th>Song</th>");
        html.AppendLine("            <th>Album</th>");
        html.AppendLine("            <th>Date</th>");
        html.AppendLine("            <th>Source</th>");
        html.AppendLine("            <th data-sort=\"none\">Spotify</th>");
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
        string searchText = string.Join(
            " ",
            displayDate,
            track.Artists,
            track.Title,
            track.Album,
            sourceText);

        html.AppendLine($"          <tr data-search=\"{EncodeAttribute(searchText)}\">");
        html.AppendLine($"            <td class=\"song-cell\" data-sort=\"{EncodeAttribute(track.Title)}\">");
        html.AppendLine("              <div class=\"song\">");
        html.AppendLine(BuildCoverMarkup(track.AlbumImageUrl, track.Title));
        html.AppendLine("                <div class=\"song-info\">");
        html.AppendLine($"                  <div class=\"song-title\">{Encode(track.Title)}</div>");
        html.AppendLine($"                  <div class=\"song-artists\">{Encode(track.Artists)}</div>");
        html.AppendLine("                </div>");
        html.AppendLine("              </div>");
        html.AppendLine("            </td>");
        html.AppendLine($"            <td data-sort=\"{EncodeAttribute(track.Album)}\">{Encode(track.Album)}</td>");
        html.AppendLine($"            <td data-sort=\"{sortDate}\">{displayDate}</td>");
        html.AppendLine($"            <td data-sort=\"{sourceText}\"><span class=\"source-badge {sourceCssClass}\">{sourceText}</span></td>");
        html.AppendLine($"            <td><a class=\"spotify-link\" href=\"{EncodeAttribute(track.SpotifyUrl)}\" target=\"_blank\" rel=\"noopener\">Open</a></td>");
        html.AppendLine("          </tr>");
    }

    /// <summary>
    /// Renders an album cover thumbnail with a note-icon fallback for missing or broken images.
    /// </summary>
    private static string BuildCoverMarkup(string? albumImageUrl, string title)
    {
        const string FallbackIcon =
            "<svg class=\"cover-fallback\" viewBox=\"0 0 24 24\" aria-hidden=\"true\">" +
            "<path d=\"M9 18V5l12-2v13\"></path><circle cx=\"6\" cy=\"18\" r=\"3\"></circle>" +
            "<circle cx=\"18\" cy=\"16\" r=\"3\"></circle></svg>";

        if (string.IsNullOrWhiteSpace(albumImageUrl))
        {
            return "                <div class=\"cover\">" + FallbackIcon + "</div>";
        }

        string img =
            $"<img src=\"{EncodeAttribute(albumImageUrl)}\" alt=\"\" loading=\"lazy\" " +
            "onerror=\"this.style.display='none'\">";

        return "                <div class=\"cover\">" + img + FallbackIcon + "</div>";
    }

    private static string GetStyles() =>
        """
          <style>
            :root {
              color-scheme: dark;
              --bg: #121212;
              --panel: #181818;
              --panel-soft: #202020;
              --row-hover: #282828;
              --text: #ffffff;
              --muted: #b3b3b3;
              --border: #2a2a2a;
              --accent: #1db954;
              --accent-dark: #169c46;
              --accent-soft: rgba(29, 185, 84, 0.16);
              --warning: #ffb020;
              --warning-soft: rgba(255, 176, 32, 0.16);
              --shadow: 0 16px 36px rgba(0, 0, 0, 0.5);
            }

            * {
              box-sizing: border-box;
            }

            body {
              margin: 0;
              background: var(--bg);
              color: var(--text);
              font-family: "Segoe UI", Arial, sans-serif;
              line-height: 1.45;
            }

            .report-header,
            .toolbar,
            .report-section {
              width: min(1180px, calc(100% - 32px));
              margin-inline: auto;
            }

            .report-header {
              display: flex;
              align-items: flex-end;
              justify-content: space-between;
              gap: 24px;
              padding: 32px 0 20px;
            }

            .eyebrow {
              margin: 0 0 6px;
              color: var(--accent);
              font-size: 12px;
              font-weight: 700;
              letter-spacing: 0;
              text-transform: uppercase;
            }

            h1 {
              margin: 0;
              font-size: 32px;
              font-weight: 700;
            }

            .report-meta {
              display: grid;
              grid-template-columns: repeat(4, minmax(96px, 1fr));
              gap: 8px;
              margin: 0;
            }

            .report-meta div {
              min-width: 0;
              padding: 10px 12px;
              background: var(--panel);
              border: 1px solid var(--border);
              border-radius: 8px;
              box-shadow: var(--shadow);
            }

            .report-meta dt {
              color: var(--muted);
              font-size: 12px;
            }

            .report-meta dd {
              margin: 2px 0 0;
              font-weight: 700;
              white-space: nowrap;
            }

            .toolbar {
              display: grid;
              grid-template-columns: auto minmax(220px, 420px) 1fr;
              align-items: center;
              gap: 10px;
              margin-bottom: 22px;
            }

            .search-label {
              color: var(--muted);
              font-size: 13px;
              font-weight: 600;
            }

            #reportSearch {
              width: 100%;
              height: 38px;
              padding: 0 12px;
              background: var(--panel);
              border: 1px solid var(--border);
              border-radius: 8px;
              color: var(--text);
              font: inherit;
            }

            #reportSearch:focus {
              border-color: var(--accent);
              outline: 3px solid rgba(29, 185, 84, 0.18);
            }

            #filterStatus {
              color: var(--muted);
              font-size: 13px;
            }

            .report-section {
              margin-bottom: 34px;
            }

            .section-heading {
              display: flex;
              align-items: flex-end;
              justify-content: space-between;
              gap: 16px;
              margin-bottom: 10px;
            }

            h2 {
              margin: 0;
              font-size: 22px;
            }

            h2 span {
              color: var(--muted);
              font-size: 16px;
              font-weight: 600;
            }

            .section-heading p {
              margin: 4px 0 0;
              color: var(--muted);
              font-size: 14px;
            }

            .empty-state {
              margin: 0;
              padding: 16px;
              background: var(--panel);
              border: 1px solid var(--border);
              border-radius: 8px;
              color: var(--muted);
            }

            .table-wrap {
              overflow-x: auto;
              background: var(--panel);
              border: 1px solid var(--border);
              border-radius: 8px;
              box-shadow: var(--shadow);
            }

            table {
              width: 100%;
              min-width: 800px;
              table-layout: fixed;
              border-collapse: collapse;
            }

            .col-song {
              width: 40%;
            }

            .col-album {
              width: 26%;
            }

            .col-date {
              width: 12%;
            }

            .col-source {
              width: 14%;
            }

            .col-spotify {
              width: 8%;
            }

            th,
            td {
              padding: 10px 12px;
              border-bottom: 1px solid var(--border);
              text-align: left;
              vertical-align: middle;
              overflow: hidden;
              text-overflow: ellipsis;
              white-space: nowrap;
            }

            th {
              position: sticky;
              top: 0;
              z-index: 1;
              background: var(--panel-soft);
              color: var(--muted);
              cursor: pointer;
              font-size: 12px;
              font-weight: 700;
              text-transform: uppercase;
              user-select: none;
            }

            th[data-sort="none"] {
              cursor: default;
            }

            th.sort-asc::after {
              content: " asc";
              color: var(--accent);
              text-transform: none;
            }

            th.sort-desc::after {
              content: " desc";
              color: var(--accent);
              text-transform: none;
            }

            tr:hover td {
              background: var(--row-hover);
            }

            tr.is-hidden {
              display: none;
            }

            .song-cell {
              min-width: 320px;
            }

            .song {
              display: flex;
              align-items: center;
              gap: 12px;
            }

            .cover {
              position: relative;
              flex-shrink: 0;
              width: 44px;
              height: 44px;
              border-radius: 4px;
              overflow: hidden;
              background: var(--panel-soft);
              display: flex;
              align-items: center;
              justify-content: center;
            }

            .cover img {
              position: absolute;
              inset: 0;
              width: 100%;
              height: 100%;
              object-fit: cover;
            }

            .cover-fallback {
              width: 18px;
              height: 18px;
              fill: none;
              stroke: var(--muted);
              stroke-width: 1.8;
              stroke-linecap: round;
              stroke-linejoin: round;
            }

            .song-info {
              min-width: 0;
            }

            .song-title {
              color: var(--text);
              font-weight: 600;
              font-size: 14px;
              overflow: hidden;
              text-overflow: ellipsis;
              white-space: nowrap;
            }

            .song-artists {
              margin-top: 2px;
              color: var(--muted);
              font-size: 13px;
              overflow: hidden;
              text-overflow: ellipsis;
              white-space: nowrap;
            }

            .source-badge {
              display: inline-flex;
              align-items: center;
              min-height: 24px;
              padding: 3px 9px;
              border-radius: 999px;
              font-size: 12px;
              font-weight: 700;
              white-space: nowrap;
            }

            .source-release {
              background: var(--accent-soft);
              color: var(--accent);
            }

            .source-search {
              background: var(--warning-soft);
              color: var(--warning);
            }

            .spotify-link {
              display: inline-flex;
              align-items: center;
              justify-content: center;
              min-height: 30px;
              padding: 5px 12px;
              border-radius: 999px;
              background: var(--accent);
              color: #061a0e;
              font-weight: 700;
              text-decoration: none;
            }

            .spotify-link:hover {
              background: #22d365;
            }

            @media (max-width: 760px) {
              .report-header {
                display: block;
              }

              .report-meta {
                grid-template-columns: repeat(2, minmax(0, 1fr));
                margin-top: 16px;
              }

              .toolbar {
                grid-template-columns: 1fr;
              }
            }
          </style>
        """;

    private static string GetScript() =>
        """
          <script>
            function initReport() {
              document.querySelectorAll('table[data-sortable="true"]').forEach(initTable);

              const search = document.getElementById('reportSearch');
              if (search) {
                search.addEventListener('input', () => applySearch(search.value));
              }
            }

            function initTable(table) {
              const rows = Array.from(table.tBodies[0].rows);
              rows.forEach((row, index) => {
                row.dataset.originalIndex = String(index);
              });

              Array.from(table.tHead.rows[0].cells).forEach((header, columnIndex) => {
                if (header.dataset.sort === 'none') {
                  return;
                }

                header.dataset.sortState = 'none';
                header.addEventListener('click', () => sortTable(table, columnIndex, header));
              });
            }

            function sortTable(table, columnIndex, header) {
              const tbody = table.tBodies[0];
              const rows = Array.from(tbody.rows);
              const headers = Array.from(table.tHead.rows[0].cells);

              headers.forEach(item => item.classList.remove('sort-asc', 'sort-desc'));

              const currentState = header.dataset.sortState || 'none';
              const nextState = currentState === 'none'
                ? 'asc'
                : currentState === 'asc'
                  ? 'desc'
                  : 'none';

              headers.forEach(item => {
                item.dataset.sortState = item === header ? nextState : 'none';
              });

              rows.sort((rowA, rowB) => {
                if (nextState === 'none') {
                  return Number(rowA.dataset.originalIndex) - Number(rowB.dataset.originalIndex);
                }

                const cellA = getSortableCellValue(rowA.cells[columnIndex]);
                const cellB = getSortableCellValue(rowB.cells[columnIndex]);
                const comparison = cellA.localeCompare(cellB, 'en', {
                  numeric: true,
                  sensitivity: 'base'
                });

                return nextState === 'asc' ? comparison : -comparison;
              });

              rows.forEach(row => tbody.appendChild(row));

              if (nextState !== 'none') {
                header.classList.add(nextState === 'asc' ? 'sort-asc' : 'sort-desc');
              }
            }

            function getSortableCellValue(cell) {
              return (cell.dataset.sort || cell.textContent || '').trim();
            }

            function applySearch(value) {
              const query = value.trim().toLowerCase();
              let visibleRows = 0;
              let totalRows = 0;

              document.querySelectorAll('tbody tr').forEach(row => {
                totalRows += 1;
                const text = (row.dataset.search || row.textContent || '').toLowerCase();
                const isVisible = query.length === 0 || text.includes(query);
                row.classList.toggle('is-hidden', !isVisible);

                if (isVisible) {
                  visibleRows += 1;
                }
              });

              const status = document.getElementById('filterStatus');
              if (status) {
                status.textContent = query.length === 0
                  ? ''
                  : visibleRows + ' of ' + totalRows + ' rows shown';
              }
            }

            document.addEventListener('DOMContentLoaded', initReport);
          </script>
        """;

    private static string Encode(string value) =>
        HtmlEncoder.Default.Encode(value);

    private static string EncodeAttribute(string value) =>
        HtmlEncoder.Default.Encode(value);
}
