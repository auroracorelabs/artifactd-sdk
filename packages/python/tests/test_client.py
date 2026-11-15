import base64
import json
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch
from artifactd_client_feedback import ClientFeedbackClient, Credentials, correlation_scope, current_correlation_id, new_correlation_id

FIXTURES = Path(__file__).resolve().parents[3] / "spec/fixtures"
FIXTURE = FIXTURES / "feature-request.json"
REPORT_KEYS = ["report_id", "kind", "package_name", "version", "architecture", "format", "runtime", "os", "os_version",
               "occurred_at_utc", "operation", "exception_type", "message", "stack_trace", "source"]
REPORT_FIXTURES = ["reports/crash.json", "reports/error.json", "reports/bugfix-request.json", "reports/feature-request.json", "google/crash-with-ga4.json"]

class Response:
    status = 201
    def __enter__(self): return self
    def __exit__(self, *args): pass

class ClientTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.options = dict(base_url="https://repo.example", username="pull", token="secret", package_name="my-app", version="2.0.0", queue_directory=self.temp.name, architecture="amd64", format="deb")
        self.client = ClientFeedbackClient(**self.options)

    def test_request_validation_dedup_and_bound(self):
        with self.assertRaises(ValueError): self.client.report_feature_request("", "text")
        with self.assertRaises(ValueError): self.client.report_feature_request("x", "x" * 1025)
        first = self.client.report_feature_request("Export CSV", "Take my data")
        self.assertIsNotNone(first)
        self.assertIsNone(self.client.report_feature_request("Export CSV", "Take my data"))
        bounded = ClientFeedbackClient(**self.options, max_pending=1)
        bounded.report_bugfix_request("Save", "Does not save")
        rows = list(Path(self.temp.name).glob("*.pending.json"))
        self.assertEqual(1, len(rows))
        self.assertEqual("bugfix_request", json.loads(rows[0].read_text())["kind"])
        self.assertNotIn("secret", rows[0].read_text())

    def test_fixture_auth_attribution_and_successful_removal(self):
        payload = json.loads(FIXTURE.read_text())
        path = Path(self.temp.name) / (payload["report_id"].replace("-", "") + ".pending.json")
        path.write_text(json.dumps(payload))
        client = ClientFeedbackClient(**self.options, ga4_client_id_provider=lambda: "999.999")
        seen = []
        def send(req, timeout):
            seen.append(req)
            return Response()
        with patch.object(client._opener, "open", side_effect=send):
            results = client.flush_pending()
        self.assertTrue(results[0].uploaded)
        self.assertFalse(path.exists())
        self.assertEqual("https://repo.example/v1/client-feedback", seen[0].full_url)
        self.assertEqual("Basic " + base64.b64encode(b"pull:secret").decode(), seen[0].get_header("Authorization"))
        self.assertEqual("123.456", json.loads(seen[0].data)["ga4_client_id"])  # body only, no header
        self.assertIsNone(seen[0].get_header("X-ga4-client-id"))
        self.assertEqual(payload, json.loads(seen[0].data))

    def test_failure_retained_and_credentials_deferred(self):
        self.client.report_error("save", RuntimeError("failed"))
        with patch.object(self.client._opener, "open", side_effect=OSError("offline")):
            self.assertIsNone(self.client.flush_pending()[0].status_code)
        client = ClientFeedbackClient(**self.options, resolve_credentials=lambda: None)
        self.assertFalse(client.flush_pending()[0].uploaded)
        self.assertEqual(1, len(list(Path(self.temp.name).glob("*.pending.json"))))

    def test_rejected_report_retained_with_details(self):
        from io import BytesIO
        from urllib.error import HTTPError
        self.client.report_bugfix_request("Save", "Fails")
        with patch.object(self.client._opener, "open", side_effect=HTTPError("https://repo.example", 403, "forbidden", {}, BytesIO(b'{"error":"access_denied"}'))):
            result = self.client.flush_pending()[0]
        self.assertEqual(403, result.status_code)
        self.assertEqual("access_denied", result.error)
        self.assertFalse(result.uploaded)
        self.assertEqual(1, len(list(Path(self.temp.name).glob("*.pending.json"))))

    def test_restart_and_corrupt_file(self):
        self.client.report_crash(RuntimeError("bad"), "main")
        Path(self.temp.name, "corrupt.pending.json").write_text("broken")
        restarted = ClientFeedbackClient(**self.options)
        with patch.object(restarted._opener, "open", return_value=Response()):
            self.assertEqual(1, len(restarted.flush_pending()))

    def test_usage_allowlist_https_and_payload(self):
        insecure = ClientFeedbackClient(**{**self.options, "base_url": "http://repo.example"})
        with patch.object(insecure._opener, "open") as send:
            self.assertFalse(insecure.track_usage("app_open"))
            send.assert_not_called()
        with self.assertRaises(ValueError): self.client.track_usage("anything")
        self.assertFalse(self.client.track_usage("device_connection_start", "telnet"))
        seen = []
        with patch("platform.system", return_value="Linux"), patch.object(self.client._opener, "open", side_effect=lambda req, timeout: (seen.append(req) or Response())):
            self.assertTrue(self.client.track_usage("device_connection_start", "ssh", 2345))
            with self.assertRaises(ValueError): self.client.track_usage("app_open", None, -1)
        payload = json.loads(seen[0].data)
        self.assertEqual("ssh", payload["protocol"])
        self.assertEqual(self.options["package_name"], payload["package_name"])
        self.assertEqual(2345, payload["engagement_time_msec"])
        self.assertTrue(payload["occurred_at_utc"].endswith("Z"))
        self.assertIsNone(seen[0].get_header("X-ga4-client-id"))
        self.assertEqual([], list(Path(self.temp.name).glob("*.pending.json")))

    def test_providers_and_invalid_url(self):
        for url in ["file:///tmp/no", "https://user:pass@repo.example", "https://repo.example?token=no"]:
            with self.assertRaises(ValueError): ClientFeedbackClient(**{**self.options, "base_url": url})
        client = ClientFeedbackClient(**self.options, resolve_credentials=lambda: Credentials("https://next.example", "next", "token"), ga4_client_id_provider=lambda: "bad-id")
        client.report_feature_request("Export", "CSV")
        with patch.object(client._opener, "open", return_value=Response()) as send:
            client.flush_pending()
            self.assertTrue(send.call_args[0][0].full_url.startswith("https://next.example"))
            self.assertIsNone(send.call_args[0][0].get_header("X-ga4-client-id"))

    def load(self, path):
        return json.loads((FIXTURES / path).read_text(encoding="utf-8"))

    def test_report_fixtures_match_the_wire_contract_and_survive_the_queue_unchanged(self):
        for path in REPORT_FIXTURES:
            with self.subTest(path), tempfile.TemporaryDirectory() as queue:
                payload = self.load(path)
                self.assertEqual(REPORT_KEYS + (["ga4_client_id"] if path.startswith("google/") else []), list(payload))
                self.assertIn(payload["kind"], ("crash", "error", "bugfix_request", "feature_request"))
                self.assertRegex(payload["occurred_at_utc"], r"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?Z$")
                if payload["kind"].endswith("_request"):
                    self.assertEqual("user", payload["source"])
                    self.assertTrue(1 <= len(payload["operation"]) <= 128)
                    self.assertTrue(1 <= len(payload["message"]) <= 1024)
                (Path(queue) / (payload["report_id"].replace("-", "") + ".pending.json")).write_text(json.dumps(payload))
                client = ClientFeedbackClient(**{**self.options, "queue_directory": queue})
                seen = []
                with patch.object(client._opener, "open", side_effect=lambda req, timeout: (seen.append(req) or Response())):
                    self.assertTrue(client.flush_pending()[0].uploaded)
                self.assertEqual(payload, json.loads(seen[0].data))

    def test_usage_fixtures_list_exactly_the_keys_the_sdk_sends(self):
        expected = list(self.load("usage/app-open.json"))
        self.assertEqual(expected, list(self.load("usage/update-check.json")))
        self.assertEqual(expected, list(self.load("google/app-open-with-ga4.json")))
        with_engagement = list(self.load("usage/app-open-with-engagement.json"))
        self.assertEqual(with_engagement, list(self.load("google/app-open-with-ga4-and-engagement.json")))
        self.assertEqual(["engagement_time_msec"], [k for k in with_engagement if k not in expected])
        self.assertNotIn("ga4_client_id", expected)
        seen = []
        with patch("platform.system", return_value="Linux"), patch.object(self.client._opener, "open", side_effect=lambda req, timeout: (seen.append(req) or Response())):
            self.assertTrue(self.client.track_usage("app_open"))
            self.assertTrue(self.client.track_usage("app_open", None, 45000))
        self.assertEqual(expected, list(json.loads(seen[0].data)))
        self.assertEqual(with_engagement, list(json.loads(seen[1].data)))

    def test_generated_report_has_the_fixture_keys_and_omits_the_identity_unless_supplied(self):
        self.client.report_feature_request("Export CSV", "Take my data")
        [file] = Path(self.temp.name).glob("*.pending.json")
        self.assertEqual(REPORT_KEYS, list(json.loads(file.read_text())))
        with tempfile.TemporaryDirectory() as queue:
            consented = ClientFeedbackClient(**{**self.options, "queue_directory": queue}, ga4_client_id_provider=lambda: "123.456")
            consented.report_feature_request("Export CSV", "Take my data")
            [saved] = Path(queue).glob("*.pending.json")
            self.assertEqual(REPORT_KEYS + ["ga4_client_id"], list(json.loads(saved.read_text())))

    def test_correlation_id_explicit_ambient_provider_dedup_and_validation(self):
        read = lambda d: [json.loads(f.read_text()) for f in Path(d).glob("*.pending.json")]
        self.assertTrue(self.client.report_error("a", ValueError("x"), correlation_id="req-1"))
        with correlation_scope("req-2"):
            self.assertEqual("req-2", current_correlation_id())
            self.client.report_error("b", ValueError("x"))
        self.assertIsNone(current_correlation_id())
        self.assertTrue(self.client.report_error("c", ValueError("x")))
        self.assertIsNone(self.client.report_error("a", ValueError("x"), correlation_id="req-1"))
        self.assertTrue(self.client.report_error("a", ValueError("x"), correlation_id="req-9"))
        by_op = {p["operation"] + str(p.get("correlation_id")): p for p in read(self.temp.name)}
        self.assertEqual({"areq-1", "breq-2", "cNone", "areq-9"}, set(by_op))
        self.assertNotIn("correlation_id", by_op["cNone"])
        with self.assertRaises(ValueError): self.client.report_error("d", None, correlation_id="has space")
        with self.assertRaises(ValueError): correlation_scope("").__enter__()
        self.assertRegex(new_correlation_id(), r"^[0-9a-f]{32}$")
        for provider, expected in ((lambda: "from-provider", "from-provider"), (lambda: "bad id", None), (lambda: 1 / 0, None)):
            with tempfile.TemporaryDirectory() as queue:
                c = ClientFeedbackClient(**{**self.options, "queue_directory": queue}, correlation_id_provider=provider)
                c.report_error("p")
                self.assertEqual(expected, read(queue)[0].get("correlation_id"))

if __name__ == "__main__": unittest.main()
