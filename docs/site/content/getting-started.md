# Getting started

Connect an application to your Artifactd instance, then send and verify your first Client Feedback report.

{{ sdk-notice }}

## Install the SDK

Install from your language's package registry, or use the files attached to a [GitHub Release](https://github.com/auroracorelabs/artifactd-sdk-client/releases) (offline installs, PHP):

| SDK | Command |
| --- | --- |
| .NET | `dotnet add package ArtifactD.ClientFeedback` |
| Python | `python -m pip install artifactd-client-feedback` |
| Node.js | `npm install @artifactd/client-feedback` |
| Rust | `cargo add artifactd-client-feedback` |
| PHP | Composer package from the release ZIP; see the [PHP guide](/php/#install-the-composer-package) |

Each release also attaches every package with `SHA256SUMS`. The SDK version is separate from the version of the application you report feedback about. Language details: [.NET](/dotnet/#install), [Python](/python/), [Node.js](/node/), [Rust](/rust/#install-the-crate) and [PHP](/php/#install-the-composer-package). The [distribution guide](/releasing/#where-users-get-the-packages) covers registries, releases and checksums.

## Quick start

Publish your application release in Artifactd and configure a pull identity with access to it. Set the following variables through your shell, IDE or application configuration:

{{ sdk-environment }}

The application package name and exact published version identify the release receiving feedback. Use a queue directory writable by the application and keep it across restarts. Each queue directory must have one owning client.

<span id="examples"></span>
## Run an example

Choose a [runnable example](/examples/) for your language. Each example reads the variables above, queues a feature request, flushes pending reports and prints the delivery result. The examples page includes the actual source files and commands to run them.

A successful report returns HTTP 201, or 200 if its report ID was already accepted. Open **Client Feedback** in your Artifactd instance to verify that the request belongs to the expected application release.

## Delivery and retries

Use [queues, delivery and application lifecycle](/delivery/) for retry behavior, credential changes and troubleshooting. Follow your language guide for blocking versus asynchronous calls and callback types.

## Usage events

Optional usage signals have a separate best-effort delivery path. See [usage events](/delivery/#usage-events) for shared requirements and your language guide for method names and configuration.

## Development

For SDK contributors, see [building and distributing the SDKs](/releasing/) and [documentation maintenance](/documentation/). The repository [README](https://github.com/auroracorelabs/artifactd-sdk-client#readme) remains a standalone introduction for GitHub visitors.
