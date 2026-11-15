using System.Text.Json.Serialization;

namespace ArtifactD.ClientFeedback;

public sealed class ClientFeedbackPayload
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Ga4ClientId { get; set; }
    public Guid ReportId { get; set; }
    public ClientFeedbackKind Kind { get; set; }
    public string PackageName { get; set; } = "";
    public string Version { get; set; } = "";
    public string? Architecture { get; set; }
    public string? Format { get; set; }
    public string? Runtime { get; set; }
    public string? Os { get; set; }
    public string? OsVersion { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public string? Operation { get; set; }
    public string? ExceptionType { get; set; }
    public string? Message { get; set; }
    public string? StackTrace { get; set; }
    public string? Source { get; set; }

    /// <summary>Id shared with the other tiers that handled the same request; omitted when none was supplied.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CorrelationId { get; set; }

    // Read-only compatibility with queue files written before the field rename.
    [JsonPropertyName("fault_time_utc")]
    public DateTime LegacyFaultTimeUtc { set { if (OccurredAtUtc == default) OccurredAtUtc = value; } }

    [JsonPropertyName("exception_message")]
    public string? LegacyExceptionMessage { set { Message ??= value; } }
}
