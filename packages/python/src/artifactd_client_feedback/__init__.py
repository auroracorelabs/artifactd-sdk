"""Durable client feedback for Artifactd. No third-party runtime dependencies."""
from __future__ import annotations

import base64
import json
import os
import platform
import re
import threading
import traceback
import uuid
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from urllib import error, request
from urllib.parse import urlsplit


@dataclass(frozen=True)
class UploadResult:
    report_id: str
    status_code: int | None
    error: str | None = None

    @property
    def uploaded(self) -> bool:
        return self.status_code is not None and 200 <= self.status_code < 300


@dataclass(frozen=True)
class Credentials:
    base_url: str
    username: str
    token: str


class _NoRedirect(request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


def _identity(provider):
    try:
        value = provider() if provider else None
        return value if isinstance(value, str) and len(value) <= 128 and re.fullmatch(r"[0-9]+\.[0-9]+", value) else None
    except Exception:
        return None


def _validate_credentials(credentials):
    url = urlsplit(credentials.base_url)
    if url.scheme not in ("http", "https") or not url.hostname or url.username or url.password or url.query or url.fragment:
        raise ValueError("base_url must be an absolute HTTP(S) URL without credentials, query or fragment")
    if not credentials.username.strip() or not credentials.token or ":" in credentials.username:
        raise ValueError("username and token are required; username cannot contain ':'")


class ClientFeedbackClient:
    """One client owns its queue directory. Calls are serialized within the client."""

    def __init__(self, *, package_name: str, version: str, queue_directory,
                 base_url: str = "", username: str = "", token: str = "",
                 resolve_credentials=None, ga4_client_id_provider=None,
                 architecture: str | None = None, format: str | None = None,
                 max_pending: int = 20, timeout: float = 15):
        if not package_name.strip() or not version.strip() or not str(queue_directory).strip():
            raise ValueError("package_name, version and queue_directory are required")
        if max_pending < 1 or timeout <= 0:
            raise ValueError("max_pending and timeout must be positive")
        self._credentials = Credentials(base_url, username, token)
        if resolve_credentials is None:
            _validate_credentials(self._credentials)
        self._resolve = resolve_credentials
        self._ga4 = ga4_client_id_provider
        self.package_name, self.version = package_name.strip(), version.strip()
        self.architecture, self.format = architecture, format
        self.queue = Path(queue_directory)
        self.max_pending, self.timeout = max_pending, timeout
        self._lock = threading.RLock()
        self._opener = request.build_opener(_NoRedirect())
        self._session = int(datetime.now(timezone.utc).timestamp())
        self._client_id = f"{uuid.uuid4().int >> 64}.{self._session}"

    def _pending(self):
        rows = []
        for path in sorted(self.queue.glob("*.pending.json"), key=lambda p: p.name):
            try:
                payload = json.loads(path.read_text(encoding="utf-8"))
                report_id = uuid.UUID(payload["report_id"])
                if path.name != f"{report_id.hex}.pending.json":
                    continue
                if payload["kind"] not in ("crash", "error", "bugfix_request", "feature_request"):
                    continue
                rows.append((path, payload))
            except (OSError, ValueError, KeyError, TypeError):
                continue
        return rows

    def _enqueue(self, kind, operation, message=None, exception=None, source="user"):
        operation = operation.strip()
        if not operation:
            raise ValueError("operation/title is required")
        if kind.endswith("_request") and (len(operation) > 128 or not message or not message.strip() or len(message.strip()) > 1024):
            raise ValueError("title must be 1..128 and description 1..1024 characters")
        payload = dict(report_id=str(uuid.uuid4()), kind=kind, package_name=self.package_name,
                       version=self.version, architecture=self.architecture, format=self.format,
                       runtime=f"Python {platform.python_version()}", os={"darwin": "macos"}.get(platform.system().lower(), platform.system().lower()),
                       os_version=platform.release(), occurred_at_utc=datetime.now(timezone.utc).isoformat(),
                       operation=operation, exception_type=type(exception).__name__ if exception else None,
                       message=message.strip() if kind.endswith("_request") else (str(exception)[:1024] if exception else None),
                       stack_trace="".join(traceback.format_exception(exception))[:8000] if exception else None,
                       source=source)
        if (ga4_client_id := _identity(self._ga4)):
            payload["ga4_client_id"] = ga4_client_id  # only present when the app supplied a consented identity
        body = json.dumps(payload, ensure_ascii=False).encode("utf-8")
        if len(body) > 65536:
            raise ValueError("report exceeds 65536 bytes")
        with self._lock:
            self.queue.mkdir(parents=True, exist_ok=True, mode=0o700)
            rows = self._pending()
            keys = ("kind", "operation", "exception_type", "message", "stack_trace")
            if any(all(p.get(k) == payload.get(k) for k in keys) for _, p in rows):
                return None
            target = self.queue / f"{uuid.UUID(payload['report_id']).hex}.pending.json"
            temporary = target.with_suffix(".tmp")
            try:
                with temporary.open("xb") as stream:
                    os.chmod(temporary, 0o600)
                    stream.write(body)
                    stream.flush()
                    os.fsync(stream.fileno())
                temporary.replace(target)
            finally:
                temporary.unlink(missing_ok=True)
            for path, _ in sorted(rows, key=lambda row: row[1].get("occurred_at_utc", ""))[:max(0, len(rows) - self.max_pending + 1)]:
                path.unlink(missing_ok=True)
        return payload["report_id"]

    def report_feature_request(self, title: str, description: str):
        return self._enqueue("feature_request", title, description)

    def report_bugfix_request(self, title: str, description: str):
        return self._enqueue("bugfix_request", title, description)

    def report_error(self, operation: str, exception: Exception | None = None):
        return self._enqueue("error", operation, exception=exception, source="curated")

    def report_crash(self, exception: Exception, source: str):
        if not isinstance(exception, BaseException) or not source.strip():
            raise ValueError("exception and source are required")
        return self._enqueue("crash", "crash", exception=exception, source=source)

    def _post(self, path, payload, https_only=False):
        try:
            credentials = self._resolve() if self._resolve else self._credentials
            if credentials is None:
                return None, None
            _validate_credentials(credentials)
            if https_only and urlsplit(credentials.base_url).scheme != "https":
                return None, None
            auth = base64.b64encode(f"{credentials.username}:{credentials.token}".encode()).decode()
            headers = {"Content-Type": "application/json", "Authorization": f"Basic {auth}"}
            body = json.dumps(payload, ensure_ascii=False).encode("utf-8")
            if len(body) > 65536:
                return 413, None
            req = request.Request(credentials.base_url.rstrip("/") + path,
                                  data=body, headers=headers, method="POST")
            try:
                with self._opener.open(req, timeout=self.timeout) as response:
                    return response.status, None
            except error.HTTPError as response:
                try:
                    body = response.read(4097)
                    if len(body) > 4096:
                        return response.code, None
                    data = json.loads(body)
                    message = data.get("error")
                    return response.code, message if isinstance(message, str) else None
                except (ValueError, AttributeError):
                    return response.code, None
                finally:
                    response.close()
        except Exception:
            return None, None

    def flush_pending(self) -> list[UploadResult]:
        with self._lock:
            results = []
            for path, payload in self._pending():
                status, message = self._post("/v1/client-error-report", payload)
                result = UploadResult(payload["report_id"], status, message)
                results.append(result)
                if result.uploaded:
                    path.unlink(missing_ok=True)
            return results

    def track_usage(self, event_name: str, protocol: str | None = None, engagement_time_ms: int | None = None) -> bool:
        if event_name not in ("app_open", "update_check", "device_connection_start"):
            raise ValueError("unsupported event_name")
        if event_name == "device_connection_start" and protocol not in ("ssh", "sftp", "ipmi", "proxmox", "winbox"):
            return False
        if engagement_time_ms is not None and (not isinstance(engagement_time_ms, int) or isinstance(engagement_time_ms, bool) or engagement_time_ms < 0):
            raise ValueError("engagement_time_ms must be a non-negative integer")
        os_name = {"darwin": "macos"}.get(platform.system().lower(), platform.system().lower())
        if os_name not in ("windows", "linux", "macos") or not self.architecture or not self.format:
            return False
        identity = _identity(self._ga4)
        payload = dict(package_name=self.package_name, event_name=event_name,
            protocol=protocol if event_name == "device_connection_start" else None,
            client_id=identity or self._client_id, session_id=self._session,
            occurred_at_utc=datetime.now(timezone.utc).isoformat(timespec="milliseconds").replace("+00:00", "Z"))
        if engagement_time_ms is not None:
            payload["engagement_time_msec"] = engagement_time_ms
        payload.update(version=self.version, architecture=self.architecture, format=self.format, os=os_name)
        status, _ = self._post("/v1/client-usage-event", payload, True)
        return status is not None and 200 <= status < 300
