"""Runs the language-independent tables in spec/conformance against this SDK."""
import json
import platform
import tempfile
import unittest
from io import BytesIO
from pathlib import Path
from unittest.mock import patch
from urllib.error import HTTPError
from artifactd_client_feedback import ClientFeedbackClient

CONFORMANCE = Path(__file__).resolve().parents[3] / "spec/conformance"


def table(name):
    return json.loads((CONFORMANCE / f"{name}.json").read_text(encoding="utf-8"))


def expand(value):
    if value is None or isinstance(value, str):
        return value
    if "repeat" in value:
        return value["repeat"][0] * value["repeat"][1]
    return "".join(expand(part) for part in value["concat"])


def body_of(response):
    return (response["body_repeat"][0] * response["body_repeat"][1]) if "body_repeat" in response else response.get("body", "")


class Ok:
    def __init__(self, status):
        self.status = status

    def __enter__(self):
        return self

    def __exit__(self, *args):
        pass


def answer(response):
    def send(req, timeout):
        if response is None:
            raise OSError("offline")
        status = response["status"]
        if status >= 300:  # redirects are not followed, so the opener surfaces them like any other non-2xx answer
            raise HTTPError(req.full_url, status, "", response.get("headers", {}), BytesIO(body_of(response).encode()))
        return Ok(status)
    return send


class ConformanceTests(unittest.TestCase):
    def client(self, **extra):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        client = ClientFeedbackClient(base_url="https://repo.example", username="pull", token="secret", package_name="my-app",
                                      version="2.0.0", queue_directory=temp.name, architecture="amd64", format="deb", **extra)
        return client, lambda: [json.loads(p.read_text()) for p in Path(temp.name).glob("*.pending.json")]

    def test_upload_responses(self):
        for case in table("upload-responses")["cases"]:
            with self.subTest(case["name"]):
                client, queued = self.client()
                client.report_feature_request("Export CSV", "Take my data")
                with patch.object(client._opener, "open", side_effect=answer(case["response"])):
                    result = client.flush_pending()[0]
                expect = case["expect"]
                self.assertEqual(expect["uploaded"], result.uploaded)
                self.assertEqual(expect["status_code"], result.status_code)
                self.assertEqual(expect["error"], result.error)
                self.assertEqual(1 if expect["retained"] else 0, len(queued()))

    def test_usage_responses(self):
        if platform.system() not in ("Linux", "Windows", "Darwin"):
            self.skipTest("usage is only sent from Linux, Windows and macOS")
        for case in table("usage-responses")["cases"]:
            with self.subTest(case["name"]):
                client, queued = self.client()
                with patch.object(client._opener, "open", side_effect=answer(case["response"])):
                    self.assertEqual(case["expect"]["sent"], client.track_usage("app_open"))
                self.assertEqual([], queued())

    def test_user_request_validation(self):
        for case in table("validation")["user_requests"]:
            for method in ("report_feature_request", "report_bugfix_request"):
                with self.subTest(f"{method}: {case['name']}"):
                    client, queued = self.client()
                    call = lambda: getattr(client, method)(expand(case["title"]), expand(case["description"]))
                    if case["valid"]:
                        self.assertIsNotNone(call())
                        self.assertEqual(1, len(queued()))
                    else:
                        with self.assertRaises(ValueError):
                            call()
                        self.assertEqual([], queued())

    def test_correlation_ids(self):
        ids = table("validation")["correlation_ids"]
        for case in ids["valid"]:
            with self.subTest(f"accepted: {case['name']}"):
                client, queued = self.client()
                self.assertIsNotNone(client.report_error("op", RuntimeError("x"), expand(case["value"])))
                self.assertEqual(expand(case["sent"]), queued()[0]["correlation_id"])
        for case in ids["invalid"]:
            with self.subTest(f"rejected: {case['name']}"):
                client, queued = self.client()
                with self.assertRaises(ValueError):
                    client.report_error("op", RuntimeError("x"), expand(case["value"]))
                self.assertEqual([], queued())

    def test_ga4_client_ids(self):
        ids = table("validation")["ga4_client_ids"]
        for accepted, cases in ((True, ids["accepted"]), (False, ids["omitted"])):
            for case in cases:
                with self.subTest(f"{'accepted' if accepted else 'omitted'}: {case['name']}"):
                    value = expand(case.get("value"))

                    def provider():
                        if case.get("throws"):
                            raise RuntimeError("provider failed")
                        return value
                    client, queued = self.client(ga4_client_id_provider=provider)
                    client.report_feature_request("Export", "CSV")
                    payload = queued()[0]
                    if accepted:
                        self.assertEqual(value, payload["ga4_client_id"])
                    else:
                        self.assertNotIn("ga4_client_id", payload)

    def test_diagnostics_are_truncated_not_rejected(self):
        limits = table("validation")["diagnostics"]
        client, queued = self.client()
        try:
            raise RuntimeError("m" * 5000)
        except RuntimeError as error:
            self.assertIsNotNone(client.report_error("op", error))
        payload = queued()[0]
        self.assertEqual(limits["message_limit"], len(payload["message"]))
        self.assertLessEqual(len(payload["stack_trace"]), limits["stack_trace_limit"])


if __name__ == "__main__":
    unittest.main()
