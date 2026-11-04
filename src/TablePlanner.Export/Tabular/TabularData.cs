namespace TablePlanner.Export.Tabular;

/// <summary>A row read from a CSV or Excel file. <see cref="RowNumber"/> is the row number the user sees in Excel (the header is row 1).</summary>
public sealed record TabularRow(int RowNumber, IReadOnlyList<string> Cells)
{
    public string Cell(int column) => column >= 0 && column < Cells.Count ? Cells[column] : "";

    public bool IsBlank => Cells.All(string.IsNullOrWhiteSpace);
}

/// <summary>A sheet of text cells: a header row and data rows. Blank rows are left out.</summary>
public sealed record TabularData(IReadOnlyList<string> Header, IReadOnlyList<TabularRow> Rows)
{
    public static TabularData Empty { get; } = new([], []);

    /// <summary>Uses the first non-blank row as the header.</summary>
    public static TabularData FromRows(IEnumerable<TabularRow> rows)
    {
        var nonBlank = rows.Where(r => !r.IsBlank).ToList();
        return nonBlank.Count == 0 ? Empty : new TabularData(nonBlank[0].Cells, nonBlank.Skip(1).ToList());
    }
}

/// <summary>Thrown when a file cannot be read as CSV or Excel. The message is meant for the user.</summary>
public sealed class TabularFormatException(string message, Exception? inner = null) : Exception(message, inner);
