using EksimSafeCopy.Core.Abstractions;
using EksimSafeCopy.Core.Models;
using EksimSafeCopy.Detectors.Detection.Detectors;
using EksimSafeCopy.DocumentEngine.Security;
using EksimSafeCopy.Infrastructure;
using DF = EksimSafeCopy.Core.Abstractions.DocumentFormat;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EksimSafeCopy.Detectors.Tests.Detectors;

public class SecretDetectorTests
{
    private readonly IDetectionEngine _engine;

    public SecretDetectorTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new DocumentSecurityOptions());
        services.AddSingleton<IDocumentSecurityValidator, DocumentSecurityValidator>();
        services.AddSingleton<IFileSystem, FileSystem>();
        services.AddDetectors();
        var sp = services.BuildServiceProvider();
        _engine = sp.GetRequiredService<IDetectionEngine>();
    }

    private Document Doc(string text, DF fmt = DF.Txt)
    {
        var page = new DocumentPage(100,100,1,72,72){ Text=text, PageNumber=1, TextBlocks = new[]{ new TextBlock{ Text=text, PageNumber=1, Type=TextBlockType.Paragraph } }.ToList().AsReadOnly() };
        return new Document{ Name="test", Format=fmt, Pages=new[]{page}, Metadata=new DocumentMetadata() };
    }

    [Theory]
    [InlineData("Password: 12345")]
    [InlineData("password = 123456")]
    [InlineData("Password : MySecret123")]
    public void Detect_Password_English(string text)
    {
        var doc = Doc(text);
        var res = _engine.Detect(doc);
        res.IsSuccess.Should().BeTrue();
        res.Value.Should().Contain(d=>d.Type==DetectionType.Secret);
    }

    [Theory]
    [InlineData("Şifre: abc123")]
    [InlineData("Şifre : 12345")]
    [InlineData("Sifre: abc123")]
    public void Detect_Sifre_Turkish(string text)
    {
        var doc = Doc(text);
        var res = _engine.Detect(doc);
        res.Value.Should().Contain(d=>d.Type==DetectionType.Secret);
    }

    [Theory]
    [InlineData("Parola : MySecret123")]
    [InlineData("Parola: abc123")]
    public void Detect_Parola_Turkish(string text)
    {
        var doc = Doc(text);
        var res = _engine.Detect(doc);
        res.Value.Should().Contain(d=>d.Type==DetectionType.Secret);
    }

    [Theory]
    [InlineData("API Key: abc123XYZ")]
    [InlineData("API_KEY=abc123XYZ")]
    [InlineData("ApiKey: abc123XYZ")]
    [InlineData("API Anahtarı: abc123XYZ")]
    public void Detect_ApiKey(string text)
    {
        var doc = Doc(text);
        var res = _engine.Detect(doc);
        res.Value.Should().Contain(d=>d.Type==DetectionType.Secret);
    }

    [Theory]
    [InlineData("Secret: my-secret-value")]
    [InlineData("SECRET_KEY=my-secret-value")]
    [InlineData("Secret Key: my-secret")]
    public void Detect_Secret(string text)
    {
        var doc = Doc(text);
        var res = _engine.Detect(doc);
        res.Value.Should().Contain(d=>d.Type==DetectionType.Secret);
    }

    [Theory]
    [InlineData("Token: abc123XYZ")]
    [InlineData("Access Token: abc123XYZ")]
    [InlineData("ACCESS_TOKEN=abc123XYZ")]
    [InlineData("Refresh Token: abc123XYZ")]
    public void Detect_Token(string text)
    {
        var doc = Doc(text);
        var res = _engine.Detect(doc);
        res.Value.Should().Contain(d=>d.Type==DetectionType.Secret);
    }

    [Fact]
    public void Detect_ClientSecret()
    {
        var doc = Doc("Client Secret: abc123XYZ");
        var res = _engine.Detect(doc);
        res.Value.Should().Contain(d=>d.Type==DetectionType.Secret && d.Value=="abc123XYZ");
    }

    [Fact]
    public void Detect_PrivateKey_Pem()
    {
        var pem = "-----BEGIN PRIVATE KEY-----\nMIIEvQIBADANBgkqhkiG9w0BAQEFAASCBKcwggSjAgEAAoIBAQD...\n-----END PRIVATE KEY-----";
        var doc = Doc($"Private Key: {pem}");
        var res = _engine.Detect(doc);
        res.Value.Should().Contain(d=>d.Type==DetectionType.Secret);
    }

    [Fact]
    public void Detect_PrivateKey_Label()
    {
        var doc = Doc("Private Key: abc123XYZ");
        var res = _engine.Detect(doc);
        res.Value.Should().Contain(d=>d.Type==DetectionType.Secret);
    }

    [Fact]
    public void Detect_DatabasePassword()
    {
        var doc = Doc("Database Password: myDbPass123");
        var res = _engine.Detect(doc);
        res.Value.Should().Contain(d=>d.Type==DetectionType.Secret);
    }

    [Fact]
    public void Detect_ConnectionString()
    {
        var doc = Doc("Connection String: Server=myServer;Database=myDB;User Id=myUser;Password=myPass123;");
        var res = _engine.Detect(doc);
        res.Value.Should().Contain(d=>d.Type==DetectionType.Secret);
    }

    [Fact]
    public void Detect_EnvPassword()
    {
        var doc = Doc("PASSWORD=abc123XYZ");
        var res = _engine.Detect(doc);
        res.Value.Should().Contain(d=>d.Type==DetectionType.Secret);
    }

    [Theory]
    [InlineData("Key Management")]
    [InlineData("Primary Key")]
    [InlineData("Foreign Key")]
    [InlineData("API Documentation")]
    [InlineData("Token Validation")]
    [InlineData("Secret Management")]
    [InlineData("Password Policy")]
    [InlineData("Keyboard")]
    public void Detect_FalsePositive_NotDetected(string text)
    {
        var doc = Doc(text);
        var res = _engine.Detect(doc);
        res.Value.Should().NotContain(d=>d.Type==DetectionType.Secret);
    }

    [Fact]
    public void Detect_Secret_Xlsx_Txt_Docx_Udf_AllFormats()
    {
        foreach(var fmt in new[]{DF.Txt, DF.Docx, DF.Xlsx, DF.Udf}){
            var doc = Doc("Password: 111234", fmt);
            var res = _engine.Detect(doc);
            res.Value.Should().Contain(d=>d.Type==DetectionType.Secret, $"format {fmt} should detect Secret");
        }
    }

    // Permanent regression for real-world false-negative (Phase 5)
    [Theory]
    [InlineData("Secret abcdefgh123")]
    [InlineData("Secret=abcdefgh123")]
    [InlineData("SECRET=abcdefgh123")]
    [InlineData("API_KEY=abcdefgh123")]
    [InlineData("CLIENT_SECRET=abcdefgh123")]
    [InlineData("FALCON_CLIENT_SECRET=abcdefgh123")]
    [InlineData("$env:FALCON_CLIENT_SECRET=\"abcdefgh123\"")]
    [InlineData("export CLIENT_SECRET=\"abcdefgh123\"")]
    [InlineData("\"client_secret\": \"abcdefgh123\"")]
    [InlineData("PASSWORD=abcdefgh123")]
    [InlineData("password: abcdefgh123")]
    [InlineData("Şifre: abcdefgh123")]
    public void Detect_RealWorld_Positive_ShouldBeSecret(string text)
    {
        var doc = Doc(text);
        var res = _engine.Detect(doc);
        res.Value.Should().Contain(d=>d.Type==DetectionType.Secret, $"{text} should be Secret");
    }

    [Theory]
    [InlineData("Client ID abc123456789")]
    [InlineData("FALCON_CLIENT_ID=\"abc123456789\"")]
    [InlineData("Primary Key: 12345")]
    [InlineData("Foreign Key: 12345")]
    [InlineData("Record Key: 12345")]
    [InlineData("Key ID: 12345")]
    [InlineData("Key Management")]
    [InlineData("API Documentation")]
    [InlineData("API Description")]
    [InlineData("API Endpoint")]
    [InlineData("Token Validation")]
    [InlineData("Password Policy")]
    [InlineData("Secret Management")]
    [InlineData("Key Information")]
    public void Detect_RealWorld_Negative_ShouldNotBeSecret(string text)
    {
        var doc = Doc(text);
        var res = _engine.Detect(doc);
        res.Value.Should().NotContain(d=>d.Type==DetectionType.Secret, $"{text} should not be Secret");
    }

    [Fact]
    public void Detect_RealWorld_SecretValue_28Dlvy()
    {
        // Synthetic, not production credential - regression for whitespace-separated Secret
        var doc = Doc("Secret 28Dlvyob4OT6BjZEJwh7Gf3MC915tNQpKgXmu0Li");
        var res = _engine.Detect(doc);
        res.Value.Should().ContainSingle(d=>d.Type==DetectionType.Secret && d.Value=="28Dlvyob4OT6BjZEJwh7Gf3MC915tNQpKgXmu0Li");
    }

    [Fact]
    public void Detect_Env_FalconClientSecret_Quoted()
    {
        var doc = Doc("$env:FALCON_CLIENT_SECRET=\"28Dlvyob4OT6BjZEJwh7Gf3MC915tNQpKgXmu0Li\"");
        var res = _engine.Detect(doc);
        res.Value.Should().ContainSingle(d=>d.Type==DetectionType.Secret && d.Value=="28Dlvyob4OT6BjZEJwh7Gf3MC915tNQpKgXmu0Li");
    }

    [Fact]
    public void Detect_NoDuplicate_ForSameSpan()
    {
        var text = "Secret 28Dlvyob4OT6BjZEJwh7Gf3MC915tNQpKgXmu0Li";
        var doc = Doc(text);
        var res = _engine.Detect(doc);
        var secrets = res.Value.Where(d=>d.Type==DetectionType.Secret).ToList();
        secrets.Should().ContainSingle(d=>d.Value=="28Dlvyob4OT6BjZEJwh7Gf3MC915tNQpKgXmu0Li");
        // Ensure no duplicate overlapping for same occurrence
        var distinctSpans = secrets.Select(d=>d.TextSpan?.StartIndex).Distinct().Count();
        distinctSpans.Should().Be(secrets.Count);
    }
}
