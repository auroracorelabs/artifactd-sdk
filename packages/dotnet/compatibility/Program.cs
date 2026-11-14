using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ArtifactD.ClientFeedback;

internal static class Program
{
    private static void Main()
    {
        string queue = Path.Combine(Path.GetTempPath(), "artifactd-framework-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var http = new HttpClient(new AcceptReports()))
            using (var feedback = new ClientFeedbackClient(new ClientFeedbackOptions
            {
                BaseURL = "https://repository.example", Username = "pull", Token = "test-only",
                PackageName = "compatibility", Version = "1.0.0", QueueDirectory = queue, HttpClient = http,
            }))
            {
                feedback.ReportFeatureRequest("Export CSV", "Take my data anywhere.");
                if (feedback.Store.ListPending().Count != 1) throw new Exception("Queue persistence failed.");
                var results = feedback.FlushPendingWithResultsAsync().GetAwaiter().GetResult();
                if (results.Count != 1 || !results[0].Uploaded || feedback.Store.ListPending().Count != 0)
                    throw new Exception("Report delivery failed.");
            }
            using (var usage = new ClientUsageReporter(new ClientUsageReportingOptions
            {
                PackageName = "compatibility", VersionProvider = () => "1.0.0", Architecture = "amd64", Format = "msi",
                ResolveCredentials = () => null,
            }))
            {
                usage.TrackAppOpen();
                usage.CompleteAsync().GetAwaiter().GetResult();
            }
            Console.WriteLine("Packaged .NET Standard client works on .NET Framework.");
        }
        finally { if (Directory.Exists(queue)) Directory.Delete(queue, true); }
    }

    private sealed class AcceptReports : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string json = await request.Content.ReadAsStringAsync();
            if (!json.Contains("\"feature_request\"") || !json.Contains("\"package_name\""))
                throw new Exception("Unexpected JSON contract.");
            return new HttpResponseMessage(HttpStatusCode.Created);
        }
    }
}
