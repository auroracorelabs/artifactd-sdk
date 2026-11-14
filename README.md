# Artifactd Client Feedback

> [!IMPORTANT]
> These SDKs are provided as examples to help developers integrate their applications with Artifactd. They are not intended to be production-grade software. Review, adapt and validate the code against your application's requirements before using it in production.

Turn user feedback into your next release. Add bug reports, feature requests, crash reporting and usage signals to your application with the Artifactd Client Feedback SDKs.

These libraries connect your application to **[Artifactd — Artifact Repository Manager](https://artifactd.net/)**, a private software catalogue for publishing and distributing releases. Feedback is linked to the exact application version that shipped, so your team can investigate failures and understand user requests in the context of a release.

**[Official website](https://artifactd.net/) · [Download Artifactd](https://artifactd.net/download) · [Official documentation](https://docs.artifactd.net/)**

## SDK documentation

The [GitHub Pages documentation workflow](https://github.com/auroracorelabs/artifactd-sdk/actions/workflows/docs.yml) builds the complete SDK guides, runnable examples and generated API references for .NET, Python, Node.js/TypeScript, Rust and PHP from this repository. See [Pages setup and local builds](docs/PAGES.md) for activation and the published-site URL after the first successful deployment.

## What you can do

- **Hear from users:** collect bug reports and feature requests directly from your application.
- **Investigate failures:** send crashes and curated errors with diagnostic details and release information.
- **Keep reports until delivery:** persist reports locally and retry uploads when connectivity or credentials become available.
- **Understand application activity:** send best-effort, allow-listed usage events such as app opens and update checks.
- **Use your application's language:** integrate through the .NET, Python, Rust, Node.js or PHP SDK, with a common server protocol.

## Installation

Choose the SDK for your application:

| SDK | Requirements | Package | Full guide |
| --- | --- | --- | --- |
| .NET | .NET Standard 2.0 consumer; .NET Framework 4.7.2+ recommended for Framework apps | `ArtifactD.ClientFeedback` | [.NET guide](packages/dotnet/README.md) |
| Python | Python 3.10+ | `artifactd-client-feedback` | [Python guide](packages/python/README.md) |
| PHP | PHP 8.2+, cURL and JSON | `artifactd/client-feedback` | [PHP guide](packages/php/README.md) |
| Rust | Stable Rust with Cargo | `artifactd-client-feedback` | [Rust guide](packages/rust/README.md) |
| Node.js / TypeScript | Node.js 22+, ESM | `@artifactd/client-feedback` | [Node.js guide](packages/node/README.md) |

The portable .NET library is tested on .NET 8 and .NET 10. Node includes TypeScript declarations and targets Node.js rather than browsers. Python and Node have no third-party runtime dependencies; Rust uses a TLS-capable HTTP client.

### Install from a package registry

Each `vX.Y.Z` release publishes the SDKs to their language registries:

| SDK | Command |
| --- | --- |
| .NET | `dotnet add package ArtifactD.ClientFeedback` |
| Python | `python -m pip install artifactd-client-feedback` |
| Node.js / TypeScript | `npm install @artifactd/client-feedback` |
| Rust | `cargo add artifactd-client-feedback` |
| PHP | Composer package from the release ZIP (see below; not published on Packagist) |

Use Node.js in ESM mode (an `.mjs` file, or `"type": "module"` in your `package.json`).

### Install from a GitHub Release

Every release also attaches the same files to the repository's [Releases page](https://github.com/auroracorelabs/artifactd-sdk/releases), together with `SHA256SUMS` and `manifest.json`. Use them for offline or air-gapped installs and for PHP. Download the files for your language into a directory such as `sdk/` and verify them:

```sh
sha256sum --ignore-missing -c SHA256SUMS
```

The bundle layout is `dotnet/` (`.nupkg`, `.snupkg`), `python/` (`.whl`, `.tar.gz`), `node/` (`.tgz`), `rust/` (`.crate`) and `php/` (`.zip`). Replace `<sdk-version>` below with the release version.

<details>
<summary><strong>.NET — local NuGet source</strong></summary>

```sh
dotnet nuget add source "/absolute/path/to/sdk/dotnet" --name artifactd-sdk
dotnet add package ArtifactD.ClientFeedback --version "<sdk-version>"
```

The `.snupkg` contains optional debugging symbols.

</details>

<details>
<summary><strong>Python — wheel</strong></summary>

```sh
python -m pip install "./sdk/python/artifactd_client_feedback-<sdk-version>-py3-none-any.whl"
```

</details>

<details>
<summary><strong>Node.js / TypeScript — tarball</strong></summary>

```sh
npm install "./sdk/node/artifactd-client-feedback-<sdk-version>.tgz"
```

</details>

<details>
<summary><strong>Rust — packaged crate</strong></summary>

```sh
mkdir -p sdk/rust-unpacked
tar -xzf "./sdk/rust/artifactd-client-feedback-<sdk-version>.crate" -C sdk/rust-unpacked
cargo add artifactd-client-feedback --path "./sdk/rust-unpacked/artifactd-client-feedback-<sdk-version>"
```

</details>

### PHP — Composer archive

Download `artifactd-client-feedback-<sdk-version>.zip` from the release into `sdk/php`, then:

```sh
composer config repositories.artifactd artifact ./sdk/php
composer require artifactd/client-feedback:<sdk-version>
```

PHP requires cURL and JSON; Composer needs ext-zip for artifact repositories. See the [PHP guide](packages/php/README.md).

## Quick start

First [download and configure Artifactd](https://artifactd.net/download), follow the [server documentation](https://docs.artifactd.net/), or use your team's existing instance. Publish an application release and create a pull identity with permission to access that package.

The examples share these environment variables:

| Variable | Value |
| --- | --- |
| `ARTIFACTD_HOST` | Your instance's HTTPS dataplane base URL, e.g. `https://repository.example`; omit endpoint paths |
| `ARTIFACTD_PULL_USERNAME` | Pull identity with access to the application package |
| `ARTIFACTD_PULL_TOKEN` | That identity's pull token |
| `ARTIFACTD_PACKAGE_NAME` | Application package name in Artifactd |
| `ARTIFACTD_PACKAGE_VERSION` | Exact published application version (`version_raw`) |
| `ARTIFACTD_QUEUE_DIRECTORY` | Optional persistent queue path; defaults are separate for each language below |

**The application version is independent of the SDK package version.** A report for an unknown package/version is rejected and stays queued.

Set these variables through your shell, IDE or application configuration. Use a queue directory writable by your application and retain it across restarts. Each directory must have one owning client instance. The examples enqueue a feature request, flush pending reports and print the delivery result.

<details>
<summary><strong>.NET quick start</strong></summary>

Copy this into `Program.cs` in a console application with the package installed, then run `dotnet run`.

```csharp
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
```

[Runnable example](packages/dotnet/samples/ArtifactD.ClientFeedback.Sample/Program.cs)

</details>

<details>
<summary><strong>Python quick start</strong></summary>

Save as `feedback.py` in the environment where you installed the wheel, then run `python feedback.py`.

```python
import os
from artifactd_client_feedback import ClientFeedbackClient

feedback = ClientFeedbackClient(
    base_url=os.environ["ARTIFACTD_HOST"],
    username=os.environ["ARTIFACTD_PULL_USERNAME"],
    token=os.environ["ARTIFACTD_PULL_TOKEN"],
    package_name=os.environ["ARTIFACTD_PACKAGE_NAME"],
    version=os.environ["ARTIFACTD_PACKAGE_VERSION"],
    queue_directory=os.environ.get(
        "ARTIFACTD_QUEUE_DIRECTORY", ".artifactd-feedback/python"
    ),
)
feedback.report_feature_request("Export CSV", "Let me take my data anywhere.")
for result in feedback.flush_pending():
    print(result.report_id, result.uploaded, result.status_code)
```

[Runnable example](examples/python/feedback.py)

</details>

<details>
<summary><strong>Node.js / TypeScript quick start</strong></summary>

Save as `feedback.mjs` in the project where you installed the npm package, then run `node feedback.mjs`. The same API is typed for TypeScript.

```js
import { ClientFeedbackClient } from '@artifactd/client-feedback';

function required(name) {
  const value = process.env[name];
  if (!value) throw new Error(`Set ${name} before running this example`);
  return value;
}

const feedback = new ClientFeedbackClient({
  baseURL: required('ARTIFACTD_HOST'),
  username: required('ARTIFACTD_PULL_USERNAME'),
  token: required('ARTIFACTD_PULL_TOKEN'),
  packageName: required('ARTIFACTD_PACKAGE_NAME'),
  version: required('ARTIFACTD_PACKAGE_VERSION'),
  queueDirectory: process.env.ARTIFACTD_QUEUE_DIRECTORY ?? '.artifactd-feedback/node',
});
feedback.reportFeatureRequest('Export CSV', 'Let me take my data anywhere.');
for (const result of await feedback.flushPending()) {
  console.log(result.reportId, result.uploaded, result.statusCode);
}
```

[Runnable example](examples/node/feedback.mjs)

</details>

<details>
<summary><strong>Rust quick start</strong></summary>

Copy this into `src/main.rs` in the Cargo application where you added the crate, then run `cargo run`. The client performs blocking I/O; use a worker thread in async applications.

```rust
use artifactd_client_feedback::{ClientFeedbackClient, ClientFeedbackOptions, Credentials};

fn main() -> artifactd_client_feedback::Result<()> {
    let credentials = Credentials {
        base_url: std::env::var("ARTIFACTD_HOST")?,
        username: std::env::var("ARTIFACTD_PULL_USERNAME")?,
        token: std::env::var("ARTIFACTD_PULL_TOKEN")?,
    };
    let mut feedback = ClientFeedbackClient::new(ClientFeedbackOptions::new(
        std::env::var("ARTIFACTD_PACKAGE_NAME")?,
        std::env::var("ARTIFACTD_PACKAGE_VERSION")?,
        std::env::var("ARTIFACTD_QUEUE_DIRECTORY")
            .unwrap_or_else(|_| ".artifactd-feedback/rust".into()),
        credentials,
    ))?;
    feedback.report_feature_request("Export CSV", "Let me take my data anywhere.")?;
    for result in feedback.flush_pending()? {
        println!(
            "{}: uploaded={}, status={:?}",
            result.report_id,
            result.uploaded(),
            result.status_code
        );
    }
    Ok(())
}
```

[Runnable example](examples/rust/src/main.rs)

</details>

<details>
<summary><strong>PHP quick start</strong></summary>

Install the Composer package and configure the environment variables above, then run `php feedback.php`:

```php
<?php
require __DIR__ . '/vendor/autoload.php';

use ArtifactD\ClientFeedback\{ClientFeedbackClient, ClientFeedbackOptions, Credentials};

$feedback = new ClientFeedbackClient(new ClientFeedbackOptions(
    packageName: getenv('ARTIFACTD_PACKAGE_NAME'),
    version: getenv('ARTIFACTD_PACKAGE_VERSION'),
    queueDirectory: getenv('ARTIFACTD_QUEUE_DIRECTORY') ?: '.artifactd-feedback/php',
    credentials: new Credentials(
        getenv('ARTIFACTD_HOST'),
        getenv('ARTIFACTD_PULL_USERNAME'),
        getenv('ARTIFACTD_PULL_TOKEN'),
    ),
));
$feedback->reportFeatureRequest('Export CSV', 'Let me take my data anywhere.');
foreach ($feedback->flushPending() as $result) {
    printf("%s: uploaded=%s, status=%s\n",
        $result->reportId, $result->uploaded() ? 'true' : 'false',
        $result->statusCode ?? 'none');
}
```

[Runnable example](examples/php/feedback.php). Calls are synchronous; one client exclusively owns each queue directory. Use a single background owner or separate queues in applications with concurrent PHP workers.

</details>

Open **Client Feedback** in Artifactd to review delivered reports against the application release.

## Examples

Once the client is configured, use the calls below. Request titles allow 1–128 characters and descriptions allow 1–1024 characters. Invalid request text is rejected locally.

| Action | .NET | Python | Node.js | Rust | PHP |
| --- | --- | --- | --- | --- | --- |
| Feature request | `ReportFeatureRequest(title, description)` | `report_feature_request(title, description)` | `reportFeatureRequest(title, description)` | `report_feature_request(title, description)?` | `reportFeatureRequest($title, $description)` |
| Bug report | `ReportBugfixRequest(title, description)` | `report_bugfix_request(title, description)` | `reportBugfixRequest(title, description)` | `report_bugfix_request(title, description)?` | `reportBugfixRequest($title, $description)` |
| Handled error | `ReportError(operation, exception)` | `report_error(operation, exception)` | `reportError(operation, error)` | `report_error(operation, message)?` | `reportError($operation, $error)` |
| Crash | `ReportCrash(exception, source)` | `report_crash(exception, source)` | `reportCrash(error, source)` | `report_crash(message, source, stack_trace)?` | `reportCrash($error, $source)` |
| Deliver queued reports | `await FlushPendingWithResultsAsync()` | `flush_pending()` | `await flushPending()` | `flush_pending()?` | `flushPending()` |

Call these methods on your `feedback` instance. Report creation writes to the local queue; flushing performs the network requests. Call flush at startup to retry previous reports and after recording new feedback. Transport failures and server rejections retain the original report ID for a later retry. A null HTTP status indicates no server response or deferred credentials; inspect each result's uploaded flag before assuming delivery. Successful report delivery removes the queued file.

### Usage events

Usage events are optional and best effort: they are not persisted. They require HTTPS, a supported OS (Linux, Windows or macOS), and the application's package name, architecture, package format and published version. Packages accept usage events by default; if an operator switched off **Accept client usage events** (Packages → Configure), the server answers 404 and the SDK reports failure. Every SDK can add the optional foreground time in milliseconds (`engagement_time_msec`); it is sent in the JSON body like all other fields.

Python, Node and Rust expose `track_usage("app_open")`, `trackUsage('app_open')` and `track_usage("app_open", None)?` respectively on the feedback client. Set `architecture` and `format` in that client's configuration first. These calls return delivery success (a promise in Node). Additional events are `update_check` and `device_connection_start`; device connections require an allowed protocol such as `ssh`.

PHP exposes `trackUsage('app_open')`, `trackUsage('update_check')` and `trackUsage('device_connection_start', 'ssh')`; these are synchronous best-effort calls. `trackUsage` takes an optional third argument, the foreground time in milliseconds.

For .NET, create a separate `ClientUsageReporter` with `ClientUsageReportingOptions`, then call `TrackAppOpen()`, `TrackUpdateCheck()` or `TrackDeviceConnectionStart("ssh")`. See the [complete .NET integration guide](packages/dotnet/ArtifactD.ClientFeedback/README.md#usage-events).

### Run examples from this checkout

The examples above can also be run directly by contributors after setting the same environment variables:

```sh
# .NET (requires a .NET 8+ runtime and a compatible SDK)
dotnet run --project packages/dotnet/samples/ArtifactD.ClientFeedback.Sample
# Python (inside a virtual environment)
python -m pip install ./packages/python
python examples/python/feedback.py
# Node.js
npm install --prefix examples/node
node examples/node/feedback.mjs
# Rust
cargo run --manifest-path examples/rust/Cargo.toml
# PHP
composer install --working-dir=examples/php
php examples/php/feedback.php
```

These commands contact the Artifactd instance you configured and create a sample feature request. SDK tests use test transports and do not need a running server.

## Repository

```text
packages/
  dotnet/     library, tests and sample
  python/     src package, tests and pyproject.toml
  rust/       crate and integration tests
  node/       ESM package, TypeScript declarations and tests
  php/        Composer package, cURL client and tests
examples/     runnable Python, Node.js, Rust and PHP quick starts
spec/         wire contract and cross-language fixtures
ci/           package assembly, checksum tests and version tooling
.github/      build, test, package and optional publishing workflows
VERSION       shared stable release version
```

Read the [shared protocol and behavior contract](spec/README.md) and [release instructions](docs/RELEASING.md). All clients use the same existing Artifactd endpoints and pull credentials. Store tokens in environment variables or credential providers, outside committed configuration.

## Development

```sh
# .NET
dotnet test ArtifactD.Sdk.slnx -c Release
# Python
PYTHONPATH=packages/python/src python3 -m unittest discover -s packages/python/tests
# Node.js
npm test --prefix packages/node
# Rust
cargo test --manifest-path packages/rust/Cargo.toml
# PHP
php packages/php/tests/client.php
# Version consistency
python3 ci/version.py --check
```

New language SDKs belong in `packages/<language>` and must implement the shared contract, consume the fixtures, build an installable package, and add CI checks before being listed here.

## License

[MIT](LICENSE) — free for personal and commercial use.
