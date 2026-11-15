using System.Net;
using ArtifactD.ClientFeedback;

namespace ArtifactD.ClientFeedback.Tests;

public sealed class ClientFeedbackClientTests
{
    [Fact]
    public void Options_MissingVersion_Throws()
    {
        var options = ValidOptions(new StubHandler(HttpStatusCode.OK));
        options.Version = "";
        Assert.Throws<InvalidOperationException>(() => new ClientFeedbackClient(options));
    }

    [Fact]
    public async Task FlushPending_OnSuccess_MarksSent()
    {
        var handler = new StubHandler(HttpStatusCode.Created);
        using var reporter = new ClientFeedbackClient(ValidOptions(handler));
        Guid id = reporter.ReportError("catalogue_refresh", new InvalidOperationException("failed"));
        Assert.NotEqual(Guid.Empty, id);
        Assert.Single(reporter.Store.ListPending());

        await reporter.FlushPendingAsync();
        Assert.Empty(reporter.Store.ListPending());
        Assert.Equal(1, handler.PostCount);
    }

    [Fact]
    public async Task FlushPending_OnNetworkFailure_LeavesPending()
    {
        var handler = new StubHandler(failNetwork: true);
        using var reporter = new ClientFeedbackClient(ValidOptions(handler));
        reporter.ReportError("ssh_open", new TimeoutException("timeout"));
        await reporter.FlushPendingAsync();
        Assert.Single(reporter.Store.ListPending());
    }

    [Fact]
    public void ReportError_DedupsSameFingerprint()
    {
        var handler = new StubHandler(HttpStatusCode.OK);
        using var reporter = new ClientFeedbackClient(ValidOptions(handler));
        var ex = new InvalidOperationException("same");
        Guid first = reporter.ReportError("ipmi_attach", ex);
        Guid second = reporter.ReportError("ipmi_attach", ex);
        Assert.NotEqual(Guid.Empty, first);
        Assert.Equal(Guid.Empty, second);
        Assert.Single(reporter.Store.ListPending());
    }

