using System.Text.Json;

namespace ArtifactD.ClientFeedback.Tests;

public sealed class CorrelationIdTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "corr-" + Guid.NewGuid().ToString("N"));

    private ClientFeedbackClient NewClient(Func<string?>? provider = null) => new(new ClientFeedbackOptions
    {
        BaseURL = "https://feed.test", Username = "u", Token = "t", PackageName = "my-app", Version = "1.0.0",
        QueueDirectory = _dir, CorrelationIdProvider = provider,
    });

    private string? Pending(ClientFeedbackClient client)
    {
        var payload = Assert.Single(client.Store.ListPending());
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(payload, ClientFeedbackJsonContext.Default.ClientFeedbackPayload));
        return doc.RootElement.TryGetProperty("correlation_id", out var v) ? v.GetString() : null;
    }

    [Fact]
    public void ExplicitIdIsSerializedAsCorrelationId()
    {
        using var client = NewClient();
        client.ReportError("op", new InvalidOperationException("x"), correlationId: "req-42");
        Assert.Equal("req-42", Pending(client));
    }

    [Fact]
    public void AmbientScopeIsPickedUpAndRestored()
    {
        using var client = NewClient();
        using (ClientFeedbackCorrelation.BeginScope("outer"))
        {
            using (ClientFeedbackCorrelation.BeginScope("inner")) Assert.Equal("inner", ClientFeedbackCorrelation.Current);
            Assert.Equal("outer", ClientFeedbackCorrelation.Current);
            client.ReportCrash(new InvalidOperationException("x"), "task");
        }
        Assert.Null(ClientFeedbackCorrelation.Current);
        Assert.Equal("outer", Pending(client));
    }

    [Fact]
    public void ProviderIsFallbackAndFaultsOrInvalidValuesAreIgnored()
    {
        using (var client = NewClient(() => "from-provider"))
        {
            client.ReportError("op");
            Assert.Equal("from-provider", Pending(client));
        }
        Directory.Delete(_dir, true);
        using (var client = NewClient(() => "bad id"))
        {
            client.ReportError("op");
            Assert.Null(Pending(client));
        }
        Directory.Delete(_dir, true);
        using (var client = NewClient(() => throw new InvalidOperationException()))
        {
            client.ReportError("op");
            Assert.Null(Pending(client));
        }
    }

    [Fact]
    public void SameErrorUnderDifferentIdsIsNotDeduplicated()
    {
        using var client = NewClient();
        Assert.NotEqual(Guid.Empty, client.ReportError("op", null, "a"));
        Assert.NotEqual(Guid.Empty, client.ReportError("op", null, "b"));
        Assert.Equal(Guid.Empty, client.ReportError("op", null, "b"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("has space")]
    public void InvalidExplicitIdThrows(string id)
    {
        using var client = NewClient();
        Assert.Throws<ArgumentException>(() => client.ReportError("op", null, id));
        Assert.Throws<ArgumentException>(() => ClientFeedbackCorrelation.BeginScope(id));
    }

    [Fact]
    public void NewIdIsValid() => Assert.NotNull(ClientFeedbackCorrelation.Normalize(ClientFeedbackCorrelation.NewId()));

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }
}
