using System.Text.Json;

namespace ArtifactD.ClientFeedback.Tests;

public sealed class SharedContractTests
{
    [Fact]
    public void SharedFeatureRequest_RoundTripsWithoutChangingWireFields()
    {
        string json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "feature-request.json"));
        var payload = JsonSerializer.Deserialize(json, ClientFeedbackJsonContext.Default.ClientFeedbackPayload)!;
        Assert.Equal(ClientFeedbackKind.FeatureRequest, payload.Kind);
        Assert.Equal("123.456", payload.Ga4ClientId);
        Assert.Equal("Export CSV", payload.Operation);
        Assert.Equal("Let me take my data anywhere.", payload.Message);
        Assert.Equal("my-app", payload.PackageName);
        string serialized = JsonSerializer.Serialize(payload, ClientFeedbackJsonContext.Default.ClientFeedbackPayload);
        using var original = JsonDocument.Parse(json);
        using var actual = JsonDocument.Parse(serialized);
        foreach (var property in original.RootElement.EnumerateObject())
        {
            var value = actual.RootElement.GetProperty(property.Name);
            if (property.Name == "occurred_at_utc")
                Assert.Equal(property.Value.GetDateTime(), value.GetDateTime());
            else
                Assert.Equal(property.Value.ToString(), value.ToString());
        }
    }

    private static readonly string[] ReportKeys =
    [
        "report_id", "kind", "package_name", "version", "architecture", "format", "runtime", "os", "os_version",
        "occurred_at_utc", "operation", "exception_type", "message", "stack_trace", "source",
    ];

    private static string Fixture(string path) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", path));

    public static TheoryData<string> ReportFixtures() => new()
    {
        "reports/crash.json", "reports/error.json", "reports/bugfix-request.json",
        "reports/feature-request.json", "google/crash-with-ga4.json",
    };

    [Theory]
    [MemberData(nameof(ReportFixtures))]
    public void ReportFixtures_MatchTheWireContractAndRoundTripUnchanged(string path)
    {
        string json = Fixture(path);
        using var original = JsonDocument.Parse(json);
        var expectedKeys = path.StartsWith("google/", StringComparison.Ordinal)
            ? ReportKeys.Append("ga4_client_id").ToArray() : ReportKeys;
        Assert.Equal(expectedKeys, original.RootElement.EnumerateObject().Select(p => p.Name).ToArray());

        var payload = JsonSerializer.Deserialize(json, ClientFeedbackJsonContext.Default.ClientFeedbackPayload)!;
        if (payload.Kind is ClientFeedbackKind.BugfixRequest or ClientFeedbackKind.FeatureRequest)
        {
            Assert.Equal("user", payload.Source);
            Assert.InRange(payload.Operation!.Length, 1, 128);
            Assert.InRange(payload.Message!.Length, 1, 1024);
        }
        using var actual = JsonDocument.Parse(
            JsonSerializer.Serialize(payload, ClientFeedbackJsonContext.Default.ClientFeedbackPayload));
        Assert.Equal(expectedKeys, actual.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => Array.IndexOf(expectedKeys, n)).ToArray());
        foreach (var property in original.RootElement.EnumerateObject())
        {
            var value = actual.RootElement.GetProperty(property.Name);
            if (property.Name == "occurred_at_utc")
                Assert.Equal(property.Value.GetDateTime(), value.GetDateTime());
            else
                Assert.Equal(property.Value.ToString(), value.ToString());
        }
    }

    [Theory]
    [InlineData("usage/app-open.json")]
    [InlineData("usage/update-check.json")]
    [InlineData("google/app-open-with-ga4.json")]
    public void UsageFixtures_ListExactlyTheKeysTheSdkSends(string path)
    {
        using var fixture = JsonDocument.Parse(Fixture(path));
        using var plain = JsonDocument.Parse(Fixture("usage/app-open.json"));
        Assert.Equal(
            plain.RootElement.EnumerateObject().Select(p => p.Name),
            fixture.RootElement.EnumerateObject().Select(p => p.Name));
        Assert.DoesNotContain(fixture.RootElement.EnumerateObject(), p => p.Name == "ga4_client_id");
    }

    [Fact]
    public void EngagementFixtures_AddOnlyTheDurationField()
    {
        using var plain = JsonDocument.Parse(Fixture("usage/app-open.json"));
        using var engaged = JsonDocument.Parse(Fixture("usage/app-open-with-engagement.json"));
        using var google = JsonDocument.Parse(Fixture("google/app-open-with-ga4-and-engagement.json"));
        var names = engaged.RootElement.EnumerateObject().Select(p => p.Name).ToArray();
        Assert.Equal(names, google.RootElement.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal(
            new[] { "engagement_time_msec" },
            names.Except(plain.RootElement.EnumerateObject().Select(p => p.Name)).ToArray());
    }

    [Fact]
    public async Task ReporterSendsTheFixtureKeys_WithAndWithoutEngagement()
    {
        var bodies = new List<string>();
        using var handler = new RecordingHandler(bodies);
        using var reporter = new ClientUsageReporter(new ClientUsageReportingOptions
        {
            ResolveCredentials = () => new("https://feed.example", "user", "token"),
            PackageName = "my-app", VersionProvider = () => "1.0.0", Architecture = "amd64", Format = "deb",
            HttpClient = new HttpClient(handler),
        });
        reporter.TrackAppOpen();
        reporter.TrackAppOpen(45000);
        await reporter.CompleteAsync();

        using var plain = JsonDocument.Parse(Fixture("usage/app-open.json"));
        using var engaged = JsonDocument.Parse(Fixture("usage/app-open-with-engagement.json"));
        Assert.Equal(2, bodies.Count);
        Assert.Equal(
            plain.RootElement.EnumerateObject().Select(p => p.Name),
            JsonDocument.Parse(bodies[0]).RootElement.EnumerateObject().Select(p => p.Name));
        Assert.Equal(
            engaged.RootElement.EnumerateObject().Select(p => p.Name),
            JsonDocument.Parse(bodies[1]).RootElement.EnumerateObject().Select(p => p.Name));
    }

    private sealed class RecordingHandler(List<string> bodies) : HttpMessageHandler
    {
        protected override async Task<System.Net.Http.HttpResponseMessage> SendAsync(
            System.Net.Http.HttpRequestMessage request, CancellationToken cancellationToken)
        {
            bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            return new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.NoContent);
        }
    }
}
