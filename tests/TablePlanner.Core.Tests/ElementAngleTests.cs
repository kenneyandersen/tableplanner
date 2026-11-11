using TablePlanner.Core.Editing;
using TablePlanner.Core.Geometry;
using TablePlanner.Core.Model;
using TablePlanner.Core.Serialization;

namespace TablePlanner.Core.Tests;

public class ElementAngleTests
{
    [Fact]
    public void A_quarter_turn_from_an_older_project_becomes_an_angle()
    {
        var element = new FloorElement { Rotation = Rotation.R270 };

        Assert.Equal(270, element.Angle);
        Assert.True(element.IsQuarterTurn);
        element.Angle = 100;
        Assert.Equal(Rotation.R90, element.Rotation); // the nearest quarter turn
        Assert.False(element.IsQuarterTurn);
        element.Angle = -45;
        Assert.Equal(315, element.Angle);
    }

    [Fact]
    public void An_older_file_without_an_angle_and_a_new_one_with_it_both_load()
    {
        var project = new EventProject();
        var floor = new Floor { Name = "F", Number = 1 };
        floor.Elements.Add(new FloorElement { Kind = FloorElementKind.Bar, Angle = 45 });
        project.Setup.Floors.Add(floor);
        var json = ProjectSerializer.Serialize(project);
        var older = json.Replace("\"angle\": 45,", "").Replace("\"angle\":45,", "").Replace("\"rotation\": \"R0\"", "\"rotation\": \"R90\"").Replace("\"rotation\":\"R0\"", "\"rotation\":\"R90\"");

        Assert.Equal(45, ProjectSerializer.Deserialize(json).Project!.Setup.Floors[0].Elements[0].Angle);
        Assert.Equal(90, ProjectSerializer.Deserialize(older).Project!.Setup.Floors[0].Elements[0].Angle);
    }

    [Fact]
    public void A_turned_element_takes_up_its_turned_outline()
    {
        var element = new FloorElement { X = 0, Y = 0, Width = 4, Depth = 2, Angle = 45 };

        var bounds = FloorElementGeometry.GetBounds(element);

        var expected = (4 + 2) / Math.Sqrt(2);
        Assert.Equal(expected, bounds.Width, 6);
        Assert.Equal(expected, bounds.Height, 6);
        Assert.Equal("rotate(45 0 0)", FloorElementGeometry.Transform(element));
        Assert.Contains("rotate(45 0 0)", ElementDrawing.Svg(new FloorElement { Kind = FloorElementKind.Stairs, Width = 2, Depth = 3, Angle = 45 }));
    }

    [Fact]
    public void Rotating_adds_a_quarter_turn_and_copies_keep_everything()
    {
        var project = new EventProject();
        var floor = new Floor { Name = "F", Number = 1 };
        project.Setup.Floors.Add(floor);
        var pillar = new FloorElement { Kind = FloorElementKind.Pillar, Angle = 30, IsRound = true, LabelSize = 0.8, LabelOffsetX = 1, LabelOffsetY = -1 };
        floor.Elements.Add(pillar);

        SetupOperations.RotateItems(project, floor.Id, [pillar.Id]);
        Assert.Equal(120, pillar.Angle);
        SetupOperations.SetElementAngle(project, floor.Id, pillar.Id, 400);
        Assert.Equal(40, pillar.Angle);

        var copyId = SetupOperations.DuplicateItems(project, floor.Id, [pillar.Id], 1, 1)[0];
        var copy = floor.Elements.Single(e => e.Id == copyId);
        Assert.Equal((40.0, true, (double?)0.8, 1.0, -1.0), (copy.Angle, copy.IsRound, copy.LabelSize, copy.LabelOffsetX, copy.LabelOffsetY));
    }
}
