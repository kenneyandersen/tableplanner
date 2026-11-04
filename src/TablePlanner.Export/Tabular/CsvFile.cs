using System.Text;

namespace TablePlanner.Export.Tabular;

/// <summary>
/// Reads and writes CSV. Files are written with ';' and a UTF-8 byte order mark, so Excel opens them
/// correctly with æøå. On reading, the separator (';', ',' or tab) is detected from the header line.
/// </summary>
public static class CsvFile
{
    public const char DefaultSeparator = ';';

    private static readonly char[] Separators = [';', ',', '\t'];

    public static byte[] Write(IEnumerable<IReadOnlyList<string>> rows, char separator = DefaultSeparator)
    {
        var text = new StringBuilder();
        foreach (var row in rows)
        {
            text.AppendJoin(separator, row.Select(cell => Quote(cell, separator)));
            text.Append("\r\n");
        }
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(text.ToString())];
    }

    public static TabularData Read(byte[] content) => Read(Decode(content));

    public static TabularData Read(string text)
    {
        var separator = DetectSeparator(text);
        return TabularData.FromRows(Parse(text, separator));
    }

    /// <summary>UTF-8 (with or without byte order mark). Files that are not valid UTF-8 were most likely saved by Excel as "CSV" in the Windows code page, so they are read as Latin-1.</summary>
    private static string Decode(byte[] content)
    {
        try
        {
            var text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(content);
            return text.Length > 0 && text[0] == '﻿' ? text[1..] : text;
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(content);
        }
    }

    private static string Quote(string cell, char separator)
    {
        var needsQuotes = cell.Contains(separator) || cell.Contains('"') || cell.Contains('\n') || cell.Contains('\r')
            || (cell.Length > 0 && (char.IsWhiteSpace(cell[0]) || char.IsWhiteSpace(cell[^1])));
        return needsQuotes ? $"\"{cell.Replace("\"", "\"\"")}\"" : cell;
    }

    /// <summary>The separator that occurs most often in the first line, outside quotes. Ties go to ';'.</summary>
    private static char DetectSeparator(string text)
    {
        var counts = new int[Separators.Length];
        var inQuotes = false;
        foreach (var c in text)
        {
            if (c == '"')
                inQuotes = !inQuotes;
            else if (!inQuotes && c is '\n' or '\r')
                break;
            else if (!inQuotes && Array.IndexOf(Separators, c) is var i and >= 0)
                counts[i]++;
        }

        var best = 0;
        for (var i = 1; i < counts.Length; i++)
            if (counts[i] > counts[best])
                best = i;
        return Separators[best];
    }

    private static IEnumerable<TabularRow> Parse(string text, char separator)
    {
        var cells = new List<string>();
        var cell = new StringBuilder();
        var inQuotes = false;
        var rowNumber = 1;
        var i = 0;

        while (i < text.Length)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    cell.Append('"');
                    i += 2;
                    continue;
                }
                if (c == '"')
                    inQuotes = false;
                else
                    cell.Append(c);
                i++;
                continue;
            }

            if (c == '"')
                inQuotes = true;
            else if (c == separator)
            {
                cells.Add(cell.ToString());
                cell.Clear();
            }
            else if (c is '\r' or '\n')
            {
                cells.Add(cell.ToString());
                cell.Clear();
                yield return new TabularRow(rowNumber++, cells.ToArray());
                cells.Clear();
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                    i++;
            }
            else
                cell.Append(c);
            i++;
        }

        if (cell.Length > 0 || cells.Count > 0)
        {
            cells.Add(cell.ToString());
            yield return new TabularRow(rowNumber, cells.ToArray());
        }
    }
}
