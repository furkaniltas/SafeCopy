using SafeCopy.Core.Abstractions;
using SafeCopy.Core.Models;
using FluentAssertions;
using Xunit;

namespace SafeCopy.Core.Tests.Models;

public class DocumentTests
{
    [Fact]
    public void Constructor_With_Valid_Params_Creates_Document()
    {
        var pages = new List<DocumentPage> { new DocumentPage { PageNumber = 1 } };
        var doc = new Document("Test Document", DocumentFormat.Pdf, pages);
        
        doc.Name.Should().Be("Test Document");
        doc.Format.Should().Be(DocumentFormat.Pdf);
        doc.Pages.Should().HaveCount(1);
        doc.PageCount.Should().Be(1);
    }

    [Fact]
    public void Constructor_With_Empty_Name_Throws()
    {
        var pages = new List<DocumentPage> { new DocumentPage { PageNumber = 1 } };
        
        var act = () => new Document("", DocumentFormat.Pdf, pages);
        
        act.Should().Throw<ArgumentException>()
            .WithParameterName("name");
    }

    [Fact]
    public void Constructor_With_Null_Name_Throws()
    {
        var pages = new List<DocumentPage> { new DocumentPage { PageNumber = 1 } };
        
        var act = () => new Document(null!, DocumentFormat.Pdf, pages);
        
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_With_Null_Pages_Throws()
    {
        var act = () => new Document("Test", DocumentFormat.Pdf, null!);
        
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("pages");
    }

    [Fact]
    public void Default_Constructor_Creates_Valid_Document()
    {
        var doc = new Document();
        
        doc.Id.Should().NotBeEmpty();
        doc.Name.Should().BeEmpty();
        doc.Pages.Should().BeEmpty();
        doc.PageCount.Should().Be(0);
        doc.TotalCharacters.Should().Be(0);
        doc.Metadata.Should().NotBeNull();
        doc.Source.Should().NotBeNull();
    }

    [Fact]
    public void Id_Is_Generated_By_Default()
    {
        var doc1 = new Document();
        var doc2 = new Document();
        
        doc1.Id.Should().NotBeEmpty();
        doc2.Id.Should().NotBeEmpty();
        doc1.Id.Should().NotBe(doc2.Id);
    }

    [Fact]
    public void WithPages_Returns_New_Document_With_Updated_Pages()
    {
        var original = new Document("Test", DocumentFormat.Pdf, new List<DocumentPage> { new() { PageNumber = 1 } });
        var newPages = new List<DocumentPage> { new() { PageNumber = 1 }, new() { PageNumber = 2 } };
        
        var updated = original.WithPages(newPages);
        
        updated.Pages.Should().HaveCount(2);
        updated.PageCount.Should().Be(2);
        updated.Id.Should().Be(original.Id);
        updated.Name.Should().Be(original.Name);
        updated.Format.Should().Be(original.Format);
    }

    [Fact]
    public void WithMetadata_Returns_New_Document_With_Updated_Metadata()
    {
        var original = new Document("Test", DocumentFormat.Pdf, new List<DocumentPage> { new() { PageNumber = 1 } });
        var metadata = new DocumentMetadata { Title = "New Title", Author = "Author" };
        
        var updated = original.WithMetadata(metadata);
        
        updated.Metadata.Title.Should().Be("New Title");
        updated.Metadata.Author.Should().Be("Author");
        updated.Id.Should().Be(original.Id);
    }

    [Fact]
    public void TotalCharacters_Sums_All_Page_Text_Lengths()
    {
        var pages = new List<DocumentPage>
        {
            new() { PageNumber = 1, Text = "Hello" },
            new() { PageNumber = 2, Text = "World" }
        };
        var doc = new Document("Test", DocumentFormat.Pdf, pages);
        
        doc.TotalCharacters.Should().Be(10);
    }

    [Fact]
    public void Pages_Are_Immutable()
    {
        var pages = new List<DocumentPage> { new() { PageNumber = 1 } };
        var doc = new Document("Test", DocumentFormat.Pdf, pages);
        
        pages.Clear();
        
        doc.Pages.Should().HaveCount(1);
    }
}

public class DocumentPageTests
{
    [Fact]
    public void Constructor_With_Valid_Values_Creates_Page()
    {
        var page = new DocumentPage
        {
            PageNumber = 1,
            Width = 800,
            Height = 600,
            DpiX = 72,
            DpiY = 72
        };
        
        page.PageNumber.Should().Be(1);
        page.Width.Should().Be(800);
        page.Height.Should().Be(600);
        page.DpiX.Should().Be(72);
        page.DpiY.Should().Be(72);
    }

[Fact]
    public void Constructor_Negative_Width_Throws()
    {
        var act = () => new DocumentPage(-1, 600, 1, 72, 72);
        
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("width");
    }
    
    [Fact]
    public void Constructor_Negative_Height_Throws()
    {
        var act = () => new DocumentPage(800, -1, 1, 72, 72);
        
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("height");
    }
    
    [Fact]
    public void Constructor_Negative_PageNumber_Throws()
    {
        var act = () => new DocumentPage(800, 600, -1, 72, 72);
        
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("pageNumber");
    }
    
    [Fact]
    public void Constructor_Zero_DpiX_Throws()
    {
        var act = () => new DocumentPage(800, 600, 1, 0, 72);
        
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("dpiX");
    }
    
    [Fact]
    public void Constructor_Negative_DpiY_Throws()
    {
        var act = () => new DocumentPage(800, 600, 1, 72, -1);
        
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("dpiY");
    }

