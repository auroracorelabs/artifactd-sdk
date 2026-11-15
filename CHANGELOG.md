# Changelog

All notable changes to the Artifactd SDKs. Versions are shared across all languages.

## Unreleased

- **Breaking:** the report endpoint is now `POST /v1/client-feedback` (was `/v1/client-error-report`). All five SDKs post to the new path; upgrade the SDK together with the server.
- Correlation id: reports carry an optional `correlation_id` shared across the systems that handled one request. All five SDKs add an id generator, an ambient scope, an optional per-report argument and a provider option; dedup now also compares the id. .NET `ReportCrash`/`ReportError` gain an optional trailing `correlationId` parameter (source compatible; recompile against it).
- Added MIT license, package metadata, GitHub Release with attached packages, NuGet publishing, community files.

## 1.4.1

- Initial public SDK repository: .NET, Python, Node.js, Rust and PHP client feedback packages.
