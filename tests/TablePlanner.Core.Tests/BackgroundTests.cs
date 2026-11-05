using TablePlanner.Core.Editing;
using TablePlanner.Core.Geometry;
using TablePlanner.Core.Model;
using TablePlanner.Core.Serialization;

namespace TablePlanner.Core.Tests;

public class BackgroundTests
{
    private static (EventProject Project, Floor Floor) Setup()
    {
        var project = new EventProject();
        var floor = SetupOperations.AddFloor(project, "Ground");
        return (project, floor);
    }

    [Fact]
    public void A_new_image_is_as_wide_as_the_floors_contents_and_at_least_30_m()
    {
        var (project, floor) = Setup();
        SetupOperations.AddTable(project, floor.Id, 40, 5);

        var wide = BackgroundOperations.SetImage(project, floor.Id, Guid.NewGuid(), 1000, 500);
        var (empty, emptyFloor) = Setup();
        var narrow = BackgroundOperations.SetImage(empty, emptyFloor.Id, Guid.NewGuid(), 1000, 500);

        Assert.Equal(Math.Ceiling(TableGeometry.GetBounds(floor.Tables[0]).Right), wide.Width, 6);
        Assert.Equal(30, narrow.Width, 6);
        Assert.Equal(15, narrow.Height, 6);
        Assert.Same(narrow, emptyFloor.Background);
    }

    [Fact]
    public void A_replaced_image_of_the_same_width_keeps_position_scale_and_settings()
    {
        var (project, floor) = Setup();
        var first = BackgroundOperations.SetImage(project, floor.Id, Guid.NewGuid(), 1000, 500);
        (first.X, first.Y, first.MetresPerPixel, first.Opacity, first.Locked) = (2, 3, 0.05, 0.8, true);

        var same = BackgroundOperations.SetImage(project, floor.Id, Guid.NewGuid(), 1000, 700);
        var other = BackgroundOperations.SetImage(project, floor.Id, Guid.NewGuid(), 800, 700);

        Assert.Equal((2, 3, 0.05, 0.8, true), (same.X, same.Y, same.MetresPerPixel, same.Opacity, same.Locked));
        Assert.Equal((0, 0, false), (other.X, other.Y, other.Locked));
    }

    [Fact]
    public void Calibrating_sets_the_scale_and_keeps_the_first_point_in_place()
    {
        var (project, floor) = Setup();
        var background = BackgroundOperations.SetImage(project, floor.Id, Guid.NewGuid(), 1000, 500);
        background.X = 1;
        // The points are 10 m apart as drawn; in reality they are 15 m apart.
        var first = new Point(5, 2);
        var pixelOfFirst = ((first.X - background.X) / background.MetresPerPixel, (first.Y - background.Y) / background.MetresPerPixel);

        BackgroundOperations.Calibrate(project, floor.Id, first, new Point(11, 10), 15);

        Assert.Equal(45, background.Width, 6);
        Assert.Equal(first.X, background.X + pixelOfFirst.Item1 * background.MetresPerPixel, 6);
        Assert.Equal(first.Y, background.Y + pixelOfFirst.Item2 * background.MetresPerPixel, 6);
    }

    [Fact]
    public void Calibrating_with_one_point_twice_is_refused()
    {
        var (project, floor) = Setup();
        BackgroundOperations.SetImage(project, floor.Id, Guid.NewGuid(), 1000, 500);

        Assert.Throws<ArgumentException>(() => BackgroundOperations.Calibrate(project, floor.Id, new Point(1, 1), new Point(1, 1), 5));
    }

    [Fact]
    public void Opacity_stays_between_a_faint_minimum_and_one()
    {
        var (project, floor) = Setup();
        BackgroundOperations.SetImage(project, floor.Id, Guid.NewGuid(), 10, 10);

        BackgroundOperations.SetOpacity(project, floor.Id, 0);
        var low = floor.Background!.Opacity;
        BackgroundOperations.SetOpacity(project, floor.Id, 3);

        Assert.Equal(BackgroundOperations.MinOpacity, low);
        Assert.Equal(1, floor.Background.Opacity);
    }

    [Fact]
    public void A_duplicated_floor_gets_its_own_copy_of_the_background_settings()
    {
        var (project, floor) = Setup();
        var background = BackgroundOperations.SetImage(project, floor.Id, Guid.NewGuid(), 1000, 500);

        var copy = SetupOperations.DuplicateFloor(project, floor.Id);

        Assert.NotSame(background, copy.Background);
        Assert.Equal(background.ImageId, copy.Background!.ImageId);
        Assert.Equal(background.Width, copy.Background.Width);
    }

    [Fact]
    public void A_project_file_carries_the_images_in_use_and_reads_them_back()
    {
        var (project, floor) = Setup();
        var used = new ProjectImage(Guid.NewGuid(), "data:image/jpeg;base64,AAAA");
        BackgroundOperations.SetImage(project, floor.Id, used.Id, 10, 10);
        var unused = new ProjectImage(Guid.NewGuid(), "data:image/png;base64,BBBB");

        var json = ProjectSerializer.SerializeFile(project, [used, unused]);
        var result = ProjectSerializer.Deserialize(json);

        Assert.True(result.Success);
        Assert.Equal([used], result.Images);
        Assert.Equal(used.Id, result.Project!.Setup.Floors[0].Background!.ImageId);
    }

    [Fact]
    public void Images_are_not_part_of_the_stored_project_or_undo_snapshots()
    {
        var (project, floor) = Setup();
        BackgroundOperations.SetImage(project, floor.Id, Guid.NewGuid(), 10, 10);

        var json = ProjectSerializer.Serialize(project);

        Assert.DoesNotContain("\"images\"", json);
        Assert.Empty(ProjectSerializer.Deserialize(json).Images);
    }

    [Fact]
    public void Images_in_a_file_that_are_not_pictures_are_left_out()
    {
        var json = $$"""
            { "schemaVersion": 1, "name": "x",
              "images": [ { "id": "{{Guid.NewGuid()}}", "dataUrl": "javascript:alert(1)" },
                          { "id": "{{Guid.NewGuid()}}", "dataUrl": "data:image/png;base64,AA" } ] }
            """;

        var result = ProjectSerializer.Deserialize(json);

        Assert.Equal(["data:image/png;base64,AA"], result.Images.Select(i => i.DataUrl));
    }
}
