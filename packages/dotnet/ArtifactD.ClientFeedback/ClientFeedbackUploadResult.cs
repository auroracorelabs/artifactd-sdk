using System.Net;

namespace ArtifactD.ClientFeedback;

public sealed record ClientFeedbackUploadResult(Guid ReportId, HttpStatusCode? StatusCode, string? Error = null)
{
    public bool Uploaded => StatusCode is { } status && (int)status >= 200 && (int)status < 300;
}
