using System.Net;
using System.Text.Json;

namespace ArtifactD.ClientFeedback.Tests;

/// <summary>Runs the language-independent tables in spec/conformance against this SDK.</summary>
public sealed class ConformanceTests
{
    private static JsonElement Table(string name) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "conformance", name + ".json"))).RootElement;

    public static TheoryData<int> Indexes(string table, string? member = null)
    {
        JsonElement root = Table(table);
        if (member is not null) root = root.GetProperty(member);
        var data = new TheoryData<int>();
        for (int i = 0; i < root.GetArrayLength(); i++) data.Add(i);
        return data;
    }
    public static TheoryData<int> UploadCases() => Indexes("upload-responses", "cases");
    public static TheoryData<int> UsageCases() => Indexes("usage-responses", "cases");
    public static TheoryData<int> UserRequestCases() => Indexes("validation", "user_requests");

    private static string Expand(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String) return value.GetString()!;
        if (value.ValueKind == JsonValueKind.Null) return "";
        if (value.TryGetProperty("repeat", out var repeat))
            return string.Concat(Enumerable.Repeat(repeat[0].GetString()!, repeat[1].GetInt32()));
        return string.Concat(value.GetProperty("concat").EnumerateArray().Select(Expand));
    }

    private static string BodyOf(JsonElement response) =>
        response.TryGetProperty("body_repeat", out var repeat)
            ? string.Concat(Enumerable.Repeat(repeat[0].GetString()!, repeat[1].GetInt32()))
            : response.TryGetProperty("body", out var body) ? body.GetString()! : "";

    private sealed class ScriptedHandler(JsonElement response) : HttpMessageHandler
    {
        public int PostCount { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            PostCount++;
            if (response.ValueKind == JsonValueKind.Null) throw new HttpRequestException("offline");
            var message = new HttpResponseMessage((HttpStatusCode)response.GetProperty("status").GetInt32())
            {
                Content = new StringContent(BodyOf(response)),
            };
            if (response.TryGetProperty("headers", out var headers))
                foreach (var header in headers.EnumerateObject())
                    message.Headers.TryAddWithoutValidation(header.Name, header.Value.GetString());
            return Task.FromResult(message);
        }
    }

    private static ClientFeedbackOptions Options(HttpMessageHandler handler, Func<string?>? ga4 = null) => new()
    {
        BaseURL = "https://repo.example",
        Username = "pull",
        Token = "secret",
        PackageName = "my-app",
        Version = "2.0.0",
        Architecture = "amd64",
        Format = "deb",
        QueueDirectory = Path.Combine(Path.GetTempPath(), "conformance-" + Guid.NewGuid().ToString("N")),
        Ga4ClientIdProvider = ga4,
        HttpClient = new HttpClient(handler),
    };

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    [Theory]
    [MemberData(nameof(UploadCases))]
    public async Task UploadResponse(int index)
    {
        JsonElement test = Table("upload-responses").GetProperty("cases")[index];
        JsonElement expect = test.GetProperty("expect");
        using var client = new ClientFeedbackClient(Options(new ScriptedHandler(test.GetProperty("response"))));
        client.ReportFeatureRequest("Export CSV", "Take my data");
        var result = (await client.FlushPendingWithResultsAsync()).Single();
        string name = test.GetProperty("name").GetString()!;
        Assert.True(expect.GetProperty("uploaded").GetBoolean() == result.Uploaded, name + ": uploaded");
        int? status = result.StatusCode is { } s ? (int)s : null;
        Assert.True((expect.GetProperty("status_code").ValueKind == JsonValueKind.Null ? (int?)null : expect.GetProperty("status_code").GetInt32()) == status, name + ": status");
        Assert.Equal(Str(expect, "error"), result.Error);
        Assert.Equal(expect.GetProperty("retained").GetBoolean() ? 1 : 0, client.Store.ListPending().Count);
    }

    /// <summary>.NET usage is fire-and-forget (no result to inspect): every answer must be absorbed without throwing,
    /// sent exactly once (no retry) and never persisted.</summary>
    [Theory]
    [MemberData(nameof(UsageCases))]
    public async Task UsageResponse(int index)
    {
        JsonElement test = Table("usage-responses").GetProperty("cases")[index];
        var handler = new ScriptedHandler(test.GetProperty("response"));
        using var reporter = new ClientUsageReporter(new ClientUsageReportingOptions
        {
            ResolveCredentials = () => new ClientUsageCredentials("https://repo.example", "pull", "secret"),
            PackageName = "my-app",
            VersionProvider = () => "2.0.0",
            Architecture = "amd64",
            Format = "deb",
            HttpClient = new HttpClient(handler),
        });
        reporter.TrackAppOpen();
        await reporter.CompleteAsync();
        string os = HostOperatingSystem.PlatformName();
        Assert.Equal(os is "windows" or "linux" or "macos" ? 1 : 0, handler.PostCount);
    }

    [Theory]
    [MemberData(nameof(UserRequestCases))]
    public void UserRequestValidation(int index)
    {
        JsonElement test = Table("validation").GetProperty("user_requests")[index];
        foreach (bool bugfix in new[] { false, true })
        {
            using var client = new ClientFeedbackClient(Options(new ScriptedHandler(default)));
            string title = Expand(test.GetProperty("title")), description = Expand(test.GetProperty("description"));
            Func<Guid> call = () => bugfix ? client.ReportBugfixRequest(title, description) : client.ReportFeatureRequest(title, description);
            string name = test.GetProperty("name").GetString()!;
            if (test.GetProperty("valid").GetBoolean())
            {
                Assert.NotEqual(Guid.Empty, call());
                Assert.Single(client.Store.ListPending());
            }
            else
            {
                Assert.ThrowsAny<ArgumentException>(() => { call(); });
                Assert.True(client.Store.ListPending().Count == 0, name + ": nothing queued");
            }
        }
    }

    public static TheoryData<int> ValidCorrelation() => Range("correlation_ids", "valid");
    public static TheoryData<int> InvalidCorrelation() => Range("correlation_ids", "invalid");
    public static TheoryData<int> AcceptedGa4() => Range("ga4_client_ids", "accepted");
    public static TheoryData<int> OmittedGa4() => Range("ga4_client_ids", "omitted");
    private static TheoryData<int> Range(string section, string list)
    {
        var data = new TheoryData<int>();
        for (int i = 0; i < Table("validation").GetProperty(section).GetProperty(list).GetArrayLength(); i++) data.Add(i);
        return data;
    }

    [Theory]
    [MemberData(nameof(ValidCorrelation))]
    public void CorrelationIdAccepted(int index)
    {
        JsonElement test = Table("validation").GetProperty("correlation_ids").GetProperty("valid")[index];
        using var client = new ClientFeedbackClient(Options(new ScriptedHandler(default)));
        Assert.NotEqual(Guid.Empty, client.ReportError("op", new InvalidOperationException("x"), Expand(test.GetProperty("value"))));
        Assert.Equal(Expand(test.GetProperty("sent")), client.Store.ListPending().Single().CorrelationId);
    }

    [Theory]
    [MemberData(nameof(InvalidCorrelation))]
    public void CorrelationIdRejected(int index)
    {
        JsonElement test = Table("validation").GetProperty("correlation_ids").GetProperty("invalid")[index];
        using var client = new ClientFeedbackClient(Options(new ScriptedHandler(default)));
        Assert.ThrowsAny<ArgumentException>(() => client.ReportError("op", new InvalidOperationException("x"), Expand(test.GetProperty("value"))));
        Assert.Empty(client.Store.ListPending());
    }

    [Theory]
    [MemberData(nameof(AcceptedGa4))]
    public void Ga4ClientIdAccepted(int index) => Ga4(Table("validation").GetProperty("ga4_client_ids").GetProperty("accepted")[index], true);

    [Theory]
    [MemberData(nameof(OmittedGa4))]
    public void Ga4ClientIdOmitted(int index) => Ga4(Table("validation").GetProperty("ga4_client_ids").GetProperty("omitted")[index], false);

    private static void Ga4(JsonElement test, bool accepted)
    {
        bool throws = test.TryGetProperty("throws", out var t) && t.GetBoolean();
        string? value = throws ? null : Expand(test.GetProperty("value"));
        using var client = new ClientFeedbackClient(Options(new ScriptedHandler(default), () => throws ? throw new InvalidOperationException("provider failed") : value));
        client.ReportFeatureRequest("Export", "CSV");
        string? sent = client.Store.ListPending().Single().Ga4ClientId;
        if (accepted) Assert.Equal(value, sent); else Assert.Null(sent);
    }

    [Fact]
    public void DiagnosticsAreTruncatedNotRejected()
    {
        JsonElement limits = Table("validation").GetProperty("diagnostics");
        using var client = new ClientFeedbackClient(Options(new ScriptedHandler(default)));
        Assert.NotEqual(Guid.Empty, client.ReportError("op", new InvalidOperationException(new string('m', 5000))));
        var payload = client.Store.ListPending().Single();
        Assert.Equal(limits.GetProperty("message_limit").GetInt32(), payload.Message!.Length);
        Assert.True(payload.StackTrace is null || payload.StackTrace.Length <= limits.GetProperty("stack_trace_limit").GetInt32());
    }
}
