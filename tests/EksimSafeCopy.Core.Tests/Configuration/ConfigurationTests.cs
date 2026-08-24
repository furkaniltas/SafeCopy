using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace EksimSafeCopy.Core.Tests.Configuration;

public class ConfigurationTests
{
    [Fact]
    public void Memory_Provider_Get_Returns_Value()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Key1"] = "Value1",
                ["Key2"] = "Value2"
            })
            .Build();
        
        config["Key1"].Should().Be("Value1");
        config["Key2"].Should().Be("Value2");
    }
    
    [Fact]
    public void Get_With_Default_Returns_Default_When_Missing()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Existing"] = "Value"
            })
            .Build();
        
        config.GetValue<string>("Missing", "Default").Should().Be("Default");
    }
    
    [Fact]
    public void Get_Without_Default_Returns_Null_When_Missing()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Existing"] = "Value"
            })
            .Build();
        
        var value = config.GetValue<string>("Missing");
        
        value.Should().BeNull();
    }
    
    [Fact]
    public void Section_Access_Works()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Section:Key1"] = "Value1",
                ["Section:Key2"] = "Value2",
                ["Other:Key"] = "Other"
            })
            .Build();
        
        var section = config.GetSection("Section");
        section["Key1"].Should().Be("Value1");
        section["Key2"].Should().Be("Value2");
    }
    
    [Fact]
    public void Nested_Sections_Work()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Parent:Child:GrandChild"] = "DeepValue"
            })
            .Build();
        
        config["Parent:Child:GrandChild"].Should().Be("DeepValue");
    }
    
    [Fact]
    public void GetChildren_Returns_Immediate_Children()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Section1:Key"] = "Value1",
                ["Section2:Key"] = "Value2",
                ["Section1:Sub:Key"] = "SubValue"
            })
            .Build();
        
        var children = config.GetChildren().Select(c => c.Key).ToList();
        
        children.Should().Contain("Section1");
        children.Should().Contain("Section2");
        children.Should().NotContain("Sub");
    }
    
    [Fact]
    public void Type_Conversion_Works()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["IntValue"] = "42",
                ["BoolValue"] = "true",
                ["DoubleValue"] = "3.14",
                ["TimeSpanValue"] = "00:05:00"
            })
            .Build();
        
        config.GetValue<int>("IntValue").Should().Be(42);
        config.GetValue<bool>("BoolValue").Should().BeTrue();
        config.GetValue<double>("DoubleValue").Should().BeApproximately(3.14, 0.001);
        config.GetValue<TimeSpan>("TimeSpanValue").Should().Be(TimeSpan.FromMinutes(5));
    }
}