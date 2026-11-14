using ArtifactD.ClientFeedback;

namespace ArtifactD.ClientFeedback.Tests;

public sealed class PublishedProductVersionTests
{
    [Fact]
    public void FromAssembly_ReturnsInformationalOrNumeric()
    {
        string version = PublishedProductVersion.FromAssembly(typeof(PublishedProductVersion).Assembly);
        Assert.False(string.IsNullOrWhiteSpace(version));
    }
}
