using System.Globalization;
using System.Text.RegularExpressions;
using TablePlanner.Export.Tabular;

namespace TablePlanner.Export.Groups;

/// <summary>A group as read from one row of a file. Preferences are group names, most wanted first.</summary>
public sealed record ImportedGroup(
    int RowNumber,
    string Name,
    int PeopleCount,
    string? ContactPerson,
    string? DietaryInfo,
    string? Colour,
    string? Notes,
    IReadOnlyList<string> Preferences,
    string? Parent = null,
    IReadOnlyList<string>? Names = null);

/// <summary>A message about the file. <see cref="RowNumber"/> is null when it concerns the whole file.</summary>
public sealed record ImportMessage(int? RowNumber, string Text)
{
    public override string ToString() => RowNumber is { } row ? $"Row {row}: {Text}" : Text;
}

/// <summary>
/// The groups read from a CSV or Excel file, before they are applied to a project. Rows with errors are
/// not in <see cref="Groups"/>; their names (if any) are in <see cref="SkippedNames"/>.
/// </summary>
public sealed partial class GroupFile
{
    private GroupFile(IReadOnlyList<ImportedGroup> groups, IReadOnlySet<GroupColumn> columns,
        IReadOnlyList<ImportMessage> errors, IReadOnlyList<ImportMessage> warnings, IReadOnlyList<string> skippedNames)
    {
        Groups = groups;
        Columns = columns;
        Errors = errors;
        Warnings = warnings;
        SkippedNames = skippedNames;
    }

    public IReadOnlyList<ImportedGroup> Groups { get; }

    /// <summary>The columns present in the file. Only these are changed when existing groups are updated.</summary>
    public IReadOnlySet<GroupColumn> Columns { get; }

    public IReadOnlyList<ImportMessage> Errors { get; }

    public IReadOnlyList<ImportMessage> Warnings { get; }

    /// <summary>Names on rows that have errors.</summary>
    public IReadOnlyList<string> SkippedNames { get; }

    public bool HasPreferences => GroupColumns.Preferences.Any(Columns.Contains);

