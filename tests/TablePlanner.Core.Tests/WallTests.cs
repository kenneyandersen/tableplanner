using System.Xml.Linq;
using TablePlanner.Core.Editing;
using TablePlanner.Core.Geometry;
using TablePlanner.Core.Model;

namespace TablePlanner.Core.Tests;

public class WallTests
{
    private static (EventProject Project, Floor Floor) Empty()
    {
        var project = new EventProject();
        var floor = new Floor { Name = "Hall", Number = 1 };
        project.Setup.Floors.Add(floor);
        return (project, floor);
    }

    [Fact]
    public void A_room_is_a_closed_wall_of_four_corners()
    {
        var (project, floor) = Empty();

        var room = SetupOperations.AddRoom(project, floor.Id, 1, 2, 10, 6);

        Assert.True(room.Closed);
        Assert.Equal([new Point(1, 2), new Point(11, 2), new Point(11, 8), new Point(1, 8)], room.Points);
        Assert.Equal(32, WallDrawing.Segments(room).Sum(s => WallDrawing.Length(s.From, s.To)));
    }

    [Fact]
    public void Walls_move_rotate_copy_and_delete_with_the_selection()
    {
        var (project, floor) = Empty();
        var wall = SetupOperations.AddWall(project, floor.Id, [new Point(0, 0), new Point(4, 0)]);

        SetupOperations.MoveItems(project, floor.Id, [wall.Id], 1, 1);
        Assert.Equal([new Point(1, 1), new Point(5, 1)], wall.Points);

        SetupOperations.RotateItems(project, floor.Id, [wall.Id]);
        Assert.Equal([new Point(3, -1), new Point(3, 3)], wall.Points); // a quarter turn around its middle (3, 1)

        var copies = SetupOperations.DuplicateItems(project, floor.Id, [wall.Id], 2, 0);
        Assert.Equal(2, floor.Walls.Count);
        Assert.Equal([new Point(5, -1), new Point(5, 3)], floor.Walls.Single(w => w.Id == copies[0]).Points);

        SetupOperations.RemoveItems(project, floor.Id, [wall.Id]);
        Assert.Single(floor.Walls);
        Assert.Single(SetupOperations.DuplicateFloor(project, floor.Id).Walls);
    }

    [Fact]
    public void Corners_can_be_moved_added_and_removed()
    {
        var (project, floor) = Empty();
        var wall = SetupOperations.AddWall(project, floor.Id, [new Point(0, 0), new Point(4, 0), new Point(4, 3)], closed: true);

        SetupOperations.InsertWallCorner(project, floor.Id, wall.Id, 0, new Point(2, -1));
        SetupOperations.MoveWallCorner(project, floor.Id, wall.Id, 1, new Point(2, -2));
        Assert.Equal([new Point(0, 0), new Point(2, -2), new Point(4, 0), new Point(4, 3)], wall.Points);

        SetupOperations.RemoveWallCorner(project, floor.Id, wall.Id, 1);
        SetupOperations.RemoveWallCorner(project, floor.Id, wall.Id, 2);
        Assert.False(wall.Closed); // two corners left: no longer a room
        SetupOperations.RemoveWallCorner(project, floor.Id, wall.Id, 0);
        Assert.Empty(floor.Walls); // one corner is no wall
    }

    [Theory]
    [InlineData(3.2, 0.4, false, false, 3, 0)] // straight across, length on the grid
    [InlineData(0.3, 2.6, false, false, 0, 2.5)] // straight down
    [InlineData(3.2, 0.4, true, false, 3, 0.5)] // Shift: any angle
    [InlineData(3.2, 0.4, false, true, 3.2, 0)] // Alt: off the grid
    public void The_next_corner_is_straight_and_on_the_grid_unless_asked(double x, double y, bool anyAngle, bool free, double expectedX, double expectedY)
    {
        var next = WallDrawing.NextCorner(new Point(0, 0), new Point(x, y), anyAngle, free);

        Assert.Equal(expectedX, next.X, 6);
        Assert.Equal(expectedY, next.Y, 6);
        Assert.Equal(new Point(1.5, 2), WallDrawing.NextCorner(null, new Point(1.4, 2.1), false, false));
    }

    [Fact]
    public void Walls_are_drawn_with_a_gap_for_an_entrance()
    {
        var (project, floor) = Empty();
        SetupOperations.AddRoom(project, floor.Id, 0, 0, 10, 6);
        Assert.DoesNotContain("<mask", WallDrawing.Svg(floor, "f"));
        floor.Elements.Add(new FloorElement { Kind = FloorElementKind.Entrance, X = 5, Y = 0, Width = 2, Depth = 0.5 });

        var svg = XElement.Parse($"<svg>{WallDrawing.Svg(floor, "f")}</svg>");

        Assert.Single(svg.Descendants("polygon"));
        Assert.Equal("url(#wall-gaps-f)", svg.Descendants("g").Single().Attribute("mask")!.Value);
        Assert.Contains(svg.Descendants("rect"), r => r.Attribute("fill")!.Value == "black");
    }

    [Fact]
    public void Walls_survive_saving_and_loading()
    {
        var (project, floor) = Empty();
        SetupOperations.AddRoom(project, floor.Id, 0.5, 1, 3, 2);

        var loaded = Serialization.ProjectSerializer.Deserialize(Serialization.ProjectSerializer.Serialize(project)).Project!;

        var wall = Assert.Single(loaded.Setup.Floors[0].Walls);
        Assert.True(wall.Closed);
        Assert.Equal(floor.Walls[0].Points, wall.Points);
    }
}
