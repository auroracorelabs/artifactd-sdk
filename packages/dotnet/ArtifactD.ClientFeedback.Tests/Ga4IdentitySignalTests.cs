using System.Net;
using System.Text.Json;

namespace ArtifactD.ClientFeedback.Tests;

public sealed class Ga4IdentitySignalTests
{
    [Theory]
    [InlineData("123.456")]
    [InlineData(null)]
    [InlineData("bad\r\nheader")]
    public async Task UsageSignalsUseOptionalConsentedIdentityWithoutBlocking(string? id)
    {
        using var handler = new Capture();
        using var http = new HttpClient(handler);
        using var usage = new ClientUsageReporter(new ClientUsageReportingOptions
        {
            ResolveCredentials = () => new("https://feed.test", "user", "token"),
            Ga4ClientIdProvider = () => id,
            PackageName = "my-app", VersionProvider = () => "1.4.1", Architecture = "amd64", Format = "deb", HttpClient = http,
        });
        usage.TrackUpdateCheck();
        await usage.CompleteAsync();
        Assert.Single(handler.Bodies);
        using var body = JsonDocument.Parse(handler.Bodies[0]);
        Assert.Equal("update_check", body.RootElement.GetProperty("event_name").GetString());
        Assert.Null(handler.Ids[0]); // the identity travels in the JSON body only
        Assert.Equal("my-app", body.RootElement.GetProperty("package_name").GetString());
        if (id == "123.456") Assert.Equal(id, body.RootElement.GetProperty("client_id").GetString());
    }

    [Fact]
    public async Task FaultyOptionalIdentityProviderNeverPreventsDelivery()
    {
        using var handler = new Capture();
        using var http = new HttpClient(handler);
        using var usage = new ClientUsageReporter(new ClientUsageReportingOptions
        {
            ResolveCredentials = () => new("https://feed.test", "user", "token"),
            Ga4ClientIdProvider = () => throw new InvalidOperationException(),
            PackageName = "my-app", VersionProvider = () => "1.4.1", Architecture = "amd64", Format = "deb", HttpClient = http,
        });
        usage.TrackAppOpen();
        await usage.CompleteAsync();
        Assert.Single(handler.Bodies);
        Assert.Null(handler.Ids[0]);
    }

    [Fact]
    public async Task CrashAndFeedbackPreserveTheirIdentityAcrossQueueRetryAndLaterLogin()
    {
        string directory = Path.Combine(Path.GetTempPath(), "ga4-report-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var handler = new Capture();
            using var http = new HttpClient(handler);
            string? id = "123.456";
            ClientUsageCredentials? credentials = null;
            using var reporter = new ClientFeedbackClient(new ClientFeedbackOptions
            {
                ResolveCredentials = () => credentials, Ga4ClientIdProvider = () => id,
                PackageName = "sample-app", Version = "1.4.1", QueueDirectory = directory, HttpClient = http,
            });
            reporter.ReportCrash(new InvalidOperationException("crash"), "test");
            reporter.ReportFeatureRequest("feature", "description");
            reporter.ReportBugfixRequest("fix", "description");
            reporter.ReportError("update_check");
            await reporter.FlushPendingAsync();
            Assert.Empty(handler.Bodies);
            Assert.Equal(4, reporter.Store.ListPending().Count);
            credentials = new("https://feed.test", "current-user", "current-token");
            id = "789.012";
            await reporter.FlushPendingAsync();
            Assert.Equal(4, handler.Bodies.Count);
            Assert.All(handler.Ids, Assert.Null); // never sent as a header
            Assert.All(handler.Bodies, body =>
            {
                using var json = JsonDocument.Parse(body);
                Assert.Equal("123.456", json.RootElement.GetProperty("ga4_client_id").GetString());
            });
            Assert.Empty(reporter.Store.ListPending());
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("123")]
    [InlineData(".123")]
    [InlineData("123.")]
    [InlineData("1.2.3")]
    [InlineData("/1.2")]
    [InlineData(":1.2")]
    public void MalformedIdentityIsIgnored(string? id) => Assert.Null(Ga4ClientIdentity.Normalize(id));

    [Fact]
    public void IdentityBoundaryAndProviderFailureAreNonblocking()
    {
        string maximum = new string('1', 126) + ".2";
        Assert.Equal(maximum, Ga4ClientIdentity.Normalize(maximum));
        Assert.Null(Ga4ClientIdentity.Normalize(maximum + "3"));
        Assert.Null(Ga4ClientIdentity.Resolve(null));
        Assert.Null(Ga4ClientIdentity.Resolve(() => throw new InvalidOperationException()));
    }

    private sealed class Capture : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];
        public List<string?> Ids { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Ids.Add(request.Headers.TryGetValues("X-GA4-Client-Id", out var ids) ? ids.Single() : null);
            Bodies.Add(await request.Content!.ReadAsStringAsync(token));
            return new HttpResponseMessage(HttpStatusCode.Created);
        }
    }
}
