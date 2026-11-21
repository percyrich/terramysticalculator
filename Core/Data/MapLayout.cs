namespace Core;

// The shape of a map: how many hexes are in each row. Hexes are numbered row by row, left to right,
// starting at 0. Rows alternate between long and short; a short row sits half a hex to the right,
// tucked between the long rows above and below it.
public sealed record MapLayout(ValueList<int> RowLengths)
{
    // The six directions from a hex, in "doubled" columns (see Neighbours).
    private static readonly (int Row, int X)[] Directions =
    {
        (0, -2), (0, 2), (-1, -1), (-1, 1), (1, -1), (1, 1),
    };

    public int HexCount => RowLengths.Sum();
    public int RowCount => RowLengths.Count;

    // True for short rows, which are drawn half a hex to the right.
    public bool IsShifted(int row) => RowLengths[row] < RowLengths.Max();

    public bool Contains(int hex) => hex >= 0 && hex < HexCount;

    public (int Row, int Column) Position(int hex)
    {
        var remaining = hex;
        for (var row = 0; row < RowCount; row++)
        {
            if (remaining < RowLengths[row])
                return (row, remaining);
            remaining -= RowLengths[row];
        }
        throw new ArgumentOutOfRangeException(nameof(hex));
    }

    // The hex at (row, column), or -1 if that is off the map.
    public int IndexOf(int row, int column)
    {
        if (row < 0 || row >= RowCount || column < 0 || column >= RowLengths[row])
            return -1;
        return RowLengths.Take(row).Sum() + column;
    }

    // The hexes touching this one (up to six).
    // Trick: give every hex a "doubled" x = 2 * column, plus 1 in shifted rows. Then neighbours in the
    // same row are 2 apart, and neighbours in the rows above and below are 1 to the left or right.
    public IEnumerable<int> Neighbours(int hex)
    {
        var (row, column) = Position(hex);
        var x = 2 * column + (IsShifted(row) ? 1 : 0);

        foreach (var (rowStep, xStep) in Directions)
        {
            var otherRow = row + rowStep;
            if (otherRow < 0 || otherRow >= RowCount)
                continue;

            var doubled = x + xStep - (IsShifted(otherRow) ? 1 : 0);
            if (doubled < 0 || doubled % 2 != 0)
                continue;

            var neighbour = IndexOf(otherRow, doubled / 2);
            if (neighbour >= 0)
                yield return neighbour;
        }
    }
}
