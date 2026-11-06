using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using ReleaseUpdater.Core.Models;

namespace ReleaseUpdater.Core.Services;

public enum TrackedSongsFormat
{
    Json,
    Csv,
    Xlsx,
    Docx
}

/// <summary>The songs read from a file, and how many entries of it could not be used (a title or an artist was missing).</summary>
public sealed record TrackedSongsImport(IReadOnlyList<TrackedSong> Songs, int SkippedCount);

/// <summary>The list after an import: what was added, and how many of the imported songs were in it already.</summary>
public sealed record TrackedSongsMerge(IReadOnlyList<TrackedSong> Songs, int AddedCount, int DuplicateCount);

/// <summary>
/// Writes the tracked songs to a file and reads them back: an Excel workbook (.xlsx), a Word document with a table (.docx),
/// JSON (a list of <c>{ "title", "artist", "link" }</c>, the shape of the app's own list) or CSV (<c>Title;Artist;Link</c>, with quotes where a
/// text has a delimiter, a quote or a line break). Bytes in, bytes out: the file itself is the caller's business.
/// </summary>
public static class TrackedSongsTransfer
{
    private static readonly JsonSerializerOptions JsonWriteOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly char[] CsvDelimiters = { ';', ',', '\t' };

    /// <summary>The format of a file by its ending (.xlsx, .docx, .csv); JSON for everything else.</summary>
    public static TrackedSongsFormat FormatForPath(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".xlsx" => TrackedSongsFormat.Xlsx,
            ".docx" => TrackedSongsFormat.Docx,
            ".csv" => TrackedSongsFormat.Csv,
            _ => TrackedSongsFormat.Json
        };

    /// <summary>The content of a file in the format. A CSV has a byte order mark so that a spreadsheet reads its umlauts right.</summary>
    public static byte[] Write(IReadOnlyList<TrackedSong> songs, TrackedSongsFormat format) =>
        format switch
        {
            TrackedSongsFormat.Xlsx => TrackedSongsXlsx.Write(songs),
            TrackedSongsFormat.Docx => TrackedSongsDocx.Write(songs),
            TrackedSongsFormat.Csv => new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetPreamble()
                .Concat(Encoding.UTF8.GetBytes(ToCsv(songs))).ToArray(),
            _ => Encoding.UTF8.GetBytes(ToJson(songs))
        };

    /// <exception cref="FormatException">The content is not a list of songs in that format.</exception>
    public static TrackedSongsImport Read(byte[] content, TrackedSongsFormat format) =>
        format switch
        {
            TrackedSongsFormat.Xlsx => FromTable(TrackedSongsXlsx.ReadTable(content)),
            TrackedSongsFormat.Docx => FromTable(TrackedSongsDocx.ReadTable(content)),
            TrackedSongsFormat.Csv => ParseCsv(DecodeText(content)),
            _ => ParseJson(DecodeText(content))
        };

    /// <summary>The text of a JSON or CSV file (UTF-8, with or without a byte order mark).</summary>
    public static string Serialize(IReadOnlyList<TrackedSong> songs, TrackedSongsFormat format) =>
        format switch
        {
            TrackedSongsFormat.Csv => ToCsv(songs),
            TrackedSongsFormat.Json => ToJson(songs),
            _ => throw new ArgumentException("Only JSON and CSV are text.", nameof(format))
        };

    /// <exception cref="FormatException">The text is not a list of songs in that format.</exception>
    public static TrackedSongsImport Parse(string text, TrackedSongsFormat format) =>
        format switch
        {
            TrackedSongsFormat.Csv => ParseCsv(text),
            TrackedSongsFormat.Json => ParseJson(text),
            _ => throw new ArgumentException("Only JSON and CSV are text.", nameof(format))
        };

