using System.Globalization;

namespace Deskbook.Core;

/// <summary>
/// The one CSV dialect this project reads: an exact header line, comma-separated fields holding no commas or quotes,
/// lines starting with # and blank lines skipped. Every bad line is reported with its number, and a file with any
/// bad line yields nothing, so a half-read file is never imported.
/// </summary>
public static class StrictCsv
{
    /// <param name="map">Builds one record from a line's fields; throws FormatException or ArgumentException when they are bad.</param>
    /// <param name="key">The record's identifier, which must be unique in the file.</param>
    public static IReadOnlyList<T> Read<T>(TextReader reader, string header, Func<string[], T> map, Func<T, string> key)
    {
        var fieldCount = header.Split(',').Length;
        var records = new List<T>();
        var errors = new List<string>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var lineNumber = 0;
        var seenHeader = false;

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            lineNumber++;
            if (line.Trim().Length == 0 || line.StartsWith('#')) continue;
            if (!seenHeader)
            {
                if (line.Trim() != header) throw new FormatException($"line {lineNumber}: expected the header '{header}'");
                seenHeader = true;
                continue;
            }
            try
            {
                if (line.Contains('"')) throw new FormatException("quoted fields are not supported");
                var fields = line.Split(',');
                if (fields.Length != fieldCount) throw new FormatException($"expected {fieldCount} fields, found {fields.Length}");
                var record = map(fields);
                if (!keys.Add(key(record))) throw new FormatException($"id '{key(record)}' appears twice in the file");
                records.Add(record);
            }
            catch (Exception e) when (e is FormatException or ArgumentException)
            {
                errors.Add($"line {lineNumber}: {e.Message}");
            }
        }

        if (!seenHeader) throw new FormatException($"the file has no header line '{header}'");
        if (errors.Count > 0) throw new FormatException(string.Join(Environment.NewLine, errors));
        return records;
    }

    public static DateOnly Date(string text) =>
        DateOnly.ParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>A positive or zero amount with a dot as the decimal separator, whatever the machine's language.</summary>
    public static decimal Amount(string text) =>
        decimal.Parse(text.Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
}
