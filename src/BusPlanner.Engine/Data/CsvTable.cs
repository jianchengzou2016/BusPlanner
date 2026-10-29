using System.Text;

namespace BusPlanner.Engine.Data;

internal sealed class CsvTable
{
    private CsvTable(string entryName, string[] headers, Dictionary<string, int> columns, List<string[]> rows)
    {
        EntryName = entryName;
        Headers = headers;
        Columns = columns;
        Rows = rows;
    }

    public string EntryName { get; }
    public string[] Headers { get; }
    public Dictionary<string, int> Columns { get; }
    public List<string[]> Rows { get; }

    public static CsvTable Read(
        string entryName,
        TextReader reader,
        string? filterColumn = null,
        IReadOnlySet<string>? allowedValues = null)
    {
        using var records = ParseRecords(reader).GetEnumerator();
        if (!records.MoveNext())
        {
            throw new GtfsLoadException($"GTFS entry '{entryName}' is empty.");
        }

        var headers = records.Current.Select(x => x.Trim()).ToArray();
        if (headers.Length == 0 || headers.Any(string.IsNullOrWhiteSpace))
        {
            throw new GtfsLoadException($"GTFS entry '{entryName}' contains an empty column name.");
        }

        if (headers.Distinct(StringComparer.Ordinal).Count() != headers.Length)
        {
            throw new GtfsLoadException($"GTFS entry '{entryName}' contains duplicate column names.");
        }

        var filterIndex = filterColumn is null ? -1 : Array.IndexOf(headers, filterColumn);
        if (allowedValues is not null && filterIndex < 0)
        {
            throw new GtfsLoadException($"GTFS entry '{entryName}' is missing filter column '{filterColumn}'.");
        }

        var rows = new List<string[]>();
        var line = 2;
        while (records.MoveNext())
        {
            var row = records.Current;
            if (row.Length != headers.Length)
            {
                throw new GtfsLoadException($"GTFS entry '{entryName}', data row {line - 1}: expected {headers.Length} fields but found {row.Length}.");
            }

            if (allowedValues is null || allowedValues.Contains(row[filterIndex].Trim())) rows.Add(row);
            line++;
        }

        var columns = headers.Select((name, index) => (name, index))
            .ToDictionary(x => x.name, x => x.index, StringComparer.Ordinal);
        return new CsvTable(entryName, headers, columns, rows);
    }

    public IEnumerable<CsvRow> EnumerateRows() =>
        Rows.Select((values, index) => new CsvRow(EntryName, index + 2, Columns, values));

    private static IEnumerable<string[]> ParseRecords(TextReader reader)
    {
        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var atFieldStart = true;
        var rowHasContent = false;

        while (true)
        {
            var value = reader.Read();
            if (value < 0)
            {
                if (inQuotes)
                {
                    throw new GtfsLoadException("A GTFS CSV field contains an unterminated quoted value.");
                }

                if (rowHasContent || field.Length > 0 || fields.Count > 0)
                {
                    fields.Add(field.ToString());
                    yield return fields.ToArray();
                }

                yield break;
            }

            var current = (char)value;
            if (inQuotes)
            {
                if (current == '"')
                {
                    if (reader.Peek() == '"')
                    {
                        reader.Read();
                        field.Append('"');
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(current);
                }

                rowHasContent = true;
                continue;
            }

            if (current == '"' && atFieldStart)
            {
                inQuotes = true;
                atFieldStart = false;
                rowHasContent = true;
            }
            else if (current == ',')
            {
                fields.Add(field.ToString());
                field.Clear();
                atFieldStart = true;
                rowHasContent = true;
            }
            else if (current is '\r' or '\n')
            {
                if (current == '\r' && reader.Peek() == '\n')
                {
                    reader.Read();
                }

                fields.Add(field.ToString());
                field.Clear();
                yield return fields.ToArray();
                fields.Clear();
                atFieldStart = true;
                rowHasContent = false;
            }
            else
            {
                if (current == '"')
                {
                    throw new GtfsLoadException("A GTFS CSV field contains a quote outside a quoted value.");
                }

                field.Append(current);
                atFieldStart = false;
                rowHasContent = true;
            }
        }
    }
}

internal sealed class CsvRow(string entry, int line, IReadOnlyDictionary<string, int> columns, string[] values)
{
    public string GetRequired(string column)
    {
        if (!columns.TryGetValue(column, out var index))
        {
            throw Error($"missing required column '{column}'");
        }

        var value = values[index].Trim();
        return string.IsNullOrEmpty(value) ? throw Error($"column '{column}' is empty") : value;
    }

    public string? GetOptional(string column)
    {
        if (!columns.TryGetValue(column, out var index))
        {
            return null;
        }

        var value = values[index].Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    public int GetRequiredInt(string column) =>
        int.TryParse(GetRequired(column), out var value) ? value : throw Error($"column '{column}' is not an integer");

    public int? GetOptionalInt(string column)
    {
        var value = GetOptional(column);
        return value is null ? null : int.TryParse(value, out var parsed) ? parsed : throw Error($"column '{column}' is not an integer");
    }

    public DateOnly GetRequiredDate(string column)
    {
        var value = GetRequired(column);
        return DateOnly.TryParseExact(value, "yyyyMMdd", out var parsed)
            ? parsed
            : throw Error($"column '{column}' must use YYYYMMDD format");
    }

    public TimeSpan? GetOptionalGtfsTime(string column)
    {
        var value = GetOptional(column);
        if (value is null)
        {
            return null;
        }

        var pieces = value.Split(':');
        if (pieces.Length != 3
            || !int.TryParse(pieces[0], out var hours)
            || !int.TryParse(pieces[1], out var minutes)
            || !int.TryParse(pieces[2], out var seconds)
            || hours < 0 || minutes is < 0 or > 59 || seconds is < 0 or > 59)
        {
            throw Error($"column '{column}' must use GTFS HH:MM:SS format");
        }

        try
        {
            return TimeSpan.FromSeconds(checked((long)hours * 3600 + minutes * 60 + seconds));
        }
        catch (OverflowException ex)
        {
            throw Error($"column '{column}' is outside the supported range", ex);
        }
    }

    public bool GetOptionalFlag(string column, bool defaultValue = false)
    {
        var value = GetOptional(column);
        return value switch
        {
            null => defaultValue,
            "0" => false,
            "1" => true,
            _ => throw Error($"column '{column}' must be 0 or 1")
        };
    }

    public GtfsLoadException Error(string message, Exception? inner = null) =>
        new($"GTFS entry '{entry}', row {line}: {message}{(inner is null ? string.Empty : $" ({inner.Message})")}");
}
