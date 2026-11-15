using System.Text.Json;
using ArtifactD.ClientFeedback;

namespace ArtifactD.ClientFeedback.Tests;

public sealed class ClientFeedbackJsonTests
{
    [Fact]
    public void Payload_RoundTripsSnakeCase()
    {
        var payload = new ClientFeedbackPayload
        {
            ReportId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            Kind = ClientFeedbackKind.Crash,
            PackageName = "sample-app",
            Version = "2.0.0+ci.1",
            Architecture = "amd64",
            Format = "msi",
            Runtime = "runtime",
            Os = "Windows",
            OsVersion = "10",
            OccurredAtUtc = new DateTime(2026, 10, 4, 8, 0, 0, DateTimeKind.Utc),
            Operation = "crash",
            ExceptionType = "System.UnauthorizedAccessException",
            Message = "denied",
            StackTrace = "at App",
            Source = "dispatcher",
        };

        string json = JsonSerializer.Serialize(payload, ClientFeedbackJsonContext.Default.ClientFeedbackPayload);
        Assert.Contains("\"report_id\"", json, StringComparison.Ordinal);
        Assert.Contains("\"package_name\"", json, StringComparison.Ordinal);
        Assert.Contains("\"kind\":\"crash\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("ReportId", json, StringComparison.Ordinal);
        Assert.DoesNotContain("PackageName", json, StringComparison.Ordinal);

        ClientFeedbackPayload? roundTrip = JsonSerializer.Deserialize(
            json,
            ClientFeedbackJsonContext.Default.ClientFeedbackPayload);
        Assert.NotNull(roundTrip);
        Assert.Equal(payload.ReportId, roundTrip.ReportId);
        Assert.Equal(ClientFeedbackKind.Crash, roundTrip.Kind);
        Assert.Equal("sample-app", roundTrip.PackageName);
        Assert.Equal("2.0.0+ci.1", roundTrip.Version);
        Assert.Contains("\"os\":\"Windows\"", json, StringComparison.Ordinal);
        Assert.Contains("\"os_version\":\"10\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Payload_ReadsLegacyQueueFieldNames_AndWritesNewOnes()
    {
        const string legacy = "{\"report_id\":\"aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee\",\"kind\":\"error\"," +
            "\"package_name\":\"app\",\"version\":\"1.0.0\",\"fault_time_utc\":\"2026-01-15T10:30:00Z\"," +
            "\"exception_message\":\"denied\"}";
        ClientFeedbackPayload? payload = JsonSerializer.Deserialize(
            legacy, ClientFeedbackJsonContext.Default.ClientFeedbackPayload);
        Assert.NotNull(payload);
        Assert.Equal(new DateTime(2026, 1, 15, 10, 30, 0, DateTimeKind.Utc), payload.OccurredAtUtc);
        Assert.Equal("denied", payload.Message);

        string json = JsonSerializer.Serialize(payload, ClientFeedbackJsonContext.Default.ClientFeedbackPayload);
        Assert.Contains("\"occurred_at_utc\"", json, StringComparison.Ordinal);
        Assert.Contains("\"message\":\"denied\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("fault_time_utc", json, StringComparison.Ordinal);
        Assert.DoesNotContain("exception_message", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Kind_RejectsCamelCaseNumericAndUnknown()
    {
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize("\"Crash\"", ClientFeedbackJsonContext.Default.ClientFeedbackKind));
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize("1", ClientFeedbackJsonContext.Default.ClientFeedbackKind));
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize("\"fatal\"", ClientFeedbackJsonContext.Default.ClientFeedbackKind));
    }

    [Theory]
    [InlineData(ClientFeedbackKind.Error, "error")]
    [InlineData(ClientFeedbackKind.BugfixRequest, "bugfix_request")]
    [InlineData(ClientFeedbackKind.FeatureRequest, "feature_request")]
    public void Kind_Literal_RoundTrips(ClientFeedbackKind kind, string literal)
    {
        string json = JsonSerializer.Serialize(kind, ClientFeedbackJsonContext.Default.ClientFeedbackKind);
        Assert.Equal($"\"{literal}\"", json);
        Assert.Equal(
            kind,
            JsonSerializer.Deserialize(json, ClientFeedbackJsonContext.Default.ClientFeedbackKind));
    }
}
