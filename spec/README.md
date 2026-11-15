# Client feedback contract

All SDKs speak the existing Artifactd dataplane protocol. Branding does not rename the server routes. This directory is everything a new SDK needs; an implementation is complete when it passes the conformance tables below.

| Layer | Where | What it pins |
| --- | --- | --- |
| Wire format | `schemas/*.schema.json` | Field names, types, enums, patterns and limits of the request bodies. Derived from the published OpenAPI document (`docs.artifactd.net/spec/openapi.json`), which is the source of truth. |
| Examples | `fixtures/` | One valid payload per report kind and usage event, with and without GA4 identity. |
| Behaviour | `conformance/*.json` | How an SDK reacts to server answers and to invalid input. Executed by all five test suites. |
| Rules in prose | this file | Everything the tables cannot express: queue format, retry, deduplication, legacy fields. |

```sh
node spec/tools/check.mjs              # fixtures satisfy the schemas, invalid payloads are rejected, schemas match OpenAPI
node spec/tools/sync-schemas.mjs       # regenerate spec/schemas after the OpenAPI document changed
```

`check.mjs` compares `schemas/` with the OpenAPI document only when the `docs.artifactd.net` checkout sits next to this repository (or `ARTIFACTD_OPENAPI` points to it); otherwise that comparison is skipped.

## Endpoints

- Reports: `POST /v1/client-feedback`, UTF-8 JSON, pull identity via HTTP Basic (`username:token`).
- Usage: `POST /v1/client-usage-event`, HTTPS only, same pull identity.
- Report kinds: `crash`, `error`, `bugfix_request`, `feature_request`.
- A report binds to a released artefact by `package_name` and its exact published `version`. Optional `architecture` and `format` disambiguate it.

Server answers (see the OpenAPI document for the complete list): reports return `201` for a new `report_id` and `200` for a repeated one, both with `{ "report_id", "id_package_artifact" }`; rejections return `400` / `403` with `{ "error": "..." }`, `401` with an empty body, `413`, `429`. Usage returns `204`, or `400`, `401`, `403`, `404` (unknown package, or usage events switched off), `413`, `429`, `502`, `504`.

## Payload rules

- `report_id` is a UUID generated once before persistence. Every retry retains it so the server can deduplicate deliveries.
- `occurred_at_utc` is UTC, RFC 3339, written with a `Z` suffix. Payload keys use snake_case; enums use lowercase wire literals. Null optional fields are accepted.
- User requests put the title in `operation` and the description in `message`, with `source=user`. Both are trimmed, must be non-blank, and are limited to 128 and 1024 characters.
- **Characters are UTF-16 code units of the trimmed value**, because the server validates with C# `String.Length`. An emoji costs 2. Do not count Unicode code points or grapheme clusters: a 100-emoji title passes such a check and is then answered with `400` on every retry. Invalid requests throw and queue nothing (`conformance/validation.json`).
- Crash and error diagnostics are truncated, never rejected: `message` to 1024 and `stack_trace` to 8000 characters (the server stores up to 16000). The whole report must stay within 65536 UTF-8 bytes, otherwise the call is rejected.
- Optional `correlation_id` (1–128 characters, no whitespace or control characters) is the id the application mints once per request and forwards across its tiers so the server can join their reports. Omit the key when there is none. SDKs resolve it as explicit argument, then ambient scope, then provider; invalid ambient/provider values are dropped, an invalid explicit value is an error. The value is trimmed before it is checked. The server rejects invalid values with 400, so SDKs never queue one.
- Optional consented GA4 identity matches `[0-9]+\.[0-9]+` and is at most 128 characters. Reports capture it at enqueue time as `ga4_client_id` in the JSON body (the key is omitted when no identity was supplied, or when the provider returns anything else or throws). No data is sent in analytics HTTP headers: everything travels in the JSON body. Credential providers resolve per request; null means defer. Do not put tokens in logs or package artifacts.

## Delivery and queue

