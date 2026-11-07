namespace TablePlanner.Core.Model;

public enum TableShape
{
    /// <summary>Rectangular table with seats along both long sides and optional end seats.</summary>
    Long,

    /// <summary>Round table with <see cref="Table.RoundSeats"/> seats around it; the diameter follows from the seat count.</summary>
    Round,
}

/// <summary>Rotation in 90° steps, clockwise.</summary>
public enum Rotation
{
    R0,
    R90,
    R180,
    R270,
}

/// <summary>
/// A table on a floor. Seats are not stored; they are generated from the table's
/// shape and size by <see cref="Geometry.TableGeometry"/>.
/// </summary>
public sealed class Table
{
    public const int MaxEndSeats = 2;

    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Number shown on cards and posters. Assigned per floor (101, 102, …), editable.</summary>
    public int Number { get; set; }

    public TableShape Shape { get; set; } = TableShape.Long;

    /// <summary>Centre of the table, in metres.</summary>
    public double X { get; set; }

    /// <summary>Centre of the table, in metres.</summary>
    public double Y { get; set; }

    public Rotation Rotation { get; set; } = Rotation.R0;

    /// <summary>Seats along each long side.</summary>
    public int SeatsPerSide { get; set; } = 8;

    /// <summary>Seats at the short ends: 0, 1 (left end) or 2 (both ends).</summary>
    public int EndSeats { get; set; }

    /// <summary>Space per seat along a side, in metres. Table length = SeatsPerSide × SeatSpacing.</summary>
    public double SeatSpacing { get; set; } = 0.6;

    /// <summary>Table width (across), in metres.</summary>
    public double Width { get; set; } = 0.8;

    /// <summary>Indices of seats that cannot be used (pillar, wheelchair space, reserved).</summary>
    public HashSet<int> DisabledSeats { get; set; } = [];

    /// <summary>Seats around a round table (only used when <see cref="Shape"/> is <see cref="TableShape.Round"/>).</summary>
    public int RoundSeats { get; set; } = 8;

    public const int MinRoundSeats = 2;

    public bool IsRound => Shape == TableShape.Round;

    /// <summary>
    /// Diameter of a round table: each seat gets <see cref="SeatSpacing"/> of the edge (8 seats ≈ 1.5 m, 10 ≈ 1.9 m),
    /// at least 0.9 m.
    /// </summary>
    public double Diameter => Math.Max(0.9, RoundSeats * SeatSpacing / Math.PI);

    /// <summary>Length of a long table, or the diameter of a round one.</summary>
    public double Length => IsRound ? Diameter : SeatsPerSide * SeatSpacing;

    /// <summary>Width (across) of a long table, or the diameter of a round one.</summary>
    public double TopWidth => IsRound ? Diameter : Width;

    public int SeatCount => IsRound ? RoundSeats : 2 * SeatsPerSide + EndSeats;

    public bool IsValidSeatIndex(int index) => index >= 0 && index < SeatCount;

    public int AvailableSeatCount => SeatCount - DisabledSeats.Count(IsValidSeatIndex);
}
