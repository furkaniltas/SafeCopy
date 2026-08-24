using EksimSafeCopy.Core.Models;
using FluentAssertions;
using Xunit;

namespace EksimSafeCopy.Core.Tests.Models;

public class BoundingBoxTests
{
    [Fact]
    public void Empty_BoundingBox_Is_Empty()
    {
        var box = BoundingBox.Empty;
        
        box.IsEmpty.Should().BeTrue();
        box.Width.Should().Be(0);
        box.Height.Should().Be(0);
        box.X.Should().Be(0);
        box.Y.Should().Be(0);
    }

    [Fact]
    public void Constructor_With_Valid_Values_Creates_Box()
    {
        var box = new BoundingBox(10, 20, 100, 200, 800, 600);
        
        box.X.Should().Be(10);
        box.Y.Should().Be(20);
        box.Width.Should().Be(100);
        box.Height.Should().Be(200);
        box.PageWidth.Should().Be(800);
        box.PageHeight.Should().Be(600);
        box.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void Constructor_With_Negative_Width_Throws()
    {
        var act = () => new BoundingBox(0, 0, -1, 10, 100, 100);
        
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("width");
    }

    [Fact]
    public void Constructor_With_Negative_Height_Throws()
    {
        var act = () => new BoundingBox(0, 0, 10, -1, 100, 100);
        
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("height");
    }

[Fact]
    public void Constructor_With_NaN_Width_Throws()
    {
        var act = () => new BoundingBox(0, 0, double.NaN, 10, 100, 100);
        
        act.Should().Throw<ArgumentException>();
    }
    
    [Fact]
    public void Constructor_With_Infinity_Width_Throws()
    {
        var act = () => new BoundingBox(0, 0, double.PositiveInfinity, 10, 100, 100);
        
        act.Should().Throw<ArgumentException>();
    }
    
    [Fact]
    public void Constructor_With_NaN_Height_Throws()
    {
        var act = () => new BoundingBox(0, 0, 10, double.NaN, 100, 100);
        
        act.Should().Throw<ArgumentException>();
    }
    
    [Fact]
    public void Constructor_With_Infinity_Height_Throws()
    {
        var act = () => new BoundingBox(0, 0, 10, double.PositiveInfinity, 100, 100);
        
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Right_And_Bottom_Properties_Are_Correct()
    {
        var box = new BoundingBox(10, 20, 100, 200);
        
        box.Right.Should().Be(110);
        box.Bottom.Should().Be(220);
    }

    [Fact]
    public void Center_Properties_Are_Correct()
    {
        var box = new BoundingBox(10, 20, 100, 200);
        
        box.CenterX.Should().Be(60);
        box.CenterY.Should().Be(120);
    }

    [Fact]
    public void Area_Is_Correct()
    {
        var box = new BoundingBox(0, 0, 10, 20);
        
        box.Area.Should().Be(200);
    }

    [Fact]
    public void Contains_Point_Inside_Returns_True()
    {
        var box = new BoundingBox(10, 10, 100, 100);
        
        box.Contains(50, 50).Should().BeTrue();
        box.Contains(10, 10).Should().BeTrue();
        box.Contains(110, 110).Should().BeTrue();
    }

    [Fact]
    public void Contains_Point_Outside_Returns_False()
    {
        var box = new BoundingBox(10, 10, 100, 100);
        
        box.Contains(5, 50).Should().BeFalse();
        box.Contains(50, 5).Should().BeFalse();
        box.Contains(120, 50).Should().BeFalse();
        box.Contains(50, 120).Should().BeFalse();
    }

    [Fact]
    public void Contains_Point_Struct_Works()
    {
        var box = new BoundingBox(10, 10, 100, 100);
        
        box.Contains(new Point(50, 50)).Should().BeTrue();
        box.Contains(new Point(5, 50)).Should().BeFalse();
    }

    [Fact]
    public void Intersects_Overlapping_Boxes_Returns_True()
    {
        var box1 = new BoundingBox(0, 0, 100, 100);
        var box2 = new BoundingBox(50, 50, 100, 100);
        
        box1.Intersects(box2).Should().BeTrue();
        box2.Intersects(box1).Should().BeTrue();
    }

    [Fact]
    public void Intersects_Non_Overlapping_Boxes_Returns_False()
    {
        var box1 = new BoundingBox(0, 0, 100, 100);
        var box2 = new BoundingBox(200, 200, 100, 100);
        
        box1.Intersects(box2).Should().BeFalse();
    }

    [Fact]
    public void Intersects_Edge_Touching_Returns_True()
    {
        var box1 = new BoundingBox(0, 0, 100, 100);
        var box2 = new BoundingBox(100, 0, 100, 100);
        
        box1.Intersects(box2).Should().BeTrue();
    }

    [Fact]
    public void Intersect_Returns_Overlapping_Region()
    {
        var box1 = new BoundingBox(0, 0, 100, 100, 800, 600);
        var box2 = new BoundingBox(50, 50, 100, 100, 800, 600);
        
        var intersection = box1.Intersect(box2);
        
        intersection.X.Should().Be(50);
        intersection.Y.Should().Be(50);
        intersection.Width.Should().Be(50);
        intersection.Height.Should().Be(50);
    }

    [Fact]
    public void Intersect_Non_Overlapping_Returns_Empty()
    {
        var box1 = new BoundingBox(0, 0, 100, 100);
        var box2 = new BoundingBox(200, 200, 100, 100);
        
        var intersection = box1.Intersect(box2);
        
        intersection.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Union_Combines_Two_Boxes()
    {
        var box1 = new BoundingBox(0, 0, 100, 100);
        var box2 = new BoundingBox(200, 200, 100, 100);
        
        var union = box1.Union(box2);
        
        union.X.Should().Be(0);
        union.Y.Should().Be(0);
        union.Width.Should().Be(300);
        union.Height.Should().Be(300);
    }

    [Fact]
    public void Union_With_Empty_Returns_Other()
    {
        var box = new BoundingBox(10, 10, 100, 100);
        
        box.Union(BoundingBox.Empty).Should().Be(box);
        BoundingBox.Empty.Union(box).Should().Be(box);
    }

    [Fact]
    public void Expand_Increases_Size_By_Margin()
    {
        var box = new BoundingBox(10, 10, 100, 100);
        
        var expanded = box.Expand(10);
        
        expanded.X.Should().Be(0);
        expanded.Y.Should().Be(0);
        expanded.Width.Should().Be(120);
        expanded.Height.Should().Be(120);
    }

    [Fact]
    public void Normalize_Divides_By_Page_Dimensions()
    {
        var box = new BoundingBox(400, 300, 200, 150, 800, 600);
        
        var normalized = box.Normalize(800, 600);
        
        normalized.X.Should().Be(0.5);
        normalized.Y.Should().Be(0.5);
        normalized.Width.Should().Be(0.25);
        normalized.Height.Should().Be(0.25);
        normalized.PageWidth.Should().Be(1);
        normalized.PageHeight.Should().Be(1);
    }

    [Fact]
    public void Denormalize_Multiplies_By_Page_Dimensions()
    {
        var box = new BoundingBox(0.5, 0.5, 0.25, 0.25, 1, 1);
        
        var denormalized = box.Denormalize(800, 600);
        
        denormalized.X.Should().Be(400);
        denormalized.Y.Should().Be(300);
        denormalized.Width.Should().Be(200);
        denormalized.Height.Should().Be(150);
        denormalized.PageWidth.Should().Be(800);
        denormalized.PageHeight.Should().Be(600);
    }

    [Fact]
    public void Normalize_Denormalize_Roundtrip_Preserves_Values()
    {
        var original = new BoundingBox(400, 300, 200, 150, 800, 600);
        
        var normalized = original.Normalize(800, 600);
        var roundtrip = normalized.Denormalize(800, 600);
        
        roundtrip.X.Should().BeApproximately(original.X, 0.001);
        roundtrip.Y.Should().BeApproximately(original.Y, 0.001);
        roundtrip.Width.Should().BeApproximately(original.Width, 0.001);
        roundtrip.Height.Should().BeApproximately(original.Height, 0.001);
    }

    [Fact]
    public void IsValid_Returns_False_For_Empty_Box()
    {
        BoundingBox.Empty.IsValid.Should().BeFalse();
    }

    [Fact]
    public void IsValid_Returns_False_For_NaN_Values()
    {
        var box = new BoundingBox(double.NaN, 0, 10, 10, 100, 100);
        
        box.IsValid.Should().BeFalse();
    }

    [Fact]
    public void IsWithinPageBounds_Returns_True_For_Box_Inside_Page()
    {
        var box = new BoundingBox(10, 10, 100, 100, 800, 600);
        
        box.IsWithinPageBounds.Should().BeTrue();
    }

    [Fact]
    public void IsWithinPageBounds_Returns_False_For_Box_Outside_Page()
    {
        var box = new BoundingBox(-10, -10, 100, 100, 800, 600);
        
        box.IsWithinPageBounds.Should().BeFalse();
    }

    [Fact]
    public void Equality_Works_Correctly()
    {
        var box1 = new BoundingBox(10, 20, 100, 200, 800, 600);
        var box2 = new BoundingBox(10, 20, 100, 200, 800, 600);
        var box3 = new BoundingBox(10, 20, 100, 200, 800, 600);
        
        box1.Should().Be(box2);
        box1.GetHashCode().Should().Be(box2.GetHashCode());
        
        (box1 == box2).Should().BeTrue();
        (box1 != box3).Should().BeFalse();
    }

    [Fact]
    public void Transform_With_Identity_Returns_Same_Box()
    {
        var box = new BoundingBox(10, 20, 100, 200);
        var transform = CoordinateTransform.Identity;
        
        var result = box.Transform(transform);
        
        result.Should().Be(box);
    }

    [Fact]
    public void Transform_With_Scale_Scales_Box()
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
    public void Transform_With_Translation_Moves_Box()
    {
        var box = new BoundingBox(10, 20, 100, 200);
        var transform = CoordinateTransform.CreateTranslation(50, -30);
        
        var result = box.Transform(transform);
        
        result.X.Should().Be(60);
        result.Y.Should().Be(-10);
        result.Width.Should().Be(100);
        result.Height.Should().Be(200);
    }
}