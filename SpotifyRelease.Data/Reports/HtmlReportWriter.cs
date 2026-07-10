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
    /// Writes the sortable HTML report into the user profile.
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
            releaseLookbackDays);

        File.WriteAllText(paths.ReportPath, html, Encoding.UTF8);

        return paths.ReportPath;
    }

    private static string BuildReportHtml(
        IReadOnlyList<ReleaseTrack> uniqueTracks,
        IReadOnlyList<ReleaseTrack> duplicateTracks,
        int releaseLookbackDays)
    {
        int releaseSongCount = uniqueTracks.Count(
            track => track.Source == ReleaseTrackSource.ArtistAlbums);
        int searchSongCount = uniqueTracks.Count(
            track => track.Source == ReleaseTrackSource.Search);
        int duplicateSearchCount = duplicateTracks.Count(
            track => track.Source == ReleaseTrackSource.Search);

        StringBuilder html = new();

        html.AppendLine("<!DOCTYPE html>");
        html.AppendLine("<html lang=\"en\">");
        html.AppendLine("<head>");
        html.AppendLine("  <meta charset=\"utf-8\">");
        html.AppendLine("  <title>Spotify Release Report</title>");
        html.AppendLine(GetStyles());
        html.AppendLine(GetScript());
        html.AppendLine("</head>");
        html.AppendLine("<body>");
        html.AppendLine("  <h1>Spotify Release Report</h1>");
        html.AppendLine($"  <p><strong>Created at:</strong> {Encode(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))}</p>");
        html.AppendLine($"  <p><strong>Lookback:</strong> Last {releaseLookbackDays} days</p>");
        html.AppendLine("  <div class=\"summary\">");
        html.AppendLine($"    <p><strong>Added songs:</strong> {uniqueTracks.Count}</p>");
        html.AppendLine($"    <p><strong>Added from releases:</strong> {releaseSongCount}</p>");
        html.AppendLine($"    <p><strong>Added from song search:</strong> {searchSongCount}</p>");
        html.AppendLine($"    <p><strong>Duplicates removed:</strong> {duplicateTracks.Count}</p>");
        html.AppendLine($"    <p><strong>Song-search duplicates:</strong> {duplicateSearchCount}</p>");
        html.AppendLine("  </div>");

        AppendSection(html, "Added songs", uniqueTracks);
        AppendSection(html, "Duplicates", duplicateTracks);

        html.AppendLine("</body>");
        html.AppendLine("</html>");

        return html.ToString();
    }

    private static void AppendSection(
        StringBuilder html,
        string heading,
        IReadOnlyList<ReleaseTrack> tracks)
    {
        html.AppendLine($"  <h2>{Encode(heading)}</h2>");

        if (tracks.Count == 0)
        {
            html.AppendLine("  <p>No songs found.</p>");
            return;
        }

        AppendTrackTable(html, tracks);
    }

    private static void AppendTrackTable(
        StringBuilder html,
        IReadOnlyList<ReleaseTrack> tracks)
    {
        html.AppendLine("  <table>");
        html.AppendLine("    <thead>");
        html.AppendLine("      <tr>");
        html.AppendLine("        <th>Date</th>");
        html.AppendLine("        <th>Artists</th>");
        html.AppendLine("        <th>Song</th>");
        html.AppendLine("        <th>Source</th>");
        html.AppendLine("        <th>Spotify</th>");
        html.AppendLine("      </tr>");
        html.AppendLine("    </thead>");
        html.AppendLine("    <tbody>");

        foreach (ReleaseTrack track in tracks)
        {
            string trackType = track.Source == ReleaseTrackSource.Search
                ? "Song search"
                : "Release";

            html.AppendLine("      <tr>");
            html.AppendLine($"        <td>{track.ReleaseDate:yyyy-MM-dd}</td>");
            html.AppendLine($"        <td>{Encode(track.Artists)}</td>");
            html.AppendLine($"        <td>{Encode(track.Title)}</td>");
            html.AppendLine($"        <td>{trackType}</td>");
            html.AppendLine($"        <td><a href=\"{EncodeAttribute(track.SpotifyUrl)}\" target=\"_blank\" rel=\"noopener\">Open</a></td>");
            html.AppendLine("      </tr>");
        }

        html.AppendLine("    </tbody>");
        html.AppendLine("  </table>");
    }

    private static string GetStyles() =>
        """
          <style>
            body {
              font-family: Arial, sans-serif;
              margin: 24px;
              background: #fafafa;
              color: #222;
            }

            h1 {
              margin-bottom: 8px;
            }

            .summary {
              margin-bottom: 24px;
              padding: 12px;
              background: white;
              border: 1px solid #ddd;
              border-radius: 8px;
            }

            table {
              border-collapse: collapse;
              width: 100%;
              background: white;
              margin-bottom: 32px;
            }

            th, td {
              border: 1px solid #ddd;
              padding: 8px;
              text-align: left;
            }

            th {
              background-color: #f0f0f0;
              cursor: pointer;
              user-select: none;
            }

            th:hover {
              background-color: #e0e0e0;
            }

            th.sort-asc::after {
              content: " ^";
            }

            th.sort-desc::after {
              content: " v";
            }

            a {
              color: #1DB954;
              font-weight: bold;
              text-decoration: none;
            }

            a:hover {
              text-decoration: underline;
            }
          </style>
        """;

    private static string GetScript() =>
        """
          <script>
            function initSortableTables() {
              document.querySelectorAll('table').forEach(table => {
                const tbody = table.querySelector('tbody');
                const headers = table.querySelectorAll('th');
                table.dataset.originalHTML = tbody.innerHTML;

                headers.forEach((header, columnIndex) => {
                  if (columnIndex >= 3) {
                    header.style.cursor = 'default';
                    return;
                  }

                  header.dataset.sortState = 'none';
                  header.addEventListener('click', () => sortTable(table, columnIndex, header));
                });
              });
            }

            function sortTable(table, columnIndex, header) {
              const tbody = table.querySelector('tbody');
              const rows = Array.from(tbody.querySelectorAll('tr'));
              const headers = table.querySelectorAll('th');

              headers.forEach(h => h.classList.remove('sort-asc', 'sort-desc'));

              const currentState = header.dataset.sortState || 'none';
              const nextState = currentState === 'none'
                ? 'asc'
                : currentState === 'asc'
                  ? 'desc'
                  : 'none';

              header.dataset.sortState = nextState;

              headers.forEach(h => {
                if (h !== header) {
                  h.dataset.sortState = 'none';
                }
              });

              if (nextState === 'none') {
                tbody.innerHTML = table.dataset.originalHTML;
                return;
              }

              rows.sort((rowA, rowB) => {
                const cellA = rowA.cells[columnIndex].textContent.trim();
                const cellB = rowB.cells[columnIndex].textContent.trim();

                if (columnIndex === 0) {
                  const dateA = parseIsoDate(cellA);
                  const dateB = parseIsoDate(cellB);

                  if (dateA && dateB) {
                    const comparison = dateA - dateB;
                    return nextState === 'asc' ? comparison : -comparison;
                  }
                }

                const comparison = cellA.localeCompare(cellB, 'en');
                return nextState === 'asc' ? comparison : -comparison;
              });

              rows.forEach(row => tbody.appendChild(row));
              header.classList.add(nextState === 'asc' ? 'sort-asc' : 'sort-desc');
            }

            function parseIsoDate(dateStr) {
              const parts = dateStr.match(/(\d{4})-(\d{1,2})-(\d{1,2})/);
              return parts ? new Date(parts[1], parts[2] - 1, parts[3]) : null;
            }

            document.addEventListener('DOMContentLoaded', initSortableTables);
          </script>
        """;

    private static string Encode(string value) =>
        HtmlEncoder.Default.Encode(value);

    private static string EncodeAttribute(string value) =>
        HtmlEncoder.Default.Encode(value);
}
