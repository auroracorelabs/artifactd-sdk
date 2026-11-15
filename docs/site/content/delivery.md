# Queues, delivery and application lifecycle

All five SDKs use the [shared protocol](/protocol/). Reports identify your released application package and version; this is independent of the SDK package version.

## Queue ownership

Give each client one persistent directory that your application can write to, and keep it across restarts. One client owns each queue directory. Do not share a directory across processes, users or SDK languages. Queue files contain diagnostic text; use application-private storage.

The default maximum is 20 pending reports. SDKs bound their queues and suppress duplicate pending reports; a duplicate enqueue returns no new ID. Check each language's API for its supported options.

PHP web applications commonly have concurrent workers. Use a single background owner for a shared queue or separate queues with explicit delivery ownership; see the [PHP guide](/php/).

## Reporting and flushing

Report creation writes to the local queue. Flush at startup to retry reports left by a previous run, and after recording new feedback. Flushing is a separate operation so that a failed request can remain durable.

.NET and Node.js expose asynchronous flush methods. Python, Rust and PHP perform blocking I/O: use a worker thread or your framework's blocking task facility when integrating with an event loop.

A successful upload removes the queued file. Transport failures and server rejections retain it for a later attempt with the same report ID. Both HTTP 201 (new report) and 200 (already accepted) count as success. A null status indicates that there was no HTTP response or credentials were deferred. Inspect the result's success flag before reporting delivery to the user.

## Credentials and sign-in changes

Use pull HTTP Basic credentials scoped to the released package. Management and publish tokens are not Client Feedback credentials. Keep real token values in application configuration or credential providers, outside committed files.

Each SDK supports a credential provider for sign-in changes. A provider can defer delivery by returning no credentials. Use the language reference for callback types and asynchronous support; the Node.js credential provider may return a promise.

## User requests and diagnostics

Feature and bugfix requests require a nonblank title up to 128 characters and a nonblank description up to 1024 characters. Invalid request text is rejected locally. Avoid placing secrets or unnecessary personal data in diagnostic text.

.NET provides optional unhandled-exception hooks through ClientFeedbackBootstrap. Python, Node.js, Rust and PHP do not install process-global handlers. Connect them explicitly to your application's error handling and flush while the process can still complete network I/O.

## Usage events

Usage signals use a separate best-effort path and are not persisted. The server accepts them for every package unless an operator switched off **Accept client usage events** in the package configuration; then it answers 404.

Events require HTTPS, Linux, Windows or macOS, the package name, a compatible architecture and package format, and a released application version. An optional foreground duration (`engagement_time_msec`) travels in the JSON body with every other field; no SDK uses HTTP headers for analytics data. Supported names are app_open, update_check and device_connection_start; device events require an allow-listed protocol. See your language guide for configuration. Optional GA4 identities must come from your application's consented identity provider.

## Troubleshooting

| Result | Check |
| --- | --- |
| No HTTP status | Connectivity, timeout and whether the credential provider deferred delivery |
| 400 | Package name, exact released version, architecture/format ambiguity, request JSON and deployment policy |
| 401 | Pull username/token and identity activation |
| 403 | Access grants for the reported released artifact |
| 413 | UTF-8 request body size; the feedback endpoint limit is 65536 bytes |
| 429 | Retry later; the dataplane may be rate limited |

Use [Artifactd's server documentation](https://docs.artifactd.net/api/client-feedback) for the HTTP contract. Do not change the report ID to retry the same report.
