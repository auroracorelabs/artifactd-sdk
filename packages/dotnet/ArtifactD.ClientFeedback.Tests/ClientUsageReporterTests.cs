using System.Net;
using System.Text.Json;
using ArtifactD.ClientFeedback;

namespace ArtifactD.ClientFeedback.Tests;

public sealed class ClientUsageReporterTests
{
    [Fact]
    public async Task SendsOnlyAllowListedEventsToAuthenticatedRelay()
    {
        using var handler = new CaptureHandler();
        using var reporter = new ClientUsageReporter(Options(handler));
        reporter.TrackAppOpen();
        reporter.TrackDeviceConnectionStart("ssh");
        reporter.TrackDeviceConnectionStart("server.private.example");
        await reporter.CompleteAsync();

        Assert.Equal(2, handler.Bodies.Count);
        using var startup = JsonDocument.Parse(handler.Bodies[0]);
        using var connection = JsonDocument.Parse(handler.Bodies[1]);
        Assert.Equal("app_open", startup.RootElement.GetProperty("event_name").GetString());
        Assert.Equal("device_connection_start", connection.RootElement.GetProperty("event_name").GetString());
        Assert.Equal("ssh", connection.RootElement.GetProperty("protocol").GetString());
        Assert.Equal(
            startup.RootElement.GetProperty("client_id").GetString(),
            connection.RootElement.GetProperty("client_id").GetString());
        Assert.Equal("https://feed.example/v1/client-usage-event", handler.Path);
        Assert.Equal("Basic", handler.AuthScheme);
        Assert.DoesNotContain(handler.Bodies, body => body.Contains("api_secret") || body.Contains("private.example"));
        Assert.Equal(
            new[] { "package_name", "event_name", "protocol", "client_id", "session_id", "occurred_at_utc", "version", "architecture", "format", "os" },
            connection.RootElement.EnumerateObject().Select(p => p.Name).ToArray());
    }

    [Fact]
    public async Task SendsOptionalEngagementTimeInTheBodyAndRejectsNegativeValues()
    {
        using var handler = new CaptureHandler();
        using var reporter = new ClientUsageReporter(Options(handler));
        reporter.TrackAppOpen(2345);
        reporter.TrackUpdateCheck(-1);
        await reporter.CompleteAsync();

        Assert.Single(handler.Bodies);
        using var body = JsonDocument.Parse(handler.Bodies[0]);
        Assert.Equal(2345, body.RootElement.GetProperty("engagement_time_msec").GetInt64());
        Assert.Equal("my-app", body.RootElement.GetProperty("package_name").GetString());
        Assert.EndsWith("Z", body.RootElement.GetProperty("occurred_at_utc").GetString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SkipsMissingCredentialsAndCleartextFeeds(bool missingCredentials)
    {
        using var handler = new CaptureHandler();
        var options = Options(handler);
        if (missingCredentials)
            options.ResolveCredentials = static () => null;
        else
            options.ResolveCredentials = static () =>
                new ClientUsageCredentials("http://feed.example", "user", "token");

        using var reporter = new ClientUsageReporter(options);
        reporter.TrackAppOpen();
        await reporter.CompleteAsync();
        Assert.Empty(handler.Bodies);
    }

    [Fact]
    public async Task TransportFailureDoesNotStopFollowingEvents()
    {
        using var handler = new CaptureHandler { FailFirst = true };
        using var reporter = new ClientUsageReporter(Options(handler));
        reporter.TrackAppOpen();
        reporter.TrackDeviceConnectionStart("sftp");
        await reporter.CompleteAsync();
        Assert.Equal(2, handler.Bodies.Count);
    }

    private static ClientUsageReportingOptions Options(CaptureHandler handler) =>
        new()
        {
            ResolveCredentials = static () =>
                new ClientUsageCredentials("https://feed.example", "test-user", "test-token"),
            PackageName = "my-app",
            VersionProvider = static () => "1.2.0",
            Architecture = "amd64",
            Format = OperatingSystem.IsWindows() ? "msi" : "deb",
            HttpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) },
        };

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public List<string> Bodies { get; } = new();
        public string? Path { get; private set; }
        public string? AuthScheme { get; private set; }
        public bool FailFirst { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            Path = request.RequestUri!.ToString();
            AuthScheme = request.Headers.Authorization?.Scheme;
            if (FailFirst && Bodies.Count == 1)
                throw new HttpRequestException("offline");
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }
    }
}
