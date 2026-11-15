using System;
using ArtifactD.ClientFeedback;

static string Required(string name) =>
    Environment.GetEnvironmentVariable(name)
    ?? throw new InvalidOperationException($"Set {name} before running this example.");

using var feedback = new ClientFeedbackClient(new ClientFeedbackOptions
{
    BaseURL = Required("ARTIFACTD_HOST"),
    Username = Required("ARTIFACTD_PULL_USERNAME"),
    Token = Required("ARTIFACTD_PULL_TOKEN"),
    PackageName = Required("ARTIFACTD_PACKAGE_NAME"),
    Version = Required("ARTIFACTD_PACKAGE_VERSION"),
    QueueDirectory = Environment.GetEnvironmentVariable("ARTIFACTD_QUEUE_DIRECTORY")
        ?? ".artifactd-feedback/dotnet",
});
feedback.ReportFeatureRequest("Export CSV", "Let me take my data anywhere.");
foreach (var result in await feedback.FlushPendingWithResultsAsync())
{
    Console.WriteLine($"{result.ReportId}: uploaded={result.Uploaded}, status={result.StatusCode}");
}