    private static string DecodeText(byte[] content) =>
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false).GetString(content).TrimStart('﻿');

    /// <summary>True for the formats that are a table (CSV, Excel, Word): the user may choose its columns.</summary>
    public static bool IsTable(TrackedSongsFormat format) =>
        format is TrackedSongsFormat.Csv or TrackedSongsFormat.Xlsx or TrackedSongsFormat.Docx;

    /// <summary>The cells of a CSV, Excel or Word file, not yet read as songs.</summary>
    /// <exception cref="FormatException">The content is not a table in that format.</exception>
    public static ImportTable ReadTable(byte[] content, TrackedSongsFormat format) =>
        format switch
        {
            TrackedSongsFormat.Xlsx => TrackedSongsXlsx.ReadTable(content),
            TrackedSongsFormat.Docx => TrackedSongsDocx.ReadTable(content),
            TrackedSongsFormat.Csv => ParseDelimitedText(DecodeText(content)),
            _ => throw new ArgumentException("JSON is not a table.", nameof(format))
        };

    /// <summary>
    /// The cells of what was copied from a spreadsheet or a Word table (tabs between the cells, a line for a row); text with
    /// semicolons or commas as a CSV file has them is understood too.
    /// </summary>
    public static ImportTable ParseClipboardText(string text)
    {
        text = text.TrimStart('﻿');
        char delimiter = text.Contains('\t') ? '\t' : DetectDelimiter(text);

        try
        {
            return new ImportTable(ReadCsvRows(text, delimiter));
        }
        catch (FormatException)
        {
            // (A quote that is not closed - 12" vinyl - in text that is not a CSV: read it line by line, quotes as they are.)
            return new ImportTable(
                text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None)
                    .Select(line => (IReadOnlyList<string>)line.Split(delimiter))
                    .ToList());
        }
    }

    /// <summary>The songs in the two columns that the user chose, without the header row when there is one.</summary>
    public static TrackedSongsImport SongsFromTable(ImportTable table, ImportColumnChoice choice)
    {
        List<TrackedSong> songs = new();
        int skipped = 0;

        for (int index = choice.HasHeader ? 1 : 0; index < table.Rows.Count; index++)
        {
            if (table.Rows[index].All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            string title = table.Cell(index, choice.TitleColumn).Trim();
            string artist = table.Cell(index, choice.ArtistColumn).Trim();

            if (title.Length == 0 || artist.Length == 0)
            {
                skipped++;
                continue;
            }

            // The hyperlink behind the text of the cell is the address (the text may only say "Link"); without one, the text is.
            string link = choice.LinkColumn is int linkColumn
                ? table.Hyperlink(index, linkColumn) ?? table.Cell(index, linkColumn).Trim()
                : string.Empty;

            songs.Add(new TrackedSong(title, artist, link.Length == 0 ? null : link));
        }

        return new TrackedSongsImport(songs, skipped);
    }

    /// <summary>Adds the imported songs that are not in the list yet (same title and artist, whatever the case) behind it.</summary>
    public static TrackedSongsMerge Merge(IReadOnlyList<TrackedSong> existing, IReadOnlyList<TrackedSong> imported)
    {
        List<TrackedSong> merged = new(existing);
        Dictionary<string, int> indexOfKey = new();
        int added = 0;
        int duplicates = 0;

        for (int i = 0; i < existing.Count; i++)
        {
            indexOfKey.TryAdd(Key(existing[i]), i);
        }

        foreach (TrackedSong song in imported)
        {
            if (!indexOfKey.TryGetValue(Key(song), out int index))
            {
                indexOfKey[Key(song)] = merged.Count;
                merged.Add(song);
                added++;
            }
            else
            {
                duplicates++;

                // The song is in the list already; a link that it did not have and the import brings is not thrown away.
                if (string.IsNullOrWhiteSpace(merged[index].Link) && !string.IsNullOrWhiteSpace(song.Link))
                {
                    merged[index] = merged[index] with { Link = song.Link };
                }
            }
        }

        return new TrackedSongsMerge(merged, added, duplicates);
    }

    private static string Key(TrackedSong song) =>
        song.Title.Trim().ToLowerInvariant() + "\u0001" + song.Artist.Trim().ToLowerInvariant();

    // ---- JSON ----

    private static string ToJson(IReadOnlyList<TrackedSong> songs) =>
        JsonSerializer.Serialize(
            songs.Select(song => string.IsNullOrWhiteSpace(song.Link)
                ? (object)new { title = song.Title, artist = song.Artist }
                : new { title = song.Title, artist = song.Artist, link = song.Link.Trim() }).ToList(),
            JsonWriteOptions);

    private static TrackedSongsImport ParseJson(string text)
    {
        try
        {
            using JsonDocument json = JsonDocument.Parse(text, new JsonDocumentOptions { AllowTrailingCommas = true });

            if (json.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw new FormatException("The file is not a list of songs.");
            }

            List<TrackedSong> songs = new();
            int skipped = 0;

            foreach (JsonElement item in json.RootElement.EnumerateArray())
            {
                string? title = item.ValueKind == JsonValueKind.Object ? ReadText(item, "title") : null;
                string? artist = item.ValueKind == JsonValueKind.Object ? ReadText(item, "artist") : null;

                if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(artist))
                {
                    skipped++;
                    continue;
                }

                string? link = item.ValueKind == JsonValueKind.Object ? ReadText(item, "link") : null;

                songs.Add(new TrackedSong(title.Trim(), artist.Trim(), string.IsNullOrWhiteSpace(link) ? null : link.Trim()));
            }

            return new TrackedSongsImport(songs, skipped);
        }
        catch (JsonException ex)
        {
            throw new FormatException("The file is not valid JSON.", ex);
        }
    }

    private static string? ReadText(JsonElement element, string name)
    {
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase) &&
                property.Value.ValueKind == JsonValueKind.String)
            {
                return property.Value.GetString();
            }
        }

        return null;
    }

    // ---- CSV ----

    private static string ToCsv(IReadOnlyList<TrackedSong> songs)
    {
        StringBuilder builder = new();
        builder.Append("Title;Artist;Link\r\n");

        foreach (TrackedSong song in songs)
        {
            builder.Append(CsvField(song.Title)).Append(';').Append(CsvField(song.Artist)).Append(';').Append(CsvField(song.Link ?? string.Empty)).Append("\r\n");
        }

        return builder.ToString();
    }

    private static string CsvField(string value) =>
        value.IndexOfAny(new[] { ';', ',', '"', '\r', '\n', '\t' }) >= 0
            ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;

    private static TrackedSongsImport ParseCsv(string text)
    {
        text = text.TrimStart('﻿');

        return SongsFromRows(ParseDelimitedText(text).Rows);
    }

    private static ImportTable ParseDelimitedText(string text)
    {
        text = text.TrimStart('﻿');

        return new ImportTable(ReadCsvRows(text, DetectDelimiter(text)));
    }

    // The rows of a table (a CSV, a sheet, a Word table) read with the columns that look right (see ImportTable.GuessColumns): the title and the
    // artist, and the link when there is a column of them; a header row and empty rows are left out, incomplete rows are counted.
    internal static TrackedSongsImport SongsFromRows(IReadOnlyList<IReadOnlyList<string>> rows) =>
        FromTable(new ImportTable(rows));

    private static TrackedSongsImport FromTable(ImportTable table) =>
        SongsFromTable(table, table.GuessColumns());

    // The delimiter that a spreadsheet of the user's region used: the one that is most common in the first line.
    private static char DetectDelimiter(string text)
    {
        int end = text.IndexOfAny(new[] { '\r', '\n' });
        string firstLine = end < 0 ? text : text.Substring(0, end);
        char best = ';';
        int bestCount = 0;

        foreach (char candidate in CsvDelimiters)
        {
            int count = firstLine.Count(character => character == candidate);

            if (count > bestCount)
            {
                best = candidate;
                bestCount = count;
            }
        }

        return best;
    }

    private static List<IReadOnlyList<string>> ReadCsvRows(string text, char delimiter)
    {
        List<IReadOnlyList<string>> rows = new();
        List<string> row = new();
        StringBuilder field = new();
        bool quoted = false;

        for (int i = 0; i < text.Length; i++)
        {
            char character = text[i];

            if (quoted)
            {
                if (character == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else if (character == '"')
                {
                    quoted = false;
                }
                else
                {
                    field.Append(character);
                }

                continue;
            }

            if (character == '"' && field.Length == 0)
            {
                quoted = true;
            }
            else if (character == delimiter)
            {
                row.Add(field.ToString());
                field.Clear();
            }
            else if (character == '\r' || character == '\n')
            {
                if (character == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                row.Add(field.ToString());
                field.Clear();
                rows.Add(row);
                row = new List<string>();
            }
            else
            {
                field.Append(character);
            }
        }

        if (quoted)
        {
            throw new FormatException("A quote in the file is not closed.");
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }

        return rows;
    }
}
