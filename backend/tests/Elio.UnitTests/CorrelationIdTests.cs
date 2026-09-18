using Elio.Api.Middleware;

namespace Elio.UnitTests;

public sealed class CorrelationIdTests
{
    [Theory]
    [InlineData("request-123_A", true)]
    [InlineData("", false)]
    [InlineData("unsafe\r\nheader", false)]
    [InlineData("spaces are invalid", false)]
    [InlineData("é", false)]
    public void Validates_untrusted_header(string value, bool expected) =>
        Assert.Equal(expected, CorrelationIdMiddleware.IsValid(value));

    [Fact]
    public void Rejects_oversized_header() =>
        Assert.False(CorrelationIdMiddleware.IsValid(new string('a', 65)));
}

