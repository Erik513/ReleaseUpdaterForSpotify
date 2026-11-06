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
/// JSON (a list of <c>{ "title", "artist" }</c>, the shape of the app's own list) or CSV (<c>Title;Artist</c>, with quotes where a
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
            TrackedSongsFormat.Xlsx => SongsFromRows(TrackedSongsXlsx.ReadRows(content)),
            TrackedSongsFormat.Docx => SongsFromRows(TrackedSongsDocx.ReadRows(content)),
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

    /// <summary>Adds the imported songs that are not in the list yet (same title and artist, whatever the case) behind it.</summary>
    public static TrackedSongsMerge Merge(IReadOnlyList<TrackedSong> existing, IReadOnlyList<TrackedSong> imported)
    {
        List<TrackedSong> merged = new(existing);
        HashSet<string> known = new(existing.Select(Key));
        int added = 0;
        int duplicates = 0;

        foreach (TrackedSong song in imported)
        {
            if (known.Add(Key(song)))
            {
                merged.Add(song);
                added++;
            }
            else
            {
                duplicates++;
            }
        }

        return new TrackedSongsMerge(merged, added, duplicates);
    }

    private static string Key(TrackedSong song) =>
        song.Title.Trim().ToLowerInvariant() + "\u0001" + song.Artist.Trim().ToLowerInvariant();

    // ---- JSON ----

    private static string ToJson(IReadOnlyList<TrackedSong> songs) =>
        JsonSerializer.Serialize(
            songs.Select(song => new { title = song.Title, artist = song.Artist }),
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

                songs.Add(new TrackedSong(title.Trim(), artist.Trim()));
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
        builder.Append("Title;Artist\r\n");

        foreach (TrackedSong song in songs)
        {
            builder.Append(CsvField(song.Title)).Append(';').Append(CsvField(song.Artist)).Append("\r\n");
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
        char delimiter = DetectDelimiter(text);

        return SongsFromRows(ReadCsvRows(text, delimiter));
    }

    // The rows of a table (a CSV, a sheet, a Word table): title in the first column, artist in the second; a header row and
    // empty rows are left out, incomplete rows are counted.
    internal static TrackedSongsImport SongsFromRows(IReadOnlyList<IReadOnlyList<string>> rows)
    {
        List<TrackedSong> songs = new();
        int skipped = 0;

        for (int index = 0; index < rows.Count; index++)
        {
            IReadOnlyList<string> row = rows[index];

            // (An empty line is nothing, not a broken entry.)
            if (row.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            if (index == 0 && IsHeader(row))
            {
                continue;
            }

            if (row.Count < 2 || string.IsNullOrWhiteSpace(row[0]) || string.IsNullOrWhiteSpace(row[1]))
            {
                skipped++;
                continue;
            }

            songs.Add(new TrackedSong(row[0].Trim(), row[1].Trim()));
        }

        return new TrackedSongsImport(songs, skipped);
    }

    // The header this class writes, and the same in German (the app is in both languages).
    private static bool IsHeader(IReadOnlyList<string> row) =>
        row.Count >= 2 &&
        new[] { "title", "titel" }.Contains(row[0].Trim(), StringComparer.OrdinalIgnoreCase) &&
        new[] { "artist", "interpret", "künstler" }.Contains(row[1].Trim(), StringComparer.OrdinalIgnoreCase);

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
