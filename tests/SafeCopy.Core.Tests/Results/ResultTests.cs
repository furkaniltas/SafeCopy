using SafeCopy.Core.Models;
using FluentAssertions;
using Xunit;

namespace SafeCopy.Core.Tests.Results;

public class ResultTests
{
    [Fact]
    public void Success_Result_Has_Value()
    {
        var result = Result<int>.Success(42);
        
        result.IsSuccess.Should().BeTrue();
        result.IsFailure.Should().BeFalse();
        result.Value.Should().Be(42);
    }
    
    [Fact]
    public void Failure_Result_Has_Error()
    {
        var error = Error.Validation("Invalid input");
        var result = Result<int>.Failure(error);
        
        result.IsSuccess.Should().BeFalse();
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(error);
    }
    
    [Fact]
    public void Accessing_Value_On_Failure_Throws()
    {
        var result = Result<int>.Failure(Error.Validation("fail"));
        
        var act = () => _ = result.Value;
        
        act.Should().Throw<InvalidOperationException>();
    }
    
    [Fact]
    public void Accessing_Error_On_Success_Throws()
    {
        var result = Result<int>.Success(42);
        
        var act = () => _ = result.Error;
        
        act.Should().Throw<InvalidOperationException>();
    }
    
    [Fact]
    public void Implicit_Conversion_From_Value()
    {
        Result<int> result = 42;
        
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(42);
    }
    
    [Fact]
    public void Implicit_Conversion_From_Error()
    {
        Result<int> result = Error.Validation("fail");
        
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("VALIDATION_ERROR");
    }
    
    [Fact]
    public void Match_Executes_Correct_Branch()
    {
        var success = Result<int>.Success(42);
        var failure = Result<int>.Failure(Error.Validation("fail"));
        
        var successResult = success.Match(v => v * 2, e => 0);
        var failureResult = failure.Match(v => v * 2, e => 0);
        
        successResult.Should().Be(84);
        failureResult.Should().Be(0);
    }
    
    [Fact]
    public void Match_Action_Executes_Correct_Branch()
    {
        var success = Result<int>.Success(42);
        var failure = Result<int>.Failure(Error.Validation("fail"));
        
        int successValue = 0, failureValue = 0;
        
        success.Match(v => successValue = v, e => failureValue = -1);
        failure.Match(v => successValue = -1, e => failureValue = 1);
        
        successValue.Should().Be(42);
        failureValue.Should().Be(1);
    }
    
    [Fact]
    public void Result_Non_Generic_Success()
    {
        var result = Result.Success();
        
        result.IsSuccess.Should().BeTrue();
        result.IsFailure.Should().BeFalse();
    }
    
    [Fact]
    public void Result_Non_Generic_Failure()
    {
        var result = Result.Failure(Error.Validation("fail"));
        
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("VALIDATION_ERROR");
    }
    
    [Fact]
    public void Error_Factory_Methods_Create_Correct_Codes()
    {
        Error.Validation("msg").Code.Should().Be("VALIDATION_ERROR");
        Error.NotFound("msg").Code.Should().Be("NOT_FOUND");
        Error.Conflict("msg").Code.Should().Be("CONFLICT");
        Error.Internal("msg").Code.Should().Be("INTERNAL_ERROR");
        Error.Unauthorized("msg").Code.Should().Be("UNAUTHORIZED");
        Error.Forbidden("msg").Code.Should().Be("FORBIDDEN");
        Error.Timeout("msg").Code.Should().Be("TIMEOUT");
        Error.Cancelled("msg").Code.Should().Be("CANCELLED");
        Error.IoError("msg").Code.Should().Be("IO_ERROR");
        Error.FormatError("msg").Code.Should().Be("FORMAT_ERROR");
        Error.SecurityError("msg").Code.Should().Be("SECURITY_ERROR");
    }
    
    [Fact]
    public void Error_Equality_Works()
    {
        var e1 = Error.Validation("same");
        var e2 = Error.Validation("same");
        var e3 = Error.Validation("different");
        
        e1.Should().Be(e2);
        e1.Should().NotBe(e3);
    }
    
    [Fact]
    public void Result_With_Complex_Type()
    {
        var dto = new TestDto { Id = 1, Name = "Test" };
        var result = Result<TestDto>.Success(dto);
        
        result.Value.Id.Should().Be(1);
        result.Value.Name.Should().Be("Test");
    }
    
    public class TestDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }
}