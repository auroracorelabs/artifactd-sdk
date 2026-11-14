# Client feedback contract

All SDKs speak the existing Artifactd dataplane protocol. Branding does not rename the server routes.

- Reports: `POST /v1/client-error-report`, UTF-8 JSON, pull identity via HTTP Basic (`username:token`).
- Usage: `POST /v1/client-usage-event`, HTTPS only, same pull identity.
- Report kinds: `crash`, `error`, `bugfix_request`, `feature_request`.
- A report binds to a released artefact by `package_name` and its exact published `version`. Optional `architecture` and `format` disambiguate it.
- `report_id` is a UUID generated once before persistence. Every retry retains it so the server can deduplicate deliveries.
- UTC `occurred_at_utc` uses RFC 3339. Payload keys use snake_case; enums use lowercase wire literals. Null optional fields are accepted.
- User requests put the title in `operation` (1–128 characters), description in `message` (1–1024), and `source=user`. Crash/error messages are truncated to 1024, stacks to 8000. Reports are capped at 65536 UTF-8 bytes.
- Queue files contain the report payload, never credentials. Names are `<uuid-without-dashes>.pending.json`. Only 2xx removes a queued report; transport errors, redirects, 4xx and 5xx retain it. The new clients write atomically and default to 20 pending reports, dropping the oldest on overflow. Do not share one queue directory between processes or SDK instances.
- Optional consented GA4 identity matches `[0-9]+\.[0-9]+` and is at most 128 characters. Reports capture it at enqueue time as `ga4_client_id` in the JSON body (the key is omitted when no identity was supplied). No data is sent in analytics HTTP headers: everything travels in the JSON body. Credential providers resolve per request; null means defer. Do not put tokens in logs or package artifacts.
- Upload results expose report ID, status (null on transport failure/deferred credentials), uploaded flag and optional string `error` from a bounded JSON rejection body.
- Usage is best effort, never persisted. Events: `app_open`, `update_check`, `device_connection_start`. Device protocols: `ssh`, `sftp`, `ipmi`, `proxmox`, `winbox`. Only Linux/Windows/macOS usage with architecture and format is sent. Payload: `package_name`, `event_name`, `protocol`, `client_id`, numeric `session_id`, `occurred_at_utc`, optional `engagement_time_msec` (foreground milliseconds), `version`, `architecture`, `format`, `os`. The server accepts usage for any package unless its operator switched off **Accept client usage events**; unknown or switched-off packages get 404.

Fixtures live in `spec/fixtures/`:

- `feature-request.json` is consumed by all five test suites and, with its `ga4_client_id`, doubles as the Google example for reports. It is also compatible with the .NET persisted queue format.
- `reports/` holds one example per kind (`crash`, `error`, `bugfix_request`, `feature_request`) and `usage/` holds `app_open` and `update_check` events. None carries Google data. They document the wire format and are not yet loaded by the test suites.
- `google/` holds the only examples with a GA4 identity; see its README.

Queue files written by older .NET clients use `fault_time_utc` and `exception_message`. The .NET SDK still reads them and writes `occurred_at_utc` and `message`; the server accepts both spellings.

## SDK-specific behavior

The .NET SDK retains its 15-minute deduplication window and optional unhandled exception hooks, plus background usage delivery. Python/Node/Rust/PHP deduplicate identical reports while pending and use explicit calls for crash reporting. They never install global hooks. Python, Rust and PHP are blocking APIs; Node returns promises for network operations and writes its small queue synchronously. Usage returns a success boolean for these four SDKs. New clients enforce timeouts and disable HTTP redirects. Runtime/OS metadata is best effort; Rust leaves OS version null.

The payload and queue format are portable; automatic queue migration is not performed. A caller can retain its existing queue path, but must stop the previous owner before starting another client.
