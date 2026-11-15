//! Durable Artifactd client feedback. Each client exclusively owns its queue directory.
//! Blocking API: call from a worker thread in async applications. No global panic hooks.
use chrono::{SecondsFormat, Utc};
use reqwest::{blocking::Client, redirect::Policy, Url};
use serde::{Deserialize, Serialize};
use std::{
    error::Error,
    fs,
    io::{Read, Write},
    path::{Path, PathBuf},
    sync::Arc,
    time::Duration,
};
use uuid::Uuid;

pub type Result<T> = std::result::Result<T, Box<dyn Error + Send + Sync>>;
pub type CredentialProvider = Arc<dyn Fn() -> Option<Credentials> + Send + Sync>;
pub type IdentityProvider = Arc<dyn Fn() -> Option<String> + Send + Sync>;

/// Conventional HTTP header used to forward the correlation id between tiers.
pub const CORRELATION_HEADER: &str = "X-Correlation-Id";

thread_local! {
    static AMBIENT_CORRELATION: std::cell::RefCell<Option<String>> = const { std::cell::RefCell::new(None) };
}

/// A new opaque id (32 lowercase hex characters) to mint once per request and forward to every tier.
pub fn new_correlation_id() -> String {
    Uuid::new_v4().simple().to_string()
}

/// Trimmed id, or `None` when empty, longer than 128 characters or containing whitespace/control characters.
pub fn normalize_correlation_id(value: &str) -> Option<String> {
    let id = value.trim();
    (!id.is_empty() && id.chars().count() <= 128 && !id.chars().any(|c| c <= ' ' || c == '\u{7f}'))
        .then(|| id.to_owned())
}

/// Makes an id ambient on the current thread until dropped; reports raised on this thread carry it.
pub struct CorrelationScope(Option<String>);
impl Drop for CorrelationScope {
    fn drop(&mut self) {
        let previous = self.0.take();
        AMBIENT_CORRELATION.with(|slot| *slot.borrow_mut() = previous);
    }
}
pub fn correlation_scope(correlation_id: &str) -> Result<CorrelationScope> {
    let id = normalize_correlation_id(correlation_id).ok_or(
        "correlation_id must be 1..128 characters without whitespace or control characters",
    )?;
    Ok(CorrelationScope(
        AMBIENT_CORRELATION.with(|slot| slot.borrow_mut().replace(id)),
    ))
}
pub fn current_correlation_id() -> Option<String> {
    AMBIENT_CORRELATION.with(|slot| slot.borrow().clone())
}

#[derive(Clone)]
pub struct Credentials {
    pub base_url: String,
    pub username: String,
    pub token: String,
}
impl Credentials {
    fn validate(&self) -> Result<()> {
        let url = Url::parse(&self.base_url)?;
        if !matches!(url.scheme(), "http" | "https")
            || url.host_str().is_none()
            || !url.username().is_empty()
            || url.password().is_some()
            || url.query().is_some()
            || url.fragment().is_some()
            || self.username.trim().is_empty()
            || self.username.contains(':')
            || self.token.is_empty()
        {
            return Err("valid HTTP(S) base_url, username and token are required".into());
        }
        Ok(())
    }
}

pub struct ClientFeedbackOptions {
    pub package_name: String,
    pub version: String,
    pub queue_directory: PathBuf,
    pub credentials: Credentials,
    pub resolve_credentials: Option<CredentialProvider>,
    pub ga4_client_id_provider: Option<IdentityProvider>,
    /// Fallback when no explicit id or `correlation_scope` is active. Invalid values are ignored.
    pub correlation_id_provider: Option<IdentityProvider>,
    pub architecture: Option<String>,
    pub format: Option<String>,
    pub max_pending: usize,
    pub timeout: Duration,
}
impl ClientFeedbackOptions {
    pub fn new(
        package_name: impl Into<String>,
        version: impl Into<String>,
        queue_directory: impl Into<PathBuf>,
        credentials: Credentials,
    ) -> Self {
        Self {
            package_name: package_name.into(),
            version: version.into(),
            queue_directory: queue_directory.into(),
            credentials,
            resolve_credentials: None,
            ga4_client_id_provider: None,
            correlation_id_provider: None,
            architecture: None,
            format: None,
            max_pending: 20,
            timeout: Duration::from_secs(15),
        }
    }
}

