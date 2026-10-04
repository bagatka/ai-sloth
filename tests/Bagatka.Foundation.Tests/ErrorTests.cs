using Xunit;

namespace Bagatka.Foundation.Tests;

public sealed class ErrorTests
{
    [Fact]
    public void Validation_reports_the_rejected_field()
    {
        Error error = Error.Validation("displayName", "Must be 1 to 100 characters.");

        Assert.Equal(ErrorKind.Validation, error.Kind);
        Assert.Equal("validation", error.Code);
        FieldError field = Assert.Single(error.Fields);
        Assert.Equal(new FieldError("displayName", "Must be 1 to 100 characters."), field);
    }

    [Fact]
    public void Only_validation_errors_carry_field_errors()
    {
        Error conflict = Error.Conflict("users.deactivated", "User is deactivated.");

        Assert.Equal(ErrorKind.Conflict, conflict.Kind);
        Assert.Equal("users.deactivated", conflict.Code);
        Assert.Empty(conflict.Fields);
    }
}
