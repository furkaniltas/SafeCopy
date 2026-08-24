using EksimSafeCopy.Core.Models;
using FluentAssertions;
using Xunit;

namespace EksimSafeCopy.Core.Tests.Models;

public class CoordinateSystemTests
{
    [Fact]
    public void Constructor_With_Valid_Dimensions_Creates_System()
    {
        var cs = new CoordinateSystem(800, 600);
        
        cs.PageWidth.Should().Be(800);
        cs.PageHeight.Should().Be(600);
        cs.Origin.Should().Be(CoordinateOrigin.TopLeft);
        cs.Unit.Should().Be(CoordinateUnit.Points);
    }

    [Fact]
    public void Constructor_With_Custom_Origin_And_Unit()
    {
        var cs = new CoordinateSystem(800, 600, CoordinateOrigin.BottomLeft, CoordinateUnit.Millimeters);
        
        cs.Origin.Should().Be(CoordinateOrigin.BottomLeft);
        cs.Unit.Should().Be(CoordinateUnit.Millimeters);
    }

    [Fact]
    public void Constructor_With_Zero_Width_Throws()
    {
        var act = () => new CoordinateSystem(0, 600);
        
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Constructor_With_Negative_Height_Throws()
    {
        var act = () => new CoordinateSystem(800, -600);
        
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Normalize_Converts_To_0_1_Range()
    {
        var cs = new CoordinateSystem(800, 600);
        var box = new BoundingBox(400, 300, 200, 150, 800, 600);
        
        var normalized = cs.Normalize(box);
        
        normalized.X.Should().Be(0.5);
        normalized.Y.Should().Be(0.5);
        normalized.Width.Should().Be(0.25);
        normalized.Height.Should().Be(0.25);
    }

    [Fact]
    public void Denormalize_Converts_From_0_1_Range()
    {
        var cs = new CoordinateSystem(800, 600);
        var box = new BoundingBox(0.5, 0.5, 0.25, 0.25, 1, 1);
        
        var denormalized = cs.Denormalize(box);
        
        denormalized.X.Should().Be(400);
        denormalized.Y.Should().Be(300);
        denormalized.Width.Should().Be(200);
        denormalized.Height.Should().Be(150);
    }

    [Fact]
    public void ConvertTo_Converts_Between_Systems()
    {
        var source = new CoordinateSystem(800, 600);
        var target = new CoordinateSystem(1600, 1200);
        var box = new BoundingBox(400, 300, 200, 150, 800, 600);
        
        var converted = source.ConvertTo(box, target);
        
        converted.X.Should().Be(800);
        converted.Y.Should().Be(600);
        converted.Width.Should().Be(400);
        converted.Height.Should().Be(300);
        converted.PageWidth.Should().Be(1600);
        converted.PageHeight.Should().Be(1200);
    }

    [Fact]
    public void Roundtrip_Normalize_Denormalize_Preserves_Values()
    {
        var cs = new CoordinateSystem(800, 600);
        var original = new BoundingBox(400, 300, 200, 150, 800, 600);
        
        var normalized = cs.Normalize(original);
        var roundtrip = cs.Denormalize(normalized);
        
        roundtrip.X.Should().BeApproximately(original.X, 0.001);
        roundtrip.Y.Should().BeApproximately(original.Y, 0.001);
        roundtrip.Width.Should().BeApproximately(original.Width, 0.001);
        roundtrip.Height.Should().BeApproximately(original.Height, 0.001);
    }

    [Fact]
    public void Different_Origins_Produce_Different_Coordinates()
    {
        var topLeft = new CoordinateSystem(800, 600, CoordinateOrigin.TopLeft);
        var bottomLeft = new CoordinateSystem(800, 600, CoordinateOrigin.BottomLeft);
        var box = new BoundingBox(400, 300, 200, 150, 800, 600);
        
        var normalizedTop = topLeft.Normalize(box);
        var normalizedBottom = bottomLeft.Normalize(box);
        
        normalizedTop.Y.Should().Be(0.5);
        normalizedBottom.Y.Should().Be(0.5);
    }
}

public class CoordinateTransformTests
{
    [Fact]
    public void Identity_Transform_Returns_Same_Box()
    {
        var box = new BoundingBox(10, 20, 100, 200);
        var transform = CoordinateTransform.Identity;
        
        var result = box.Transform(transform);
        
        result.Should().Be(box);
    }

    [Fact]
    public void Scale_Transform_Scales_Box()
    {
        var box = new BoundingBox(10, 20, 100, 200);
        var transform = CoordinateTransform.CreateScale(2, 0.5);
        
        var result = box.Transform(transform);
        
        result.X.Should().Be(20);
        result.Y.Should().Be(10);
        result.Width.Should().Be(200);
        result.Height.Should().Be(100);
    }

    [Fact]
    public void Translation_Transform_Moves_Box()
    {
        var box = new BoundingBox(10, 20, 100, 200);
        var transform = CoordinateTransform.CreateTranslation(50, -30);
        
        var result = box.Transform(transform);
        
        result.X.Should().Be(60);
        result.Y.Should().Be(-10);
        result.Width.Should().Be(100);
        result.Height.Should().Be(200);
    }

    [Fact]
    public void Combined_Transform_Applies_Both()
    {
        var box = new BoundingBox(10, 20, 100, 200);
        var transform = new CoordinateTransform { ScaleX = 2, ScaleY = 2, TranslateX = 10, TranslateY = 20 };
        
        var result = box.Transform(transform);
        
        result.X.Should().Be(30);
        result.Y.Should().Be(60);
        result.Width.Should().Be(200);
        result.Height.Should().Be(400);
    }

    [Fact]
    public void Transform_Point_With_Identity_Returns_Same()
    {
        var point = new Point(100, 200);
        var transform = CoordinateTransform.Identity;
        
        var result = transform.Transform(point);
        
        result.Should().Be(point);
    }

    [Fact]
    public void Transform_Point_With_Scale_Scales_Point()
    {
        var point = new Point(100, 200);
        var transform = CoordinateTransform.CreateScale(0.5, 2);
        
        var result = transform.Transform(point);
        
        result.X.Should().Be(50);
        result.Y.Should().Be(400);
    }
}