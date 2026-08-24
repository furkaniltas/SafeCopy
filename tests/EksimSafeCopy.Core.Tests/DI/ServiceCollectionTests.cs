using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EksimSafeCopy.Core.Tests.DI;

public class ServiceCollectionTests
{
    [Fact]
    public void Singleton_Registration_Resolves_Same_Instance()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITestService, TestService>();
        
        var provider = services.BuildServiceProvider();
        
        var instance1 = provider.GetRequiredService<ITestService>();
        var instance2 = provider.GetRequiredService<ITestService>();
        
        instance1.Should().BeSameAs(instance2);
    }
    
    [Fact]
    public void Transient_Registration_Resolves_Different_Instances()
    {
        var services = new ServiceCollection();
        services.AddTransient<ITestService, TestService>();
        
        var provider = services.BuildServiceProvider();
        
        var instance1 = provider.GetRequiredService<ITestService>();
        var instance2 = provider.GetRequiredService<ITestService>();
        
        instance1.Should().NotBeSameAs(instance2);
    }
    
    [Fact]
    public void Scoped_Registration_Resolves_Same_Instance_Within_Scope()
    {
        var services = new ServiceCollection();
        services.AddScoped<ITestService, TestService>();
        
        var provider = services.BuildServiceProvider();
        
        using (var scope = provider.CreateScope())
        {
            var instance1 = scope.ServiceProvider.GetRequiredService<ITestService>();
            var instance2 = scope.ServiceProvider.GetRequiredService<ITestService>();
            instance1.Should().BeSameAs(instance2);
        }
        
        using (var scope2 = provider.CreateScope())
        {
            var instance3 = scope2.ServiceProvider.GetRequiredService<ITestService>();
            instance3.Should().NotBeSameAs(provider.GetRequiredService<ITestService>());
        }
    }
    
    [Fact]
    public void Factory_Registration_Works()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITestService>(_ => new TestService { Value = "factory" });
        
        var provider = services.BuildServiceProvider();
        
        var instance = provider.GetRequiredService<ITestService>();
        instance.Value.Should().Be("factory");
    }
    
    [Fact]
    public void Instance_Registration_Works()
    {
        var services = new ServiceCollection();
        var instance = new TestService { Value = "instance" };
        services.AddSingleton<ITestService>(instance);
        
        var provider = services.BuildServiceProvider();
        
        var resolved = provider.GetRequiredService<ITestService>();
        resolved.Should().BeSameAs(instance);
    }
    
    [Fact]
    public void Missing_Registration_Throws_On_GetRequiredService()
    {
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();
        
        var act = () => provider.GetRequiredService<ITestService>();
        
        act.Should().Throw<InvalidOperationException>();
    }
    
    [Fact]
    public void GetService_Returns_Null_For_Missing_Registration()
    {
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();
        
        var result = provider.GetService<ITestService>();
        
        result.Should().BeNull();
    }
    
    [Fact]
    public void GetServices_Returns_All_Registrations()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITestService, TestService1>();
        services.AddSingleton<ITestService, TestService2>();
        
        var provider = services.BuildServiceProvider();
        
        var instances = provider.GetServices<ITestService>().ToList();
        
        instances.Should().HaveCount(2);
    }
    
    public interface ITestService { string Value { get; set; } }
    public class TestService : ITestService { public string Value { get; set; } = "default"; }
    public class TestService1 : ITestService { public string Value { get; set; } = "1"; }
    public class TestService2 : ITestService { public string Value { get; set; } = "2"; }
}