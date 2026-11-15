# ArtifactD.ClientFeedback

Bring user feedback into your release cycle. Capture bug reports, feature requests, crashes and usage signals, with a persistent report queue and delivery results. Each report connects to the **released artefact** identified by package name and published version.

## Install

```bash
dotnet add package ArtifactD.ClientFeedback
```

The package contains a compiled portable assembly and declares the dependencies NuGet needs to restore. Release tags `vX.Y.Z` produce version `X.Y.Z`. For offline installs, download the `.nupkg` from the [GitHub Release](https://github.com/auroracorelabs/artifactd-sdk-client/releases) and add its directory as a local source: `dotnet nuget add source "/path/to/packages" --name artifactd-sdk`.

Use [Artifactd's official website](https://artifactd.net/) to learn about the server, [download Artifactd](https://artifactd.net/download), or read the [official documentation](https://docs.artifactd.net/).

## Configure

Set the dataplane base URL, pull Basic credentials, catalogue package name, and the **exact published version** of the running build (`version_raw` on the artefact).

```csharp
using ArtifactD.ClientFeedback;

using var feedback = new ClientFeedbackClient(new ClientFeedbackOptions
{
    BaseURL = "https://repository.example",
    Username = pullUsername,
    Token = pullToken,
    PackageName = "my-app",
    Version = PublishedProductVersion.FromEntryAssembly(),
    QueueDirectory = Path.Combine(appData, "client-feedback"),
});

ClientFeedbackBootstrap.AttachUnhandledHandlers(feedback);
await feedback.FlushPendingAsync();
```

## Report

- Unhandled process faults: `AttachUnhandledHandlers` writes `kind=crash`.
- Operator-visible failures: `ReportError("catalogue_refresh", ex)` writes `kind=error`. Do not report every handled exception.
- After write, call `FlushPendingAsync` (on startup and after errors) to upload the local queue.

Each queued payload includes `os` and `os_version` for the machine that ran the app.

Unknown package/version pairs are rejected by the server so reports never attach to the wrong release.

For server setup and feedback management, see the [official Artifactd documentation](https://docs.artifactd.net/).

## User requests

Use the same client and credentials for requests submitted by users:

```csharp
feedback.ReportBugfixRequest("Save fails", "Steps to reproduce and expected behavior");
feedback.ReportFeatureRequest("Export CSV", "What the user needs and why");
await feedback.FlushPendingAsync();
```

Kinds are `bugfix_request` and `feature_request`. The existing table and wire fields are reused: `operation` holds the title (required, max 128 characters), `message` holds the description (required, max 1024), and `source` is `user`. No exception or stack is needed. Invalid text is rejected rather than truncated. Package/version matching, retry queue, access checks, retention and report-ID idempotency are unchanged. Request deduplication includes kind, title and full description.

`FlushPendingWithResultsAsync()` returns one upload result per queued report (`ReportId`, `Uploaded`, HTTP `StatusCode`, and optional server `Error`). A null status indicates a transport failure. Rejected reports remain queued. Map server errors to curated messages rather than displaying raw response text. `FlushPendingAsync()` remains available for callers that do not need delivery details.

## Usage events

Best-effort allow-listed events go to the same dataplane with the same pull identity. The GA4 secret stays on Artifactd.

```csharp
using var usage = new ClientUsageReporter(new ClientUsageReportingOptions
{
    ResolveCredentials = () => new ClientUsageCredentials(baseURL, pullUsername, pullToken),
    PackageName = "my-app",
    VersionProvider = () => PublishedProductVersion.FromEntryAssembly(),
    Architecture = "amd64",
    Format = OperatingSystem.IsWindows() ? "msi" : "deb",
});

usage.TrackAppOpen();
usage.TrackDeviceConnectionStart("ssh"); // allow-listed protocols only
usage.TrackUpdateCheck(engagementMilliseconds: 12_500); // optional foreground time
```

`POST /v1/client-usage-event` (HTTPS only). Events are dropped when credentials are missing, the feed is cleartext HTTP, or architecture/format/version cannot be resolved. There is no durable queue: a bounded in-memory channel drops on overflow. Failures never throw to the caller.

## Optional GA4 account signals

Set `Ga4ClientIdProvider` on report/usage options to the optional consented GA4 client ID supplied by your application. Invalid or missing values and provider failures are ignored. Reports capture the ID when queued; the JSON body carries it as `ga4_client_id` (omitted when absent), without attributing an old report to a later login. Usage sends this ID as `client_id` (otherwise retaining the existing per-process identity). `TrackUpdateCheck()` sends an allow-listed `update_check` event; every Track method accepts an optional `engagementMilliseconds`. The server accepts usage for every package unless an operator switched off **Accept client usage events**. `ClientFeedbackOptions.ResolveCredentials` optionally resolves current pull credentials per upload; a null result leaves reports queued until sign-in. Report and usage providers must use the same account settings. Google API secrets remain server-side.