    /// <summary>Reads a .csv/.txt or .xlsx file, chosen by the file name. Never throws; problems are reported in <see cref="Errors"/>.</summary>
    public static GroupFile Read(string fileName, byte[] content)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        try
        {
            return extension switch
            {
                ".xlsx" or ".xlsm" => Parse(XlsxFile.Read(content)),
                ".xls" => Failed("Old Excel files (.xls) are not supported. Save the file as .xlsx or CSV and try again."),
                _ => Parse(CsvFile.Read(content)),
            };
        }
        catch (TabularFormatException ex)
        {
            return Failed(ex.Message);
        }
    }

    public static GroupFile Parse(TabularData data)
    {
        var errors = new List<ImportMessage>();
        var warnings = new List<ImportMessage>();

        if (data.Header.Count == 0)
            return Failed("The file is empty.");

        var columnIndex = new Dictionary<GroupColumn, int>();
        var unknown = new List<string>();
        for (var i = 0; i < data.Header.Count; i++)
        {
            var header = data.Header[i].Trim();
            if (header.Length == 0)
                continue;
            if (GroupColumns.Match(header) is not { } column)
                unknown.Add(header);
            else if (!columnIndex.TryAdd(column, i))
                warnings.Add(new(null, $"The column '{header}' appears more than once. Only the first one is used."));
        }
        if (unknown.Count > 0)
            warnings.Add(new(null, $"These columns are not used: {string.Join(", ", unknown.Select(h => $"'{h}'"))}."));

        var missing = new[] { GroupColumn.Name, GroupColumn.People }.Where(c => !columnIndex.ContainsKey(c)).ToList();
        if (missing.Count > 0)
            return Failed($"The file needs {string.Join(" and ", missing.Select(c => $"a '{GroupColumns.Header(c)}' column"))} in the first row.");

        string Text(TabularRow row, GroupColumn column) => columnIndex.TryGetValue(column, out var i) ? row.Cell(i).Trim() : "";
        string? Optional(TabularRow row, GroupColumn column) => Text(row, column) is { Length: > 0 } text ? text : null;

        var groups = new List<ImportedGroup>();
        var skipped = new List<string>();
        var rowsByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // Groups named as another row's parent group have their people in their subgroups, so they need no number.
        var parentNames = data.Rows.Where(r => !r.IsBlank).Select(r => Text(r, GroupColumn.Parent)).Where(p => p.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var row in data.Rows)
        {
            if (row.IsBlank)
                continue;

            var name = Text(row, GroupColumn.Name);
            var rowErrors = new List<string>();
            if (name.Length == 0)
                rowErrors.Add("the name is missing.");
            else if (rowsByName.TryGetValue(name, out var firstRow))
                rowErrors.Add($"'{name}' is already on row {firstRow}.");

            var peopleText = Text(row, GroupColumn.People);
            var isParent = parentNames.Contains(name);
            var names = Text(row, GroupColumn.Names).Split(NameSeparators).Select(n => n.Trim()).Where(n => n.Length > 0).ToList();
            var people = isParent && (peopleText.Length == 0 || peopleText == "0") ? 0
                : peopleText.Length == 0 && names.Count > 0 ? names.Count
                : ParsePeople(peopleText);
            if (people is { } counted && !isParent && names.Count > counted)
                people = names.Count; // everyone named needs a seat
            if (people is null)
                rowErrors.Add(peopleText.Length == 0
                    ? "the number of people is missing."
                    : $"the number of people must be a whole number of at least 1, not '{peopleText}'.");

            if (rowErrors.Count > 0)
            {
                errors.AddRange(rowErrors.Select(e => new ImportMessage(row.RowNumber, Capitalize(e))));
                if (name.Length > 0)
                    skipped.Add(name);
                continue;
            }

            var colourText = Text(row, GroupColumn.Colour);
            var colour = ParseColour(colourText);
            if (colourText.Length > 0 && colour is null)
                warnings.Add(new(row.RowNumber, $"'{colourText}' is not a colour like #3366CC, so an automatic colour is used."));

            var preferences = GroupColumns.Preferences
                .Select(column => Text(row, column))
                .Where(p => p.Length > 0)
                .ToList();

            rowsByName[name] = row.RowNumber;
            groups.Add(new ImportedGroup(row.RowNumber, name, people!.Value,
                Optional(row, GroupColumn.ContactPerson), Optional(row, GroupColumn.DietaryInfo), colour,
                Optional(row, GroupColumn.Notes), preferences, Optional(row, GroupColumn.Parent), columnIndex.ContainsKey(GroupColumn.Names) ? names : null));
        }

        if (groups.Count == 0 && errors.Count == 0)
            errors.Add(new(null, "The file has no groups below the header row."));

        return new GroupFile(groups, columnIndex.Keys.ToHashSet(), errors, warnings, skipped);
    }

    /// <summary>Names in the Names column are separated by semicolons or line breaks.</summary>
    private static readonly char[] NameSeparators = [';', '\n', '\r'];

    /// <summary>Groups read another way (the import wizard's <see cref="ImportMapping"/>), with the columns they fill.</summary>
    internal static GroupFile Create(IReadOnlyList<ImportedGroup> groups, IReadOnlySet<GroupColumn> columns,
        IReadOnlyList<ImportMessage> errors, IReadOnlyList<ImportMessage> warnings) =>
        new(groups, columns, errors, warnings, []);

    private static GroupFile Failed(string error) =>
        new([], new HashSet<GroupColumn>(), [new ImportMessage(null, error)], [], []);

    /// <summary>A whole number of at least 1. Accepts "12" and also "12.0" or "12,0", as spreadsheets sometimes write them.</summary>
    private static int? ParsePeople(string text)
    {
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count))
            return count >= 1 ? count : null;
        if (double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            && number >= 1 && number <= int.MaxValue && number == Math.Floor(number))
            return (int)number;
        return null;
    }

    /// <summary>"#RRGGBB", "RRGGBB", "#RGB" or "RGB", returned as lower-case "#rrggbb" (the form the colour picker uses).</summary>
    private static string? ParseColour(string text)
    {
        var hex = text.TrimStart('#');
        if (!HexColour().IsMatch(hex))
            return null;
        if (hex.Length == 3)
            hex = string.Concat(hex.Select(c => $"{c}{c}"));
        return "#" + hex.ToLowerInvariant();
    }

    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    [GeneratedRegex("^([0-9a-fA-F]{3}|[0-9a-fA-F]{6})$")]
    private static partial Regex HexColour();
}
