# Artifactd Client Feedback for Python

Turn bug reports and feature requests into your next release. Requires Python 3.10+; no runtime dependencies.

Install with `pip install artifactd-client-feedback`. Offline: use the wheel attached to the [GitHub Release](https://github.com/auroracorelabs/artifactd-sdk-client/releases) (`pip install artifactd_client_feedback-<version>-py3-none-any.whl`), or `pip install ./packages/python` from a checkout.

```python
import os
from artifactd_client_feedback import ClientFeedbackClient

feedback = ClientFeedbackClient(
    base_url=os.environ["ARTIFACTD_HOST"],
    username=os.environ["ARTIFACTD_PULL_USERNAME"],
    token=os.environ["ARTIFACTD_PULL_TOKEN"],
    package_name="my-app", version="2.0.0",
    queue_directory=".feedback", architecture="amd64", format="deb",
)
feedback.report_feature_request("Export CSV", "Let me take my data anywhere.")
feedback.report_bugfix_request("Save fails", "Saving an empty draft shows an error.")
for result in feedback.flush_pending():
    print(result.report_id, result.uploaded, result.status_code)
```

`report_error(operation, exception=None)` and `report_crash(exception, source)` queue diagnostics. Call `flush_pending()` at startup and after reports. Rejected reports stay queued; transport failures return a null status. Methods are synchronous; call from a worker thread if necessary.

Use `resolve_credentials=lambda: Credentials(base_url, username, token)` for sign-in changes (return `None` to defer uploads), and `ga4_client_id_provider` for an optional consented ID. Providers resolve per call. Import `Credentials` from the same module. Report attribution is captured when queued. `track_usage("app_open")`, `track_usage("update_check")` or `track_usage("device_connection_start", "ssh")` return delivery success and require HTTPS, Linux/Windows/macOS, architecture and format. A third argument, `track_usage("app_open", None, 12500)`, adds the foreground time in milliseconds. No global exception handlers are installed.

One client owns its queue directory; avoid multiple processes using it. Defaults: 20 pending reports, 15-second network timeout. Identical pending reports return `None` instead of another ID. Queue paths should be private to the current user. Use HTTPS in production.

From the repository root:

```sh
PYTHONPATH=packages/python/src python3 -m unittest discover -s packages/python/tests
python3 -m build packages/python
```

See the shared contract and release instructions in the repository for protocol details and publishing setup.
