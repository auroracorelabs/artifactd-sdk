# Artifactd Client Feedback SDKs

Bring user requests, bug reports and crash diagnostics back to the application release that shipped. Choose your language, install a package, and connect to your own Artifactd instance.

## Choose your SDK

| SDK | Integration guide | Generated API reference |
| --- | --- | --- |
| .NET Standard 2.0 | [.NET](/dotnet/) | [.NET API](/reference/dotnet/index.html) |
| Python 3.10+ | [Python](/python/) | [Python API](/reference/python/artifactd_client_feedback.html) |
| Node.js 22+ / TypeScript | [Node.js](/node/) | [Node.js API](/reference/node/index.html) |
| Rust | [Rust](/rust/) | [Rust API](/reference/rust/artifactd_client_feedback/index.html) |
| PHP 8.2+ | [PHP](/php/) | [PHP API](/reference/php/index.html) |

## Start integrating

1. [Install the SDK](/getting-started/#install-the-sdk) from your language's package registry or a GitHub Release.
2. Configure your Artifactd dataplane host, pull credentials and exact published application version.
3. Queue a feature request or bug report, then flush the pending reports.
4. Review the delivery results and check Client Feedback in Artifactd.

Use [Getting started](/getting-started/) for shared setup and [runnable examples](/examples/) for code and execution commands. The [queue and delivery guide](/delivery/) explains retries, credentials and application lifecycle decisions.

## API reference

API references are generated from the same SDK source in the documentation build: DocFX for .NET, pdoc for Python, TypeDoc for Node.js/TypeScript, rustdoc for Rust, and PHP reflection for PHP. They include public types, configuration properties and method signatures.

This site documents the SDK checkout deployed from the repository's default branch. The shared SDK version is shown in the navigation. Match your installed package to the corresponding source version when inspecting release-specific behavior.

## Server documentation

- [Developer documentation](https://docs.artifactd.net/)
- [REST API and OpenAPI](https://docs.artifactd.net/api)
- [AI integration references](https://docs.artifactd.net/ai)
- [Official Artifactd website](https://artifactd.net/) · [Download Artifactd](https://artifactd.net/download)
- [SDK source on GitHub](https://github.com/auroracorelabs/artifactd-sdk)

The documentation site hosts static references. Feedback requests go to your Artifactd instance, using its pull credentials.