#[derive(Debug, Clone, Copy, Serialize, Deserialize, PartialEq, Eq)]
#[serde(rename_all = "snake_case")]
pub enum FeedbackKind {
    Crash,
    Error,
    BugfixRequest,
    FeatureRequest,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct FeedbackPayload {
    pub report_id: String,
    pub kind: FeedbackKind,
    pub package_name: String,
    pub version: String,
    pub architecture: Option<String>,
    pub format: Option<String>,
    pub runtime: Option<String>,
    pub os: Option<String>,
    pub os_version: Option<String>,
    pub occurred_at_utc: String,
    pub operation: String,
    pub exception_type: Option<String>,
    pub message: Option<String>,
    pub stack_trace: Option<String>,
    pub source: String,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub ga4_client_id: Option<String>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub correlation_id: Option<String>,
}
#[derive(Debug)]
pub struct UploadResult {
    pub report_id: String,
    pub status_code: Option<u16>,
    pub error: Option<String>,
}
impl UploadResult {
    pub fn uploaded(&self) -> bool {
        self.status_code.is_some_and(|s| (200..300).contains(&s))
    }
}

pub struct ClientFeedbackClient {
    options: ClientFeedbackOptions,
    http: Client,
    client_id: String,
    session_id: i64,
}
fn normalize_id(value: Option<String>) -> Option<String> {
    value.filter(|s| {
        let parts: Vec<_> = s.split('.').collect();
        s.len() <= 128
            && parts.len() == 2
            && parts
                .iter()
                .all(|p| !p.is_empty() && p.bytes().all(|c| c.is_ascii_digit()))
    })
}
fn os_name() -> &'static str {
    if cfg!(target_os = "windows") {
        "windows"
    } else if cfg!(target_os = "macos") {
        "macos"
    } else {
        std::env::consts::OS
    }
}
impl ClientFeedbackClient {
    pub fn new(options: ClientFeedbackOptions) -> Result<Self> {
        if options.package_name.trim().is_empty()
            || options.version.trim().is_empty()
            || options.queue_directory.as_os_str().is_empty()
            || options.max_pending == 0
            || options.timeout.is_zero()
        {
            return Err("package, version, queue and positive limits required".into());
        }
        if options.resolve_credentials.is_none() {
            options.credentials.validate()?;
        }
        let http = Client::builder()
            .timeout(options.timeout)
            .redirect(Policy::none())
            .build()?;
        let session_id = Utc::now().timestamp();
        Ok(Self {
            options,
            http,
            session_id,
            client_id: format!("{}.{}", Uuid::new_v4().as_u128() >> 64, session_id),
        })
    }
    fn identity(&self) -> Option<String> {
        // A consent provider must be side-effect free and must not panic.
        normalize_id(
            self.options
                .ga4_client_id_provider
                .as_ref()
                .and_then(|f| f()),
        )
    }
    fn pending(&self) -> Result<Vec<(PathBuf, FeedbackPayload)>> {
        let mut rows = Vec::new();
        if !self.options.queue_directory.exists() {
            return Ok(rows);
        }
        for entry in fs::read_dir(&self.options.queue_directory)? {
            let path = entry?.path();
            if !path
                .file_name()
                .is_some_and(|n| n.to_string_lossy().ends_with(".pending.json"))
            {
                continue;
            }
            if let Ok(bytes) = fs::read(&path) {
                if let Ok(payload) = serde_json::from_slice::<FeedbackPayload>(&bytes) {
                    if Uuid::parse_str(&payload.report_id).is_ok() {
                        rows.push((path, payload));
                    }
                }
            }
        }
        rows.sort_by(|a, b| a.1.occurred_at_utc.cmp(&b.1.occurred_at_utc));
        Ok(rows)
    }
    fn enqueue(
        &mut self,
        kind: FeedbackKind,
        operation: &str,
        message: Option<&str>,
        exception_type: Option<&str>,
        stack: Option<&str>,
        source: &str,
        correlation_id: Option<&str>,
    ) -> Result<Option<String>> {
        let correlation_id = self.resolve_correlation(correlation_id)?;
        let operation = operation.trim();
        if operation.is_empty() || source.trim().is_empty() {
            return Err("operation and source required".into());
        }
        let request = matches!(
            kind,
            FeedbackKind::BugfixRequest | FeedbackKind::FeatureRequest
        );
        if request
            && (operation.chars().count() > 128
                || message.is_none_or(|m| m.trim().is_empty() || m.trim().chars().count() > 1024))
        {
            return Err("title must be 1..128 and description 1..1024 characters".into());
        }
        let payload = FeedbackPayload {
            report_id: Uuid::new_v4().to_string(),
            kind,
            package_name: self.options.package_name.trim().into(),
            version: self.options.version.trim().into(),
            architecture: self.options.architecture.clone(),
            format: self.options.format.clone(),
            runtime: Some("Rust".into()),
            os: Some(os_name().into()),
            os_version: None,
            occurred_at_utc: Utc::now().to_rfc3339_opts(SecondsFormat::Millis, true),
            operation: operation.into(),
            exception_type: exception_type.map(str::to_owned),
            message: message.map(|s| s.trim().chars().take(1024).collect()),
            stack_trace: stack.map(|s| s.chars().take(8000).collect()),
            source: source.trim().into(),
            ga4_client_id: self.identity(),
            correlation_id,
        };
        let body = serde_json::to_vec(&payload)?;
        if body.len() > 65536 {
            return Err("report exceeds 65536 bytes".into());
        }
        let rows = self.pending()?;
        if rows.iter().any(|(_, p)| {
            p.kind == kind
                && p.operation == operation
                && p.exception_type == payload.exception_type
                && p.message == payload.message
                && p.stack_trace == payload.stack_trace
                && p.correlation_id == payload.correlation_id
        }) {
            return Ok(None);
        }
        fs::create_dir_all(&self.options.queue_directory)?;
        let target = self.options.queue_directory.join(format!(
            "{}.pending.json",
            Uuid::parse_str(&payload.report_id)?.simple()
        ));
        let temporary = target.with_extension("tmp");
        let write = (|| -> Result<()> {
            let mut options = fs::OpenOptions::new();
            options.write(true).create_new(true);
            #[cfg(unix)]
            {
                use std::os::unix::fs::OpenOptionsExt;
                options.mode(0o600);
            }
            let mut file = options.open(&temporary)?;
            file.write_all(&body)?;
            file.sync_all()?;
            fs::rename(&temporary, &target)?;
            Ok(())
        })();
        if temporary.exists() {
            let _ = fs::remove_file(&temporary);
        }
        write?;
        for (path, _) in rows
            .iter()
            .take((rows.len() + 1).saturating_sub(self.options.max_pending))
        {
            fs::remove_file(path)?;
        }
        Ok(Some(payload.report_id))
    }
    pub fn report_feature_request(
        &mut self,
        title: &str,
        description: &str,
    ) -> Result<Option<String>> {
        self.enqueue(
            FeedbackKind::FeatureRequest,
            title,
            Some(description),
            None,
            None,
            "user",
            None,
        )
    }
    pub fn report_bugfix_request(
        &mut self,
        title: &str,
        description: &str,
    ) -> Result<Option<String>> {
        self.enqueue(
            FeedbackKind::BugfixRequest,
            title,
            Some(description),
            None,
            None,
            "user",
            None,
        )
    }
    pub fn report_error(&mut self, operation: &str, message: &str) -> Result<Option<String>> {
        self.enqueue(
            FeedbackKind::Error,
            operation,
            Some(message),
            None,
            None,
            "curated",
            None,
        )
    }
    /// Like `report_error`, tagged with an id shared with the other tiers of the same request.
    pub fn report_error_correlated(
        &mut self,
        operation: &str,
        message: &str,
        correlation_id: &str,
    ) -> Result<Option<String>> {
        self.enqueue(
            FeedbackKind::Error,
            operation,
            Some(message),
            None,
            None,
            "curated",
            Some(correlation_id),
        )
    }
    /// Like `report_crash`, tagged with an id shared with the other tiers of the same request.
    pub fn report_crash_correlated(
        &mut self,
        message: &str,
        source: &str,
        stack_trace: Option<&str>,
        correlation_id: &str,
    ) -> Result<Option<String>> {
        self.enqueue(
            FeedbackKind::Crash,
            "crash",
            Some(message),
            None,
            stack_trace,
            source,
            Some(correlation_id),
        )
    }
    fn resolve_correlation(&self, explicit: Option<&str>) -> Result<Option<String>> {
        if let Some(value) = explicit {
            return normalize_correlation_id(value).map(Some).ok_or_else(|| {
                "correlation_id must be 1..128 characters without whitespace or control characters"
                    .into()
            });
        }
        if let Some(ambient) = current_correlation_id() {
            return Ok(Some(ambient));
        }
        Ok(self
            .options
            .correlation_id_provider
            .as_ref()
            .and_then(|provider| {
                std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| provider())).ok()
            })
            .flatten()
            .and_then(|value| normalize_correlation_id(&value)))
    }
    pub fn report_crash(
        &mut self,
        message: &str,
        source: &str,
        stack_trace: Option<&str>,
    ) -> Result<Option<String>> {
        self.enqueue(
            FeedbackKind::Crash,
            "crash",
            Some(message),
            None,
            stack_trace,
            source,
            None,
        )
    }
    fn post(
        &self,
        path: &str,
        body: &impl Serialize,
        https_only: bool,
    ) -> Result<(Option<u16>, Option<String>)> {
        let resolved = self.options.resolve_credentials.as_ref().map(|f| f());
        let credentials = match resolved {
            Some(Some(ref c)) => c,
            Some(None) => return Ok((None, None)),
            None => &self.options.credentials,
        };
        credentials.validate()?;
        if https_only && Url::parse(&credentials.base_url)?.scheme() != "https" {
            return Ok((None, None));
        }
        let bytes = serde_json::to_vec(body)?;
        if bytes.len() > 65536 {
            return Ok((Some(413), None));
        }
        let request = self
            .http
            .post(format!(
                "{}{}",
                credentials.base_url.trim_end_matches('/'),
                path
            ))
            .basic_auth(&credentials.username, Some(&credentials.token))
            .header("Content-Type", "application/json")
            .body(bytes);
        match request.send() {
            Ok(response) => {
                let status = response.status().as_u16();
                let mut error = None;
                if !(200..300).contains(&status) {
                    let mut bytes = Vec::new();
                    if response.take(4097).read_to_end(&mut bytes).is_ok() && bytes.len() <= 4096 {
                        if let Ok(value) = serde_json::from_slice::<serde_json::Value>(&bytes) {
                            error = value
                                .get("error")
                                .and_then(|v| v.as_str())
                                .map(str::to_owned);
                        }
                    }
                }
                Ok((Some(status), error))
            }
            Err(_) => Ok((None, None)),
        }
    }
    pub fn flush_pending(&mut self) -> Result<Vec<UploadResult>> {
        let mut results = Vec::new();
        for (path, payload) in self.pending()? {
            let (status_code, error) = self
                .post("/v1/client-feedback", &payload, false)
                .unwrap_or((None, None));
            let result = UploadResult {
                report_id: payload.report_id,
                status_code,
                error,
            };
            if result.uploaded() {
                fs::remove_file(path)?;
            }
            results.push(result);
        }
        Ok(results)
    }
    pub fn track_usage(&self, event_name: &str, protocol: Option<&str>) -> Result<bool> {
        self.track_usage_with_engagement(event_name, protocol, None)
    }
    /// Like `track_usage`, with the foreground time in milliseconds since the application started.
    pub fn track_usage_with_engagement(
        &self,
        event_name: &str,
        protocol: Option<&str>,
        engagement_time_ms: Option<u64>,
    ) -> Result<bool> {
        if !matches!(
            event_name,
            "app_open" | "update_check" | "device_connection_start"
        ) {
            return Err("unsupported event_name".into());
        }
        if event_name == "device_connection_start"
            && !protocol
                .is_some_and(|p| matches!(p, "ssh" | "sftp" | "ipmi" | "proxmox" | "winbox"))
        {
            return Ok(false);
        }
        if !matches!(os_name(), "windows" | "linux" | "macos")
            || self
                .options
                .architecture
                .as_ref()
                .is_none_or(|s| s.trim().is_empty())
            || self
                .options
                .format
                .as_ref()
                .is_none_or(|s| s.trim().is_empty())
        {
            return Ok(false);
        }
        let body = self.usage_body(event_name, protocol, engagement_time_ms);
        Ok(self
            .post("/v1/client-usage-event", &body, true)
            .ok()
            .and_then(|r| r.0)
            .is_some_and(|s| (200..300).contains(&s)))
    }
}

