using Kuroko.Core.Diagnostics;

namespace Kuroko.Tests;

public class AppVersionTests
{
    [Theory]
    [InlineData("1.0.0+3f2a9c1d4e5b6a7980f1e2d3c4b5a69788776655", "1.0.0+3f2a9c1")]
    [InlineData("1.0.0+3f2a9c1", "1.0.0+3f2a9c1")]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("1.2.3-beta.1", "1.2.3-beta.1")]
    [InlineData(null, "?")]
    [InlineData("", "?")]
    public void Formats_the_informational_version(string? input, string expected)
        => Assert.Equal(expected, AppVersion.Format(input));
}
