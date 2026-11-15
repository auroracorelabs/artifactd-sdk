using ArtifactD.ClientFeedback;

namespace ArtifactD.ClientFeedback.Tests;

public sealed class HostOperatingSystemTests
{
    [Fact]
    public void PlatformName_IsClosedSnakeCaseSet()
    {
        string name = HostOperatingSystem.PlatformName();
        Assert.True(
            name is "windows" or "linux" or "macos" or "freebsd" or "unknown",
            "os must be a snake_case platform name.");
    }

    [Fact]
    public void VersionLabel_IsNonEmptyAndWithinLimit()
    {
        string version = HostOperatingSystem.VersionLabel();
        Assert.False(string.IsNullOrWhiteSpace(version));
        Assert.True(version.Length <= HostOperatingSystem.MaxOsVersionChars);
    }

    [Fact]
    public void ParseOsReleasePretty_PrefersPrettyName()
    {
        string? label = HostOperatingSystem.ParseOsReleasePretty(
            """
            NAME="Debian GNU/Linux"
            VERSION_ID="13"
            VERSION="13 (trixie)"
            PRETTY_NAME="Debian GNU/Linux 13 (trixie)"
            """);
        Assert.Equal("Debian GNU/Linux 13 (trixie)", label);
    }

    [Fact]
    public void ParseOsReleasePretty_FallsBackToVersionId()
    {
        string? label = HostOperatingSystem.ParseOsReleasePretty("VERSION_ID=\"13\"\n");
        Assert.Equal("13", label);
    }
}