impl ClientFeedbackClient {
    fn usage_body(
        &self,
        event_name: &str,
        protocol: Option<&str>,
        engagement_time_ms: Option<u64>,
    ) -> serde_json::Value {
        let identity = self.identity();
        let mut body = serde_json::json!({"package_name": self.options.package_name.trim(),
            "event_name": event_name, "protocol": if event_name == "device_connection_start" { protocol } else { None },
            "client_id": identity.as_ref().unwrap_or(&self.client_id), "session_id": self.session_id,
            "occurred_at_utc": Utc::now().to_rfc3339_opts(SecondsFormat::Millis, true),
            "version": self.options.version.trim(), "architecture": self.options.architecture, "format": self.options.format, "os": os_name()});
        if let Some(ms) = engagement_time_ms {
            body["engagement_time_msec"] = ms.into();
        }
        body
    }
}

/// Read a serialized report using the same contract as the other SDKs.
pub fn read_report(path: impl AsRef<Path>) -> Result<FeedbackPayload> {
    Ok(serde_json::from_slice(&fs::read(path)?)?)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn usage_body_is_json_only_and_carries_package_and_timing() {
        let dir = std::env::temp_dir().join(format!("artifactd-usage-{}", Uuid::new_v4()));
        let mut options = ClientFeedbackOptions::new(
            "my-app",
            "1.0.0",
            dir,
            Credentials {
                base_url: "https://repo.example".into(),
                username: "pull".into(),
                token: "secret".into(),
            },
        );
        options.architecture = Some("amd64".into());
        options.format = Some("deb".into());
        options.ga4_client_id_provider = Some(Arc::new(|| Some("123.456".into())));
        let client = ClientFeedbackClient::new(options).unwrap();
        let body = client.usage_body("app_open", None, Some(2345));
        assert_eq!(body["package_name"], "my-app");
        assert_eq!(body["client_id"], "123.456");
        assert_eq!(body["engagement_time_msec"], 2345);
        assert!(body["occurred_at_utc"].as_str().unwrap().ends_with('Z'));
        assert!(body["protocol"].is_null());
        assert!(client
            .usage_body("app_open", None, None)
            .get("engagement_time_msec")
            .is_none());
    }

    #[test]
    fn correlation_id_explicit_ambient_provider_dedup_and_validation() {
        let dir = std::env::temp_dir().join(format!("artifactd-corr-{}", Uuid::new_v4()));
        let credentials = Credentials {
            base_url: "https://repo.example".into(),
            username: "pull".into(),
            token: "secret".into(),
        };
        let mut client = ClientFeedbackClient::new(ClientFeedbackOptions::new(
            "my-app",
            "1.0.0",
            &dir,
            credentials.clone(),
        ))
        .unwrap();
        assert!(client
            .report_error_correlated("a", "x", "req-1")
            .unwrap()
            .is_some());
        assert!(client
            .report_error_correlated("a", "x", "req-1")
            .unwrap()
            .is_none());
        assert!(client
            .report_error_correlated("a", "x", "req-9")
            .unwrap()
            .is_some());
        {
            let _scope = correlation_scope("req-2").unwrap();
            assert_eq!(current_correlation_id().as_deref(), Some("req-2"));
            assert!(client.report_error("b", "x").unwrap().is_some());
        }
        assert_eq!(current_correlation_id(), None);
        assert!(client.report_error("c", "x").unwrap().is_some());
        assert!(client
            .report_error_correlated("d", "x", "has space")
            .is_err());
        assert!(correlation_scope("").is_err());
        let mut by_op: Vec<(String, Option<String>)> = client
            .pending()
            .unwrap()
            .into_iter()
            .map(|(_, p)| (p.operation, p.correlation_id))
            .collect();
        by_op.sort();
        assert_eq!(
            by_op,
            vec![
                ("a".into(), Some("req-1".into())),
                ("a".into(), Some("req-9".into())),
                ("b".into(), Some("req-2".into())),
                ("c".into(), None),
            ]
        );
        let json = fs::read_to_string(
            &client
                .pending()
                .unwrap()
                .into_iter()
                .find(|(_, p)| p.operation == "c")
                .unwrap()
                .0,
        )
        .unwrap();
        assert!(!json.contains("correlation_id"));
        assert_eq!(new_correlation_id().len(), 32);

        let faulty_dir = dir.join("faulty");
        let mut options = ClientFeedbackOptions::new("my-app", "1.0.0", &faulty_dir, credentials);
        options.correlation_id_provider = Some(Arc::new(|| panic!("boom")));
        let mut faulty = ClientFeedbackClient::new(options).unwrap();
        assert!(faulty.report_error("p", "x").unwrap().is_some());
        assert_eq!(faulty.pending().unwrap()[0].1.correlation_id, None);
        let _ = fs::remove_dir_all(&dir);
    }

    #[test]
    fn usage_fixtures_list_exactly_the_keys_the_sdk_sends() {
        let keys = |text: &str| -> Vec<String> {
            let mut keys: Vec<String> = serde_json::from_str::<serde_json::Value>(text)
                .unwrap()
                .as_object()
                .unwrap()
                .keys()
                .cloned()
                .collect();
            keys.sort();
            keys
        };
        let plain = keys(include_str!("../../../spec/fixtures/usage/app-open.json"));
        assert_eq!(
            plain,
            keys(include_str!(
                "../../../spec/fixtures/usage/update-check.json"
            ))
        );
        assert_eq!(
            plain,
            keys(include_str!(
                "../../../spec/fixtures/google/app-open-with-ga4.json"
            ))
        );
        let engaged = keys(include_str!(
            "../../../spec/fixtures/usage/app-open-with-engagement.json"
        ));
        assert_eq!(
            engaged,
            keys(include_str!(
                "../../../spec/fixtures/google/app-open-with-ga4-and-engagement.json"
            ))
        );
        assert!(!plain.contains(&"ga4_client_id".to_string()));

        let dir = std::env::temp_dir().join(format!("artifactd-usage-{}", Uuid::new_v4()));
        let mut options = ClientFeedbackOptions::new(
            "my-app",
            "1.0.0",
            dir,
            Credentials {
                base_url: "https://repo.example".into(),
                username: "pull".into(),
                token: "secret".into(),
            },
        );
        options.architecture = Some("amd64".into());
        options.format = Some("deb".into());
        let client = ClientFeedbackClient::new(options).unwrap();
        let sent = |body: serde_json::Value| -> Vec<String> {
            let mut keys: Vec<String> = body.as_object().unwrap().keys().cloned().collect();
            keys.sort();
            keys
        };
        assert_eq!(plain, sent(client.usage_body("app_open", None, None)));
        assert_eq!(
            engaged,
            sent(client.usage_body("app_open", None, Some(45000)))
        );
    }
}
