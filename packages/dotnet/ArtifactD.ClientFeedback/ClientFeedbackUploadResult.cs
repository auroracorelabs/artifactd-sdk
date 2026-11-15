using System.Net;

namespace ArtifactD.ClientFeedback;

public sealed record ClientFeedbackUploadResult(Guid ReportId, HttpStatusCode? StatusCode, string? Error = null)
{
    public bool Uploaded => StatusCode is HttpStatusCode.OK or HttpStatusCode.Created or HttpStatusCode.NoContent;
}
