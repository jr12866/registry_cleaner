using RegistryCleaner.Core.Safety.PathNormalization;

namespace RegistryCleaner.Core.Tests;

/// <summary>
/// Tests for <see cref="RegistryPathNormalizer"/>, the canonicalisation that every
/// protected-path comparison depends on.
/// </summary>
public sealed class RegistryPathNormalizerTests
{
    [Theory]
    [InlineData(@"SOFTWARE\App", "SOFTWARE\\APP")]
    [InlineData(@"software\app", "SOFTWARE\\APP")]
    [InlineData(@"SOFTWARE/App/", "SOFTWARE\\APP")]
    [InlineData(@"SOFTWARE\App\", "SOFTWARE\\APP")]
    [InlineData(@"SOFTWARE\\App", "SOFTWARE\\APP")]
    [InlineData(@"SOFTWARE/./App", "SOFTWARE\\APP")]
    [InlineData(@"SOFTWARE\App\..\Other", "SOFTWARE\\OTHER")]
    [InlineData("", "")]
    public void Normalize_ProducesCanonicalForm(string input, string expected)
    {
        Assert.Equal(expected, RegistryPathNormalizer.Normalize(input));
    }

    [Fact]
    public void Normalize_Null_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, RegistryPathNormalizer.Normalize(null));
    }

    [Fact]
    public void Normalize_DotDotCannotEscapeAboveRoot()
    {
        var result = RegistryPathNormalizer.Normalize(@"..\..\SOFTWARE\App");

        Assert.Equal("SOFTWARE\\APP", result);
    }

    [Theory]
    [InlineData(@"SYSTEM\Services", @"SYSTEM\Services", true)]
    [InlineData(@"system\services\Tcpip", @"SYSTEM\Services", true)]
    [InlineData(@"SYSTEM\Services\Tcpip", @"SYSTEM\Services\Tcpip", true)]
    [InlineData(@"SYSTEM\ServicesExtra", @"SYSTEM\Services", false)]
    [InlineData(@"SYSTEM\Other", @"SYSTEM\Services", false)]
    [InlineData(@"SYSTEM", @"SYSTEM\Services", false)]
    public void IsUnder_IdentifiesSubtreeMembership(
        string candidate,
        string ancestor,
        bool expected)
    {
        Assert.Equal(expected, RegistryPathNormalizer.IsUnder(candidate, ancestor));
    }

    [Fact]
    public void Ancestors_ReturnsEveryLevelFromTheTop()
    {
        var ancestors = RegistryPathNormalizer.Ancestors(@"A\B\C");

        Assert.Equal(["A", @"A\B", @"A\B\C"], ancestors);
    }

    [Fact]
    public void Ancestors_OfEmptyPath_IsEmpty()
    {
        Assert.Empty(RegistryPathNormalizer.Ancestors(string.Empty));
    }
}
