using System.Globalization;
using TablePlanner.Core.Model;
using TablePlanner.Export.Tabular;

namespace TablePlanner.Export.Groups;

/// <summary>What a column of a guest list holds, as chosen in the import wizard.</summary>
public enum ColumnRole
{
    Ignore,

    /// <summary>The group a row belongs to (the bottom level when there are level columns).</summary>
    GroupName,

    /// <summary>The name of the row's parent group, as in the app's own export (one row per group).</summary>
    ParentGroup,

    /// <summary>Top level, e.g. department.</summary>
    Level1,

    /// <summary>Second level, e.g. team.</summary>
    Level2,

    /// <summary>Third level, e.g. subteam.</summary>
    Level3,

    /// <summary>Number of people: the group's size on a group row, the party size on a person row ("2" = them and a guest).</summary>
    People,

    /// <summary>A person's full name: the row is a person.</summary>
    PersonName,

    FirstName,
    LastName,

    /// <summary>Several names in one cell, separated by semicolons (as in the app's own export).</summary>
    Names,

    /// <summary>A preferred group's name; several such columns are ranked left to right.</summary>
    Preference,

    ContactPerson,
    DietaryInfo,
    Notes,
    Colour,
}

/// <summary>A sheet of a guest list file: its name and all rows as they are.</summary>
public sealed record GuestListSheet(string Name, IReadOnlyList<TabularRow> Rows);