    [Fact]
    public void Default_Values_Are_Valid()
    {
        var page = new DocumentPage();
        
        page.PageNumber.Should().Be(0);
        page.Width.Should().Be(0);
        page.Height.Should().Be(0);
        page.DpiX.Should().Be(0);
        page.DpiY.Should().Be(0);
        page.Text.Should().BeEmpty();
        page.TextBlocks.Should().BeEmpty();
        page.Images.Should().BeEmpty();
        page.Rotation.Should().Be(PageRotation.None);
        page.IsScanned.Should().BeFalse();
        page.OcrInfo.Should().BeNull();
    }

    [Fact]
    public void Properties_Can_Be_Set()
    {
        var page = new DocumentPage
        {
            PageNumber = 1,
            Width = 800,
            Height = 600,
            DpiX = 300,
            DpiY = 300,
            Text = "Test text",
            TextBlocks = new List<TextBlock> { new() { Text = "Block 1" } },
            Images = new List<ImageReference> { new() { Id = "img1" } },
            Rotation = PageRotation.Rotate90,
            IsScanned = true
        };
        
        page.Text.Should().Be("Test text");
        page.TextBlocks.Should().HaveCount(1);
        page.Images.Should().HaveCount(1);
        page.Rotation.Should().Be(PageRotation.Rotate90);
        page.IsScanned.Should().BeTrue();
    }
}

public class TextBlockTests
{
    [Fact]
    public void Constructor_With_Valid_Values_Creates_Block()
    {
        var block = new TextBlock
        {
            Id = "block1",
            Text = "Test block",
            Type = TextBlockType.Paragraph,
            Direction = TextDirection.LeftToRight,
            PageNumber = 1,
            OrderIndex = 0
        };
        
        block.Id.Should().Be("block1");
        block.Text.Should().Be("Test block");
        block.Type.Should().Be(TextBlockType.Paragraph);
        block.Direction.Should().Be(TextDirection.LeftToRight);
        block.PageNumber.Should().Be(1);
        block.OrderIndex.Should().Be(0);
    }

[Fact]
    public void Constructor_Negative_OrderIndex_Throws()
    {
        var act = () => new TextBlock(-1, 1);
        
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("orderIndex");
    }
    
    [Fact]
    public void Constructor_Negative_PageNumber_Throws()
    {
        var act = () => new TextBlock(0, -1);
        
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("pageNumber");
    }

    [Fact]
    public void Default_Values_Are_Valid()
    {
        var block = new TextBlock();
        
        block.Id.Should().NotBeEmpty();
        block.Text.Should().BeEmpty();
        block.Type.Should().Be(TextBlockType.Paragraph);
        block.Direction.Should().Be(TextDirection.LeftToRight);
        block.Spans.Should().BeEmpty();
        block.Properties.Should().BeEmpty();
    }

    [Fact]
    public void Spans_Can_Be_Added()
    {
        var block = new TextBlock
        {
            Spans = new List<TextSpan>
            {
                new() { StartIndex = 0, Length = 5, Text = "Hello" },
                new() { StartIndex = 6, Length = 5, Text = "World" }
            }
        };
        
        block.Spans.Should().HaveCount(2);
        block.Text.Should().BeEmpty();
    }
}

public class TextSpanTests
{
    [Fact]
    public void Constructor_With_Valid_Values_Creates_Span()
    {
        var span = new TextSpan
        {
            StartIndex = 0,
            Length = 5,
            Text = "Hello",
            BlockId = 1
        };
        
        span.StartIndex.Should().Be(0);
        span.Length.Should().Be(5);
        span.Text.Should().Be("Hello");
        span.BlockId.Should().Be(1);
    }

[Fact]
    public void Constructor_Negative_StartIndex_Throws()
    {
        var act = () => new TextSpan(-1, 5);
        
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("startIndex");
    }
    
    [Fact]
    public void Constructor_Negative_Length_Throws()
    {
        var act = () => new TextSpan(0, -1);
        
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("length");
    }

    [Fact]
    public void EndIndex_Returns_Start_Plus_Length()
    {
        var span = new TextSpan { StartIndex = 10, Length = 5 };
        
        span.EndIndex.Should().Be(15);
    }

    [Fact]
    public void Overlaps_Returns_True_For_Overlapping_Spans()
    {
        var span1 = new TextSpan { StartIndex = 0, Length = 10 };
        var span2 = new TextSpan { StartIndex = 5, Length = 10 };
        
        span1.Overlaps(span2).Should().BeTrue();
        span2.Overlaps(span1).Should().BeTrue();
    }

    [Fact]
    public void Overlaps_Returns_False_For_Non_Overlapping_Spans()
    {
        var span1 = new TextSpan { StartIndex = 0, Length = 5 };
        var span2 = new TextSpan { StartIndex = 10, Length = 5 };
        
        span1.Overlaps(span2).Should().BeFalse();
        span2.Overlaps(span1).Should().BeFalse();
    }

    [Fact]
    public void Overlaps_Returns_False_For_Adjacent_Spans()
    {
        var span1 = new TextSpan { StartIndex = 0, Length = 5 };
        var span2 = new TextSpan { StartIndex = 5, Length = 5 };
        
        span1.Overlaps(span2).Should().BeFalse();
    }

    [Fact]
    public void Default_Values_Are_Valid()
    {
        var span = new TextSpan();
        
        span.StartIndex.Should().Be(0);
        span.Length.Should().Be(0);
        span.Text.Should().BeEmpty();
        span.BlockId.Should().Be(0);
        span.Properties.Should().BeEmpty();
    }
}