    [Fact]
    public void ReportCrash_WritesKindCrashAndVersion()
    {
        var handler = new StubHandler(HttpStatusCode.OK);
        var options = ValidOptions(handler);
        options.Version = "2.0.0+ci.9";
        using var reporter = new ClientFeedbackClient(options);
        reporter.ReportCrash(new UnauthorizedAccessException("denied"), "dispatcher");
        ClientFeedbackPayload row = Assert.Single(reporter.Store.ListPending());
        Assert.Equal(ClientFeedbackKind.Crash, row.Kind);
        Assert.Equal("2.0.0+ci.9", row.Version);
        Assert.Equal("pkg-app", row.PackageName);
        Assert.Equal("dispatcher", row.Source);
        Assert.Equal(HostOperatingSystem.PlatformName(), row.Os);
        Assert.Equal(HostOperatingSystem.VersionLabel(), row.OsVersion);
        Assert.False(string.IsNullOrWhiteSpace(row.OsVersion));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UserRequest_QueuesTextAndUploads(bool bugfix)
    {
        using var reporter = new ClientFeedbackClient(ValidOptions(new StubHandler(HttpStatusCode.Created)));
        Guid id = bugfix
            ? reporter.ReportBugfixRequest(" Save fails ", " Steps to reproduce ")
            : reporter.ReportFeatureRequest(" Save fails ", " Steps to reproduce ");
        Assert.NotEqual(Guid.Empty, id);
        var payload = Assert.Single(reporter.Store.ListPending());
        Assert.Equal(bugfix ? ClientFeedbackKind.BugfixRequest : ClientFeedbackKind.FeatureRequest, payload.Kind);
        Assert.Equal("Save fails", payload.Operation);
        Assert.Equal("Steps to reproduce", payload.Message);
        Assert.Equal("user", payload.Source);
        Assert.Null(payload.ExceptionType);
        Assert.Null(payload.StackTrace);
        await reporter.FlushPendingAsync();
        Assert.Empty(reporter.Store.ListPending());
    }

    [Fact]
    public void UserRequests_DedupIncludesDescriptionAndKind()
    {
        using var reporter = new ClientFeedbackClient(ValidOptions(new StubHandler()));
        Assert.NotEqual(Guid.Empty, reporter.ReportBugfixRequest("Save", "First"));
        Assert.Equal(Guid.Empty, reporter.ReportBugfixRequest("Save", "First"));
        Assert.NotEqual(Guid.Empty, reporter.ReportBugfixRequest("Save", "Second"));
        Assert.NotEqual(Guid.Empty, reporter.ReportFeatureRequest("Save", "First"));
        Assert.Equal(3, reporter.Store.ListPending().Count);
    }

    [Theory]
    [InlineData("", "description")]
    [InlineData("title", " ")]
    public void UserRequests_RejectMissingText(string title, string description)
    {
        using var reporter = new ClientFeedbackClient(ValidOptions(new StubHandler()));
        Assert.Throws<ArgumentException>(() => reporter.ReportBugfixRequest(title, description));
        Assert.Throws<ArgumentException>(() => reporter.ReportFeatureRequest(title, description));
        Assert.Empty(reporter.Store.ListPending());
    }

    [Fact]
    public void UserRequests_RejectOversizedText()
    {
        using var reporter = new ClientFeedbackClient(ValidOptions(new StubHandler()));
        Assert.Throws<ArgumentException>(() => reporter.ReportBugfixRequest(new string('x', 129), "description"));
        Assert.Throws<ArgumentException>(() => reporter.ReportFeatureRequest("title", new string('x', 1025)));
        Assert.Empty(reporter.Store.ListPending());
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task FlushWithResults_PreservesRejectionAndPendingReport(HttpStatusCode status)
    {
        using var reporter = new ClientFeedbackClient(ValidOptions(new StubHandler(status)));
        Guid id = reporter.ReportFeatureRequest("Export", "Need CSV");
        var result = Assert.Single(await reporter.FlushPendingWithResultsAsync());
        Assert.Equal(id, result.ReportId);
        Assert.False(result.Uploaded);
        Assert.Equal(status, result.StatusCode);
        Assert.Equal("No released artefact matches package_name and version.", result.Error);
        Assert.Single(reporter.Store.ListPending());
    }

    [Fact]
    public async Task FlushWithResults_NetworkFailureHasNoStatusAndKeepsQueue()
    {
        using var reporter = new ClientFeedbackClient(ValidOptions(new StubHandler(failNetwork: true)));
        reporter.ReportFeatureRequest("Export", "Need CSV");
        var result = Assert.Single(await reporter.FlushPendingWithResultsAsync());
        Assert.False(result.Uploaded);
        Assert.Null(result.StatusCode);
        Assert.Single(reporter.Store.ListPending());
    }

    [Fact]
    public async Task FlushWithResults_SuccessIncludesReportIdAndClearsQueue()
    {
        using var reporter = new ClientFeedbackClient(ValidOptions(new StubHandler(HttpStatusCode.Created)));
        Guid id = reporter.ReportFeatureRequest("Export", "Need CSV");
        var result = Assert.Single(await reporter.FlushPendingWithResultsAsync());
        Assert.Equal(id, result.ReportId);
        Assert.True(result.Uploaded);
        Assert.Empty(reporter.Store.ListPending());
    }

    private static ClientFeedbackOptions ValidOptions(StubHandler handler) =>
        new()
        {
            BaseURL = "https://repository.example",
            Username = "apt_user",
            Token = "token",
            PackageName = "pkg-app",
            Version = "1.2.0",
            Architecture = "amd64",
            Format = "msi",
            QueueDirectory = Path.Combine(Path.GetTempPath(), "cer-q-" + Guid.NewGuid().ToString("N")),
            HttpClient = new HttpClient(handler) { BaseAddress = new Uri("https://repository.example/") },
        };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly bool _failNetwork;

        public StubHandler(HttpStatusCode status = HttpStatusCode.OK, bool failNetwork = false)
        {
            _status = status;
            _failNetwork = failNetwork;
        }

        public int PostCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_failNetwork)
                throw new HttpRequestException("offline");

            PostCount++;
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.EndsWith("/v1/client-feedback", request.RequestUri?.ToString(), StringComparison.Ordinal);
            return Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent("{\"error\":\"No released artefact matches package_name and version.\"}"),
            });
        }
    }
}
