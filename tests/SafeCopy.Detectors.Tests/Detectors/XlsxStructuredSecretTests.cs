using SafeCopy.Core.Abstractions;
using SafeCopy.Core.Models;
using SafeCopy.DocumentEngine.Security;
using SafeCopy.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using DF = SafeCopy.Core.Abstractions.DocumentFormat;

namespace SafeCopy.Detectors.Tests.Detectors;

public class XlsxStructuredSecretTests
{
    private readonly IDetectionEngine _engine;

    public XlsxStructuredSecretTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new DocumentSecurityOptions());
        services.AddSingleton<IDocumentSecurityValidator, SafeCopy.DocumentEngine.Security.DocumentSecurityValidator>();
        services.AddSingleton<IFileSystem, FileSystem>();
        services.AddDetectors();
        var sp = services.BuildServiceProvider();
        _engine = sp.GetRequiredService<IDetectionEngine>();
    }

    private Document Make(string header, string value)
    {
        var blocks = new List<TextBlock>
        {
            new TextBlock{ Text=$"A1: {header}", PageNumber=1, OrderIndex=0, Type=TextBlockType.Paragraph, Properties=new Dictionary<string,object>{{"CellReference","A1"}}, Spans=new[]{ new TextSpan{ StartIndex=0, Length=($"A1: {header}").Length, Text=$"A1: {header}" } }.ToList().AsReadOnly() },
            new TextBlock{ Text=$"A2: {value}", PageNumber=1, OrderIndex=1, Type=TextBlockType.Paragraph, Properties=new Dictionary<string,object>{{"CellReference","A2"}}, Spans=new[]{ new TextSpan{ StartIndex=0, Length=($"A2: {value}").Length, Text=$"A2: {value}" } }.ToList().AsReadOnly() }
        };
        var text = string.Join("\n", blocks.Select(b=>b.Text));
        var page = new DocumentPage(100,100,1,72,72){ Text=text, PageNumber=1, TextBlocks=blocks.AsReadOnly() };
        return new Document{ Name="test.xlsx", Format=DF.Xlsx, Pages=new[]{page}, Metadata=new DocumentMetadata()};
    }

    [Theory]
    [InlineData("password")]
    [InlineData("key")]
    [InlineData("api")]
    [InlineData("şifre")]
    [InlineData("sifre")]
    [InlineData("parola")]
    [InlineData("gizli anahtar")]
    [InlineData("api anahtarı")]
    [InlineData("apikey")]
    [InlineData("api_key")]
    [InlineData("access_token")]
    [InlineData("client_secret")]
    [InlineData("Şifre (Sahte Örnek)")]
    public void Xlsx_SecretHeaders_Detected(string header)
    {
        var doc = Make(header, "mySecret123");
        var res = _engine.Detect(doc);
        res.Value.Should().Contain(d=>d.Type==DetectionType.Secret, $"{header} should be Secret");
    }

    [Theory]
    [InlineData("primary key")]
    [InlineData("foreign key")]
    [InlineData("record key")]
    [InlineData("key id")]
    [InlineData("key management")]
    [InlineData("API documentation")]
    [InlineData("API description")]
    [InlineData("API endpoint")]
    [InlineData("token validation")]
    [InlineData("password policy")]
    public void Xlsx_FalsePositiveHeaders_NotSecret(string header)
    {
        var doc = Make(header, "123");
        var res = _engine.Detect(doc);
        res.Value.Should().NotContain(d=>d.Type==DetectionType.Secret, $"{header} should not be Secret");
    }

    [Fact]
    public void Xlsx_Password_Key_Api_Together_AllSecret()
    {
        var blocks = new List<TextBlock>
        {
            new TextBlock{ Text="A1: ad soyad", PageNumber=1, OrderIndex=0, Type=TextBlockType.Paragraph, Properties=new Dictionary<string,object>{{"CellReference","A1"}}, Spans=new[]{ new TextSpan{ StartIndex=0, Length="A1: ad soyad".Length, Text="A1: ad soyad" } }.ToList().AsReadOnly() },
            new TextBlock{ Text="C1: password", PageNumber=1, OrderIndex=1, Type=TextBlockType.Paragraph, Properties=new Dictionary<string,object>{{"CellReference","C1"}}, Spans=new[]{ new TextSpan{ StartIndex=0, Length="C1: password".Length, Text="C1: password" } }.ToList().AsReadOnly() },
            new TextBlock{ Text="D1: key", PageNumber=1, OrderIndex=2, Type=TextBlockType.Paragraph, Properties=new Dictionary<string,object>{{"CellReference","D1"}}, Spans=new[]{ new TextSpan{ StartIndex=0, Length="D1: key".Length, Text="D1: key" } }.ToList().AsReadOnly() },
            new TextBlock{ Text="E1: api", PageNumber=1, OrderIndex=3, Type=TextBlockType.Paragraph, Properties=new Dictionary<string,object>{{"CellReference","E1"}}, Spans=new[]{ new TextSpan{ StartIndex=0, Length="E1: api".Length, Text="E1: api" } }.ToList().AsReadOnly() },
            new TextBlock{ Text="A2: Furkan iltaş", PageNumber=1, OrderIndex=4, Type=TextBlockType.Paragraph, Properties=new Dictionary<string,object>{{"CellReference","A2"}}, Spans=new[]{ new TextSpan{ StartIndex=0, Length="A2: Furkan iltaş".Length, Text="A2: Furkan iltaş" } }.ToList().AsReadOnly() },
            new TextBlock{ Text="C2: 123455", PageNumber=1, OrderIndex=5, Type=TextBlockType.Paragraph, Properties=new Dictionary<string,object>{{"CellReference","C2"}}, Spans=new[]{ new TextSpan{ StartIndex=0, Length="C2: 123455".Length, Text="C2: 123455" } }.ToList().AsReadOnly() },
            new TextBlock{ Text="D2: 123213123171213", PageNumber=1, OrderIndex=6, Type=TextBlockType.Paragraph, Properties=new Dictionary<string,object>{{"CellReference","D2"}}, Spans=new[]{ new TextSpan{ StartIndex=0, Length="D2: 123213123171213".Length, Text="D2: 123213123171213" } }.ToList().AsReadOnly() },
            new TextBlock{ Text="E2: 3479238497238912jwsa1", PageNumber=1, OrderIndex=7, Type=TextBlockType.Paragraph, Properties=new Dictionary<string,object>{{"CellReference","E2"}}, Spans=new[]{ new TextSpan{ StartIndex=0, Length="E2: 3479238497238912jwsa1".Length, Text="E2: 3479238497238912jwsa1" } }.ToList().AsReadOnly() },
        };
        var text = string.Join("\n", blocks.Select(b=>b.Text));
        var page = new DocumentPage(100,100,1,72,72){ Text=text, PageNumber=1, TextBlocks=blocks.AsReadOnly() };
        var doc = new Document{ Name="test.xlsx", Format=DF.Xlsx, Pages=new[]{page}, Metadata=new DocumentMetadata()};
        var res = _engine.Detect(doc).Value;
        res.Should().Contain(d=>d.Type==DetectionType.Secret && d.Value=="123455");
        res.Should().Contain(d=>d.Type==DetectionType.Secret && d.Value=="123213123171213");
        res.Should().Contain(d=>d.Type==DetectionType.Secret && d.Value=="3479238497238912jwsa1");
    }
}
