namespace TablePlanner.Solver.Tests;

public class TableNumberTextTests
{
    [Theory]
    [InlineData(new int[0], "")]
    [InlineData(new[] { 5 }, "5")]
    [InlineData(new[] { 102, 103 }, "102, 103")]
    [InlineData(new[] { 114, 102, 103, 108, 111, 112, 113 }, "102, 103, 108, 111–114")]
    [InlineData(new[] { 1, 2, 3, 3, 7 }, "1–3, 7")]
    public void Runs_of_three_or_more_become_ranges(int[] numbers, string expected) =>
        Assert.Equal(expected, TableNumberText.Format(numbers));
}
