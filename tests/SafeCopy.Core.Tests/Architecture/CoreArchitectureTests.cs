using FluentAssertions;
using Xunit;

namespace SafeCopy.Core.Tests.Architecture;

public class CoreArchitectureTests
{
    [Fact]
    public void Core_Assembly_Does_Not_Reference_WPF()
    {
        var coreAssembly = typeof(SafeCopy.Core.Models.Document).Assembly;
        
        var referencedAssemblies = coreAssembly.GetReferencedAssemblies()
            .Select(a => a.Name)
            .ToList();
        
        referencedAssemblies.Should().NotContain("PresentationCore");
        referencedAssemblies.Should().NotContain("PresentationFramework");
        referencedAssemblies.Should().NotContain("WindowsBase");
        referencedAssemblies.Should().NotContain("System.Xaml");
    }
    
    [Fact]
    public void Core_Assembly_Does_Not_Reference_Windows_API()
    {
        var coreAssembly = typeof(SafeCopy.Core.Models.Document).Assembly;
        
        var referencedAssemblies = coreAssembly.GetReferencedAssemblies()
            .Select(a => a.Name)
            .ToList();
        
        referencedAssemblies.Should().NotContain(a => a.Contains("Windows", StringComparison.OrdinalIgnoreCase) && 
            (a.Contains("Forms") || a.Contains("UI") || a.Contains("Shell")));
    }
    
    [Fact]
    public void Core_Assembly_Does_Not_Reference_Concrete_Infrastructure()
    {
        var coreAssembly = typeof(SafeCopy.Core.Models.Document).Assembly;
        
        var referencedAssemblies = coreAssembly.GetReferencedAssemblies()
            .Select(a => a.Name)
            .ToList();
        
        referencedAssemblies.Should().NotContain("SafeCopy.Infrastructure");
        referencedAssemblies.Should().NotContain("SafeCopy.DocumentEngine");
        referencedAssemblies.Should().NotContain("SafeCopy.Detectors");
        referencedAssemblies.Should().NotContain("SafeCopy.Ocr");
        referencedAssemblies.Should().NotContain("SafeCopy.Renderer");
        referencedAssemblies.Should().NotContain("SafeCopy.App");
    }
    
    [Fact]
    public void Core_Assembly_Does_Not_Reference_PDF_Libraries()
    {
        var coreAssembly = typeof(SafeCopy.Core.Models.Document).Assembly;
        
        var referencedAssemblies = coreAssembly.GetReferencedAssemblies()
            .Select(a => a.Name)
            .ToList();
        
        referencedAssemblies.Should().NotContain(a => a.Contains("Pdf", StringComparison.OrdinalIgnoreCase));
        referencedAssemblies.Should().NotContain(a => a.Contains("iText", StringComparison.OrdinalIgnoreCase));
        referencedAssemblies.Should().NotContain(a => a.Contains("PdfSharp", StringComparison.OrdinalIgnoreCase));
    }
    
    [Fact]
    public void Core_Assembly_Does_Not_Reference_Office_Libraries()
    {
        var coreAssembly = typeof(SafeCopy.Core.Models.Document).Assembly;
        
        var referencedAssemblies = coreAssembly.GetReferencedAssemblies()
            .Select(a => a.Name)
            .ToList();
        
        referencedAssemblies.Should().NotContain(a => a.Contains("OpenXml", StringComparison.OrdinalIgnoreCase));
        referencedAssemblies.Should().NotContain(a => a.Contains("DocumentFormat", StringComparison.OrdinalIgnoreCase));
        referencedAssemblies.Should().NotContain(a => a.Contains("NPOI", StringComparison.OrdinalIgnoreCase));
    }
    
    [Fact]
    public void Core_Assembly_Does_Not_Reference_OCR_Libraries()
    {
        var coreAssembly = typeof(SafeCopy.Core.Models.Document).Assembly;
        
        var referencedAssemblies = coreAssembly.GetReferencedAssemblies()
            .Select(a => a.Name)
            .ToList();
        
        referencedAssemblies.Should().NotContain(a => a.Contains("Tesseract", StringComparison.OrdinalIgnoreCase));
        referencedAssemblies.Should().NotContain(a => a.Contains("Ocr", StringComparison.OrdinalIgnoreCase));
    }
    
    [Fact]
    public void Core_Only_References_Allowed_Packages()
    {
        var coreAssembly = typeof(SafeCopy.Core.Models.Document).Assembly;
        
        var referencedAssemblies = coreAssembly.GetReferencedAssemblies()
            .Select(a => a.Name)
            .ToList();
        
        // Allowed: System.*, Microsoft.Extensions.*, netstandard
        var allowedPrefixes = new[] { "System.", "Microsoft.Extensions.", "netstandard", "mscorlib" };
        
        foreach (var assembly in referencedAssemblies)
        {
            if (assembly == null) continue;
            var isAllowed = allowedPrefixes.Any(p => assembly.StartsWith(p, StringComparison.OrdinalIgnoreCase));
            isAllowed.Should().BeTrue($"Assembly {assembly} is not allowed in Core");
        }
    }
}