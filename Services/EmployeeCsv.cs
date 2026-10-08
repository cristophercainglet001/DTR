using System.Text;

namespace DepEdDTRSystem.Services;

public sealed record EmployeeImportRow(
    int RowNumber,
    string BadgeNumber,
    string FirstName,
    string? MiddleName,
    string LastName,
    string? Suffix,
    string Position,
    string? Warning);

public sealed record EmployeeImportIssue(int RowNumber, string Value, string Message);

public sealed class EmployeeCsvResult
{
    public List<EmployeeImportRow> Rows { get; } = [];

    public List<EmployeeImportIssue> Errors { get; } = [];

    public string? FatalError { get; set; }
}

public static class EmployeeCsv
{
    public const int MaxRows = 2000;
    public const string TemplateHeader = "Badgenumber,Name,TITLE";

    private static readonly HashSet<string> Suffixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Jr", "Sr", "II", "III", "IV",
    };

    private static readonly HashSet<string> SurnameParticles = new(StringComparer.OrdinalIgnoreCase)
    {
        "de", "del", "dela", "delas", "delos", "di", "la", "las", "los", "san", "santa", "van", "von",
    };

    public static byte[] BuildTemplate() =>
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetPreamble()
            .Concat(Encoding.UTF8.GetBytes(TemplateHeader + "\r\n"))
            .ToArray();

    public static string Decode(byte[] bytes)
    {
        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes).TrimStart('\uFEFF');
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(bytes);
        }
    }

    public static EmployeeCsvResult Parse(string text)
    {
        var result = new EmployeeCsvResult();
        var records = ReadRecords(text);
        if (records.Count == 0)
        {
            result.FatalError = "The file is empty.";
            return result;
        }

        var header = records[0].Fields.Select(NormalizeHeader).ToList();
        var badgeColumn = header.FindIndex(h => h is "badgenumber" or "badgeno" or "badge" or "employeeid" or "idnumber");
        var nameColumn = header.FindIndex(h => h is "name" or "fullname" or "employeename");
        var titleColumn = header.FindIndex(h => h is "title" or "position" or "jobtitle");
        if (badgeColumn < 0 || nameColumn < 0 || titleColumn < 0)
        {
            result.FatalError = $"The first row must contain the columns: {TemplateHeader}.";
            return result;
        }

        if (records.Count - 1 > MaxRows)
        {
            result.FatalError = $"The file has more than {MaxRows} rows. Split it into smaller files.";
            return result;
        }

        var seenBadges = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (rowNumber, fields) in records.Skip(1))
        {
            string Cell(int column) => column < fields.Count ? CleanCell(fields[column]) : string.Empty;

            var badge = Cell(badgeColumn);
            var name = Cell(nameColumn);
            var title = Cell(titleColumn);
            var label = string.IsNullOrEmpty(badge) ? name : badge;

            if (badge.Length == 0)
            {
                result.Errors.Add(new(rowNumber, label, "Badgenumber is missing."));
                continue;
            }

            if (badge.Length > 50)
            {
                result.Errors.Add(new(rowNumber, badge, "Badgenumber is longer than 50 characters."));
                continue;
            }

            if (seenBadges.TryGetValue(badge, out var firstRow))
            {
                result.Errors.Add(new(rowNumber, badge, $"Duplicate Badgenumber (already on row {firstRow})."));
                continue;
            }

            seenBadges[badge] = rowNumber;

            if (title.Length == 0)
            {
                result.Errors.Add(new(rowNumber, badge, "TITLE is missing."));
                continue;
            }

            if (title.Length > 150)
            {
                result.Errors.Add(new(rowNumber, badge, "TITLE is longer than 150 characters."));
                continue;
            }

            if (!TryParseName(name, out var parsed, out var nameError))
            {
                result.Errors.Add(new(rowNumber, badge, nameError!));
                continue;
            }

            if (parsed.First.Length > 100 || parsed.Last.Length > 100 || (parsed.Middle?.Length ?? 0) > 100)
            {
                result.Errors.Add(new(rowNumber, badge, "A name part is longer than 100 characters."));
                continue;
            }

            result.Rows.Add(new EmployeeImportRow(
                rowNumber, badge, parsed.First, parsed.Middle, parsed.Last, parsed.Suffix, title, parsed.Warning));
        }

        return result;
    }

    // "Juan Drew L. Delo Santos" -> first "Juan Drew", middle "L.", last "Delo Santos".
    // "SANTOS, JUAN DREW L." is also understood.
    public static bool TryParseName(string name, out ParsedName parsed, out string? error)
    {
        parsed = default;
        error = null;
        var normalized = string.Join(' ', name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length == 0)
        {
            error = "Name is missing.";
            return false;
        }

        var commaIndex = normalized.IndexOf(',');
        if (commaIndex >= 0)
        {
            var lastTokens = Tokenize(normalized[..commaIndex]);
            var restTokens = Tokenize(normalized[(commaIndex + 1)..]);
            var suffix = TakeSuffix(lastTokens) ?? TakeSuffix(restTokens);
            string? middle = null;
            if (restTokens.Count >= 2 && IsInitial(restTokens[^1]))
            {
                middle = restTokens[^1];
                restTokens.RemoveAt(restTokens.Count - 1);
            }

            if (lastTokens.Count == 0 || restTokens.Count == 0)
            {
                error = "Name must include a first and last name.";
                return false;
            }

            parsed = new ParsedName(string.Join(' ', restTokens), middle, string.Join(' ', lastTokens), suffix, null);
            return true;
        }

        var tokens = Tokenize(normalized);
        var trailingSuffix = tokens.Count >= 3 ? TakeSuffix(tokens) : null;
        if (tokens.Count < 2)
        {
            error = "Name must include a first and last name.";
            return false;
        }

        for (var i = 1; i <= tokens.Count - 2; i++)
        {
            if (IsInitial(tokens[i]))
            {
                parsed = new ParsedName(
                    string.Join(' ', tokens.Take(i)),
                    tokens[i],
                    string.Join(' ', tokens.Skip(i + 1)),
                    trailingSuffix,
                    null);
                return true;
            }
        }

        if (tokens.Count == 2)
        {
            parsed = new ParsedName(tokens[0], null, tokens[1], trailingSuffix, null);
            return true;
        }

        var lastStart = tokens.Count - 1;
        while (lastStart > 1 && SurnameParticles.Contains(tokens[lastStart - 1].TrimEnd('.')))
        {
            lastStart--;
        }

        var first = string.Join(' ', tokens.Take(lastStart));
        var last = string.Join(' ', tokens.Skip(lastStart));
        parsed = new ParsedName(
            first,
            null,
            last,
            trailingSuffix,
            $"No middle initial found: read \"{first}\" as the first name and \"{last}\" as the last name.");
        return true;
    }

    public readonly record struct ParsedName(string First, string? Middle, string Last, string? Suffix, string? Warning);

    private static List<string> Tokenize(string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).ToList();

    private static bool IsInitial(string token)
    {
        var trimmed = token.TrimEnd('.');
        return trimmed.Length == 1 && char.IsLetter(trimmed[0]);
    }

    private static string? TakeSuffix(List<string> tokens)
    {
        if (tokens.Count > 1 && Suffixes.Contains(tokens[^1].TrimEnd('.', ',')))
        {
            var suffix = tokens[^1].TrimEnd(',');
            tokens.RemoveAt(tokens.Count - 1);
            return suffix;
        }

        return null;
    }

    private static string NormalizeHeader(string header) =>
        new string(header.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    private static string CleanCell(string value) =>
        string.Join(' ', new string(value.Where(c => !char.IsControl(c) || c is '\t' or ' ').ToArray())
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static List<(int RowNumber, List<string> Fields)> ReadRecords(string text)
    {
        var delimiter = DetectDelimiter(text);
        var records = new List<(int, List<string>)>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var rowNumber = 1;

        void EndRecord()
        {
            fields.Add(field.ToString());
            field.Clear();
            if (fields.Any(f => f.Trim().Length > 0))
            {
                records.Add((rowNumber, fields));
            }

            fields = [];
            rowNumber++;
        }

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(c);
                }

                continue;
            }

            if (c == '"' && field.Length == 0)
            {
                inQuotes = true;
            }
            else if (c == delimiter)
            {
                fields.Add(field.ToString());
                field.Clear();
            }
            else if (c is '\r' or '\n')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                EndRecord();
            }
            else
            {
                field.Append(c);
            }
        }

        if (field.Length > 0 || fields.Count > 0)
        {
            EndRecord();
        }

        return records;
    }

    private static char DetectDelimiter(string text)
    {
        int comma = 0, semicolon = 0, tab = 0;
        var inQuotes = false;
        foreach (var c in text)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (!inQuotes)
            {
                if (c is '\r' or '\n')
                {
                    break;
                }

                if (c == ',') comma++;
                else if (c == ';') semicolon++;
                else if (c == '\t') tab++;
            }
        }

        if (semicolon > comma && semicolon >= tab) return ';';
        if (tab > comma && tab > semicolon) return '\t';
        return ',';
    }
}
