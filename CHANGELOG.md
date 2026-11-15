# Changelog

All notable changes to the Artifactd SDKs. Versions are shared across all languages.

## 1.5.0 — 2026-11-15

- Contract: `spec/schemas` (JSON Schema derived from the published OpenAPI document), shared conformance tables in `spec/conformance` (server answers, input validation, usage answers) executed by all five test suites, and `node spec/tools/check.mjs` to validate fixtures against the schemas.
- Title and description limits (128 / 1024) now count UTF-16 code units of the trimmed value, exactly as the server does. Node, Python, Rust and PHP previously counted Unicode code points and could queue an emoji-heavy request the server answers with 400 forever.
- .NET: any 2xx answer now counts as delivered (was only 200, 201 and 204); the SDK-owned `HttpClient` no longer follows redirects and uses a 15 second timeout, like the other SDKs.
- Python: report timestamps use the `Z` suffix like every other SDK.
- **Breaking:** the report endpoint is now `POST /v1/client-feedback` (was `/v1/client-error-report`). All five SDKs post to the new path; upgrade the SDK together with the server.
- Correlation id: reports carry an optional `correlation_id` shared across the systems that handled one request. All five SDKs add an id generator, an ambient scope, an optional per-report argument and a provider option; dedup now also compares the id. .NET `ReportCrash`/`ReportError` gain an optional trailing `correlationId` parameter (source compatible; recompile against it).
- Added MIT license, package metadata, GitHub Release with attached packages, NuGet publishing, community files.

## 1.4.1

- Initial public SDK repository: .NET, Python, Node.js, Rust and PHP client feedback packages.