- Queue files contain the report payload, never credentials. Names are `<uuid-without-dashes>.pending.json`; a file whose name does not match the `report_id` inside it, or that cannot be parsed, is skipped and left alone. The clients write atomically, default to 20 pending reports and drop the oldest on overflow. Do not share one queue directory between processes or SDK instances.
- **Only a 2xx answer removes a queued report.** Transport errors, timeouts, redirects, 4xx and 5xx retain it (`conformance/upload-responses.json`). The next flush is the retry: SDKs do no in-process retry, no backoff and do not interpret `Retry-After`; the application decides when to flush again.
- Redirects are never followed. Requests carry credentials, so a redirect must not move them to another location.
- The default network timeout is 15 seconds for reports (configurable). Usage defaults to 15 seconds as well, except .NET which uses 5.
- An upload result exposes the report ID, the HTTP status (null on transport failure or deferred credentials), an uploaded flag and an optional string `error`. `error` comes only from a JSON object body of at most 4096 bytes whose `error` member is a string; any other body, including a larger one, yields null.
- Deduplication baseline: a report whose `kind`, `operation`, `exception_type`, `message`, `stack_trace` and `correlation_id` equal those of a report still pending is not queued again (the call returns null / an empty id). Entries that were already delivered do not count. The .NET SDK adds a 15-minute in-memory window and a shorter stack fingerprint (see below).

## Usage events

Usage is best effort, never persisted and never retried. Events: `app_open`, `update_check`, `device_connection_start`. Device protocols: `ssh`, `sftp`, `ipmi`, `proxmox`, `winbox`. Only Linux/Windows/macOS usage with architecture and format is sent, and only over HTTPS. Payload: `package_name`, `event_name`, `protocol`, `client_id`, numeric `session_id`, `occurred_at_utc`, optional `engagement_time_msec` (foreground milliseconds), `version`, `architecture`, `format`, `os`. The server accepts usage for any package unless its operator switched off **Accept client usage events**; unknown or switched-off packages get 404. A 2xx answer means sent; every other answer, and every transport failure, is absorbed without throwing (`conformance/usage-responses.json`).

## Fixtures

- `feature-request.json` is consumed by all five test suites and, with its `ga4_client_id`, doubles as the Google example for reports. It is also compatible with the .NET persisted queue format.
- `reports/` holds one example per kind (`crash`, `error`, `bugfix_request`, `feature_request`) and `usage/` holds `app_open` and `update_check` events. All five suites load them: each report must reach the server byte-for-byte unchanged after a trip through the queue, and the usage keys must equal what the SDK sends. None carries Google data.
- `google/` holds the only examples with a GA4 identity; see its README.

## Conformance tables

`conformance/` holds data, not code. Each SDK suite reads the JSON and drives its own transport double (a stubbed `fetch`, opener, `HttpMessageHandler`, `Transport` or local socket):

- `upload-responses.json`: server answer to a queued report -> `uploaded`, `status_code`, `error`, whether the file stays queued.
- `usage-responses.json`: server answer to a usage event -> `sent`. Rust can only exercise the transport failure (no TLS server in tests) and .NET, which is fire-and-forget, only checks that every answer is absorbed and sent once.
- `validation.json`: title/description limits (including trimming and UTF-16 counting), explicit correlation ids, GA4 identities and diagnostic truncation.
- `invalid-payloads.json`: mutations the wire schemas must reject; checked by `node spec/tools/check.mjs` rather than by the SDKs.

Values may be written as `{"repeat": [text, count]}` or `{"concat": [...]}` to keep the files small.

### Adding an SDK

1. Implement the rules above; the table descriptions say what each case means.
2. Load `conformance/*.json` and the fixtures from your test suite, like `packages/node/test/conformance.test.js`.
3. Run `node spec/tools/check.mjs` after any change to schemas, fixtures or tables.

## SDK-specific behavior

The .NET SDK retains its 15-minute deduplication window (its pending fingerprint uses the first 240 characters of the stack and ignores the message of non-user reports) and optional unhandled exception hooks, plus background usage delivery. Python/Node/Rust/PHP deduplicate identical reports while pending and use explicit calls for crash reporting. They never install global hooks. Python, Rust and PHP are blocking APIs; Node returns promises for network operations and writes its small queue synchronously. Usage returns a success boolean for these four SDKs. All SDKs enforce timeouts and disable HTTP redirects. Runtime/OS metadata is best effort; Rust leaves OS version null.

The payload and queue format are portable; automatic queue migration is not performed. A caller can retain its existing queue path, but must stop the previous owner before starting another client.

Queue files written by older .NET clients use `fault_time_utc` and `exception_message`. The .NET SDK still reads them and writes `occurred_at_utc` and `message`; the server accepts both spellings. New SDKs must write only `occurred_at_utc` and `message`, and may ignore (skip) queue files that use the legacy names.
