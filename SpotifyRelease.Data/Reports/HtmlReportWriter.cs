using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using SpotifyRelease.Core.Abstractions;
using SpotifyRelease.Core.Models;
using SpotifyRelease.Data.Storage;

namespace SpotifyRelease.Data.Reports;

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
        html.AppendLine("        <thead>");
        html.AppendLine("          <tr>");
        html.AppendLine("            <th>Date</th>");
        html.AppendLine("            <th>Artists</th>");
        html.AppendLine("            <th>Song</th>");
        html.AppendLine("            <th>Album</th>");
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
        html.AppendLine($"            <td data-sort=\"{sortDate}\">{displayDate}</td>");
        html.AppendLine($"            <td data-sort=\"{EncodeAttribute(track.Artists)}\">{Encode(track.Artists)}</td>");
        html.AppendLine($"            <td data-sort=\"{EncodeAttribute(track.Title)}\"><strong>{Encode(track.Title)}</strong></td>");
        html.AppendLine($"            <td data-sort=\"{EncodeAttribute(track.Album)}\">{Encode(track.Album)}</td>");
        html.AppendLine($"            <td data-sort=\"{sourceText}\"><span class=\"source-badge {sourceCssClass}\">{sourceText}</span></td>");
        html.AppendLine($"            <td><a class=\"spotify-link\" href=\"{EncodeAttribute(track.SpotifyUrl)}\" target=\"_blank\" rel=\"noopener\">Open</a></td>");
        html.AppendLine("          </tr>");
    }

    private static string GetStyles() =>
        """
          <style>
            :root {
              color-scheme: light;
              --bg: #f4f6f5;
              --panel: #ffffff;
              --panel-soft: #f8faf9;
              --text: #17201b;
              --muted: #63706a;
              --border: #dce3df;
              --accent: #1db954;
              --accent-dark: #117a39;
              --warning: #b45f06;
              --shadow: 0 16px 36px rgba(16, 24, 20, 0.08);
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
              color: var(--accent-dark);
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
              min-width: 820px;
              border-collapse: collapse;
            }

            th,
            td {
              padding: 11px 12px;
              border-bottom: 1px solid var(--border);
              text-align: left;
              vertical-align: middle;
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
              color: var(--accent-dark);
              text-transform: none;
            }

            th.sort-desc::after {
              content: " desc";
              color: var(--accent-dark);
              text-transform: none;
            }

            tr:nth-child(even) td {
              background: #fbfcfb;
            }

            tr:hover td {
              background: #eef8f2;
            }

            tr.is-hidden {
              display: none;
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
              background: #e7f7ed;
              color: var(--accent-dark);
            }

            .source-search {
              background: #fff2df;
              color: var(--warning);
            }

            .spotify-link {
              display: inline-flex;
              align-items: center;
              justify-content: center;
              min-height: 30px;
              padding: 5px 10px;
              border-radius: 8px;
              background: var(--accent);
              color: #ffffff;
              font-weight: 700;
              text-decoration: none;
            }

            .spotify-link:hover {
              background: var(--accent-dark);
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
