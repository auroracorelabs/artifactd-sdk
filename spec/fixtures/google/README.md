# Google (GA4) integration examples

Only these examples carry a GA4 identity. The identity must be consented and match `[0-9]+\.[0-9]+` (max 128 characters).

- Reports: the ID is sent as `ga4_client_id` in the JSON body (`crash-with-ga4.json`).
- Usage events: the ID replaces the per-process `client_id` in the JSON body (`app-open-with-ga4.json`). Usage payloads have no `ga4_client_id` key.
- Duration: `engagement_time_msec` (foreground time in milliseconds) and `occurred_at_utc` are JSON body fields, forwarded to Google with `session_id` (`app-open-with-ga4-and-engagement.json`).

All data travels in the JSON body. No SDK sends analytics data in HTTP headers.

The legacy `../feature-request.json` also contains `ga4_client_id`; the SDK test suites assert it, so it is kept as-is.