/// <summary>
/// The import wizard's model: reads a guest list (CSV or Excel, any sheet, header on any row), guesses what each column
/// holds, and turns the rows into groups (<see cref="Build"/>) for <see cref="GroupImport"/>. Rows can be groups (with a
/// number of people), people (with their department, team, …) or a mix.
/// </summary>
public static class ImportMapping
{
    /// <summary>Reads every sheet of a .xlsx, or the one "sheet" of a .csv/.txt. Throws <see cref="TabularFormatException"/> with a message for the user.</summary>
    public static IReadOnlyList<GuestListSheet> ReadSheets(string fileName, byte[] content)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".xlsx" or ".xlsm" => XlsxFile.ReadSheets(content).Select(s => new GuestListSheet(s.Name, s.Rows)).ToList(),
            ".xls" => throw new TabularFormatException("Old Excel files (.xls) are not supported. Save the file as .xlsx or CSV and try again."),
            _ => [new GuestListSheet(Path.GetFileNameWithoutExtension(fileName), CsvFile.ReadRows(content))],
        };
    }

    /// <summary>The data below a header row (by the row number shown in Excel): non-blank rows after it.</summary>
    public static TabularData WithHeaderAt(IReadOnlyList<TabularRow> rows, int headerRowNumber)
    {
        var header = rows.FirstOrDefault(r => r.RowNumber == headerRowNumber);
        return header is null ? TabularData.Empty
            : new TabularData(header.Cells, rows.Where(r => r.RowNumber > headerRowNumber && !r.IsBlank).ToList());
    }

    /// <summary>
    /// The likely header row: among the first rows, the one with the most cells that look like known column names;
    /// failing that, the first row with the most filled cells (titles above a table usually fill one cell).
    /// </summary>
    public static int GuessHeaderRow(IReadOnlyList<TabularRow> rows)
    {
        var candidates = rows.Where(r => !r.IsBlank).Take(15).ToList();
        if (candidates.Count == 0)
            return 1;
        var byKnown = candidates.Select(r => (r, known: r.Cells.Count(c => RoleOf(c, hasGroupColumn: false, levels: 0, hasPeople: false) != ColumnRole.Ignore)))
            .OrderByDescending(x => x.known).ThenBy(x => x.r.RowNumber).First();
        if (byKnown.known >= 2)
            return byKnown.r.RowNumber;
        return candidates.OrderByDescending(r => r.Cells.Count(c => c.Trim().Length > 0)).ThenBy(r => r.RowNumber).First().RowNumber;
    }

    /// <summary>A guess at each column's role from its header (English and Danish names).</summary>
    public static List<ColumnRole> Guess(IReadOnlyList<string> header)
    {
        var normalized = header.Select(Normalize).ToList();
        var hasGroupColumn = normalized.Any(h => GroupWords.Contains(h));
        var levels = normalized.Count(h => Level1Words.Contains(h) || Level2Words.Contains(h) || Level3Words.Contains(h));
        var hasPeople = normalized.Any(h => PeopleWords.Contains(h));
        var roles = header.Select(h => RoleOf(h, hasGroupColumn, levels, hasPeople)).ToList();
        // Only one column per role, except preferences.
        var seen = new HashSet<ColumnRole>();
        for (var i = 0; i < roles.Count; i++)
            if (roles[i] is not (ColumnRole.Ignore or ColumnRole.Preference) && !seen.Add(roles[i]))
                roles[i] = ColumnRole.Ignore;
        return roles;
    }

    /// <summary>
    /// The role a header suggests. "Name" is a person's name next to a group column or two level columns (or one without
    /// a number column), otherwise the group's ("Department, Name, People" is a list of teams).
    /// </summary>
    private static ColumnRole RoleOf(string header, bool hasGroupColumn, int levels, bool hasPeople)
    {
        var h = Normalize(header);
        if (h.Length == 0)
            return ColumnRole.Ignore;
        if (h is "name" or "navn")
            return hasGroupColumn || levels >= 2 || (levels == 1 && !hasPeople) ? ColumnRole.PersonName : ColumnRole.GroupName;
        if (GroupWords.Contains(h)) return ColumnRole.GroupName;
        if (h is "parentgroup" or "parent" or "overgruppe" or "hørertil" or "partof" or "belongsto" or "ingroup") return ColumnRole.ParentGroup;
        if (Level1Words.Contains(h)) return ColumnRole.Level1;
        if (Level2Words.Contains(h)) return ColumnRole.Level2;
        if (Level3Words.Contains(h)) return ColumnRole.Level3;
        if (PeopleWords.Contains(h)) return ColumnRole.People;
        if (h is "person" or "fullname" or "fuldtnavn" or "fuldenavn" or "guest" or "guestname" or "gæst" or "gæstenavn" or "deltager" or "deltagernavn" or "participant" or "attendee" or "medarbejder" or "employee")
            return ColumnRole.PersonName;
        if (h is "firstname" or "fornavn" or "givenname" or "first") return ColumnRole.FirstName;
        if (h is "lastname" or "efternavn" or "surname" or "familyname" or "last") return ColumnRole.LastName;
        if (h is "names" or "members" or "navne" or "deltagere" or "guestnames" or "peoplenames") return ColumnRole.Names;
        if (h is "contactperson" or "contact" or "kontakt" or "kontaktperson") return ColumnRole.ContactPerson;
        if (h is "dietaryinfo" or "dietary" or "diet" or "allergies" or "allergy" or "kost" or "diæt" or "allergier" or "allergi" or "kosthensyn" or "specialkost")
            return ColumnRole.DietaryInfo;
        if (h is "notes" or "note" or "comments" or "comment" or "bemærkninger" or "bemærkning" or "noter" or "kommentar" or "kommentarer")
            return ColumnRole.Notes;
        if (h is "colour" or "color" or "farve") return ColumnRole.Colour;
        if (new[] { "preference", "pref", "sitwith", "ønske", "sidderhos", "siddermed", "præference" }.Any(p => h.StartsWith(p, StringComparison.Ordinal)))
            return ColumnRole.Preference;
        return ColumnRole.Ignore;
    }

    private static readonly HashSet<string> GroupWords = ["group", "groupname", "gruppe", "gruppenavn", "party", "selskab", "household", "husstand", "bord"];
    private static readonly HashSet<string> Level1Words = ["department", "dept", "afdeling", "division", "company", "firma", "virksomhed", "level1", "niveau1", "hovedgruppe", "organisation", "organization"];
    private static readonly HashSet<string> Level2Words = ["team", "hold", "level2", "niveau2", "undergruppe", "subgroup", "unit", "enhed"];
    private static readonly HashSet<string> Level3Words = ["subteam", "underteam", "level3", "niveau3", "subunit"];
    private static readonly HashSet<string> PeopleWords = ["people", "peoplecount", "count", "size", "persons", "guests", "numberofpeople", "antal", "antalpers", "antalpersoner", "personer", "pax", "kuverter", "gæster", "number", "headcount"];

    private static string Normalize(string header) =>
        new string(header.Where(c => char.IsLetterOrDigit(c)).ToArray()).ToLowerInvariant();

    /// <summary>Whether the roles describe a tree of levels or people (handled by <see cref="Build"/>'s own rules) rather than one group per row.</summary>
    private static bool IsTreeOrPeople(IReadOnlyList<ColumnRole> roles) =>
        roles.Any(r => r is ColumnRole.Level1 or ColumnRole.Level2 or ColumnRole.Level3 or ColumnRole.PersonName or ColumnRole.FirstName or ColumnRole.LastName);

    /// <summary>
    /// Turns the rows into groups for <see cref="GroupImport"/>.
    /// <list type="bullet">
    /// <item>One row per group (a group name, maybe a parent group and a number): read like the app's own export.</item>
    /// <item>With level columns and/or person names: each row's group is its path of levels, ending with the group name if
    /// given (e.g. Sales › North). A row with a person's name adds that person (and, with a number, their party) to the
    /// group; a row without one is the group itself, with its number of people. Rows may mix both. Someone only in a
    /// department, not a team, goes to the department's "– other" group.</item>
    /// </list>
    /// </summary>
    public static GroupFile Build(TabularData data, IReadOnlyList<ColumnRole> roles)
    {
        if (!IsTreeOrPeople(roles))
            return GroupFile.Parse(AsCanonical(data, roles));
        return new TreeBuilder(data, roles).Build();
    }

    /// <summary>The data with the app's own column names, so the regular reader (<see cref="GroupFile.Parse"/>) handles it.</summary>
    private static TabularData AsCanonical(TabularData data, IReadOnlyList<ColumnRole> roles)
    {
        var preference = 0;
        var header = roles.Select(role => role switch
        {
            ColumnRole.GroupName => GroupColumns.Header(GroupColumn.Name),
            ColumnRole.ParentGroup => GroupColumns.Header(GroupColumn.Parent),
            ColumnRole.People => GroupColumns.Header(GroupColumn.People),
            ColumnRole.Names => GroupColumns.Header(GroupColumn.Names),
            ColumnRole.ContactPerson => GroupColumns.Header(GroupColumn.ContactPerson),
            ColumnRole.DietaryInfo => GroupColumns.Header(GroupColumn.DietaryInfo),
            ColumnRole.Notes => GroupColumns.Header(GroupColumn.Notes),
            ColumnRole.Colour => GroupColumns.Header(GroupColumn.Colour),
            ColumnRole.Preference when ++preference <= Group.MaxPreferences => $"Preference {preference}",
            _ => "",
        }).ToList();
        return data with { Header = header };
    }

    /// <summary>Builds groups from rows with levels and/or people (see <see cref="Build"/>).</summary>
    private sealed class TreeBuilder(TabularData data, IReadOnlyList<ColumnRole> roles)
    {
        private sealed class Node(string key, string name, string? parentKey, int row)
        {
            public string Key { get; } = key;
            public string Name { get; set; } = name;
            public string? ParentKey { get; } = parentKey;
            public int Row { get; } = row;
            public int? Count;               // from group rows
            public int PartyPeople;          // from person rows (each person and their guests)
            public readonly List<string> Names = [];
            public string? Contact, Dietary, Notes, Colour;
            public readonly List<string> Preferences = [];
            public bool HasChildren;
        }

        private readonly List<Node> _nodes = [];
        private readonly Dictionary<string, Node> _byKey = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<ImportMessage> _errors = [];
        private readonly List<ImportMessage> _warnings = [];

        private string Text(TabularRow row, ColumnRole role) =>
            string.Join(" ", Enumerable.Range(0, roles.Count).Where(i => roles[i] == role).Select(i => row.Cell(i).Trim()).Where(t => t.Length > 0));

        public GroupFile Build()
        {
            foreach (var row in data.Rows)
                ReadRow(row);

            // A group that has subgroups and also people of its own: those people go to "<name> – other".
            foreach (var node in _nodes.Where(n => n.HasChildren && (n.Count > 0 || n.PartyPeople > 0 || n.Names.Count > 0)).ToList())
            {
                var other = GetOrAdd(node.Key + "\u0001other", $"{node.Name} – other", node.Key, node.Row);
                other.Count = node.Count;
                other.PartyPeople = node.PartyPeople;
                other.Names.AddRange(node.Names);
                other.Dietary = node.Dietary;
                (node.Count, node.PartyPeople) = (null, 0);
                node.Names.Clear();
                node.Dietary = null;
            }

            // Group names must be unique: a name used under two parents gets its parent's name added, e.g. "North (Sales)".
            foreach (var clash in _nodes.GroupBy(n => n.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
                foreach (var node in clash.Skip(1))
                    node.Name = node.ParentKey is { } p && _byKey.TryGetValue(p, out var parent) ? $"{node.Name} ({parent.Name})" : $"{node.Name} ({node.Row})";

            var groups = new List<ImportedGroup>();
            foreach (var node in _nodes)
            {
                var people = node.HasChildren ? 0 : Math.Max(node.Count ?? 0, 0) + node.PartyPeople;
                people = Math.Max(people, node.Names.Count);
                if (!node.HasChildren && people == 0)
                {
                    _errors.Add(new(node.Row, $"'{node.Name}' has no number of people and no names, so it is left out."));
                    continue;
                }
                var parent = node.ParentKey is { } p ? _byKey[p].Name : null;
                groups.Add(new ImportedGroup(node.Row, node.Name, people, node.Contact, node.Dietary, node.Colour, node.Notes, node.Preferences,
                    parent, node.Names.Count > 0 ? node.Names : null));
            }

            var columns = new HashSet<GroupColumn> { GroupColumn.Name, GroupColumn.People };
            if (groups.Any(g => g.Parent is not null)) columns.Add(GroupColumn.Parent);
            if (groups.Any(g => g.Names is not null)) columns.Add(GroupColumn.Names);
            if (roles.Contains(ColumnRole.ContactPerson)) columns.Add(GroupColumn.ContactPerson);
            if (roles.Contains(ColumnRole.DietaryInfo)) columns.Add(GroupColumn.DietaryInfo);
            if (roles.Contains(ColumnRole.Notes)) columns.Add(GroupColumn.Notes);
            if (roles.Contains(ColumnRole.Colour)) columns.Add(GroupColumn.Colour);
            if (roles.Contains(ColumnRole.Preference)) columns.Add(GroupColumn.Preference1);
            if (groups.Count == 0 && _errors.Count == 0)
                _errors.Add(new(null, "No groups were found. Check which columns hold the department, team, group or name."));
            var file = GroupFile.Create(groups, columns, _errors, _warnings);
            // The level columns' headings name the levels (only when they are in order, as levels are counted from the top).
            file.LevelNames = new[] { ColumnRole.Level1, ColumnRole.Level2, ColumnRole.Level3 }
                .Select(role => Enumerable.Range(0, roles.Count).FirstOrDefault(i => roles[i] == role, -1))
                .TakeWhile(i => i >= 0)
                .Select(i => i < data.Header.Count ? data.Header[i].Trim() : "")
                .ToList();
            return file;
        }

        private void ReadRow(TabularRow row)
        {
            if (row.IsBlank)
                return;
            var path = new[] { ColumnRole.Level1, ColumnRole.Level2, ColumnRole.Level3, ColumnRole.GroupName }
                .Select(r => Text(row, r)).Where(t => t.Length > 0).Take(GroupTree.MaxLevels).ToList();
            var person = Text(row, ColumnRole.PersonName) is { Length: > 0 } full ? full
                : string.Join(" ", new[] { Text(row, ColumnRole.FirstName), Text(row, ColumnRole.LastName) }.Where(t => t.Length > 0));
            if (path.Count == 0)
            {
                if (person.Length > 0)
                    _errors.Add(new(row.RowNumber, $"'{person}' has no group, department or team, so is left out."));
                else
                    _warnings.Add(new(row.RowNumber, "The row has no group, department, team or name, so it is skipped."));
                return;
            }

            // The path of groups, from the top level down; the row belongs to the last.
            Node? node = null;
            for (var i = 0; i < path.Count; i++)
            {
                var key = string.Join("\u0001", path.Take(i + 1));
                if (node is not null)
                    node.HasChildren = true;
                node = GetOrAdd(key, path[i], node?.Key, row.RowNumber);
            }

            var countText = Text(row, ColumnRole.People);
            int? count = null;
            if (countText.Length > 0)
            {
                if (ParseCount(countText) is { } c)
                    count = c;
                else
                    _warnings.Add(new(row.RowNumber, $"'{countText}' is not a number of people, so it is ignored."));
            }

            if (person.Length > 0)
            {
                // A person, with their party: "2" is them and one guest.
                node!.Names.Add(person);
                node.PartyPeople += Math.Max(1, count ?? 1);
                var diet = Text(row, ColumnRole.DietaryInfo);
                if (diet.Length > 0)
                    node.Dietary = node.Dietary is null ? $"{person}: {diet}" : $"{node.Dietary}; {person}: {diet}";
                var note = Text(row, ColumnRole.Notes);
                if (note.Length > 0)
                    node.Notes = node.Notes is null ? $"{person}: {note}" : $"{node.Notes}; {person}: {note}";
            }
            else
            {
                node!.Count = (node.Count ?? 0) + (count ?? 0);
                node.Dietary ??= Text(row, ColumnRole.DietaryInfo) is { Length: > 0 } d ? d : null;
                node.Notes ??= Text(row, ColumnRole.Notes) is { Length: > 0 } notes ? notes : null;
            }
            foreach (var name in Text(row, ColumnRole.Names).Split(';').Select(n => n.Trim()).Where(n => n.Length > 0))
                node.Names.Add(name);
            node.Contact ??= Text(row, ColumnRole.ContactPerson) is { Length: > 0 } contact ? contact : null;
            node.Colour ??= Text(row, ColumnRole.Colour) is { Length: > 0 } colour ? colour : null;
            if (node.Preferences.Count == 0)
                node.Preferences.AddRange(Enumerable.Range(0, roles.Count).Where(i => roles[i] == ColumnRole.Preference)
                    .Select(i => row.Cell(i).Trim()).Where(t => t.Length > 0).Take(Group.MaxPreferences));
        }

        private Node GetOrAdd(string key, string name, string? parentKey, int row)
        {
            if (_byKey.TryGetValue(key, out var node))
                return node;
            node = new Node(key, name, parentKey, row);
            _byKey[key] = node;
            _nodes.Add(node);
            return node;
        }

        private static int? ParseCount(string text)
        {
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
                return n >= 0 ? n : null;
            return double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && d >= 0 && d == Math.Floor(d) && d < int.MaxValue
                ? (int)d : null;
        }
    }
}
