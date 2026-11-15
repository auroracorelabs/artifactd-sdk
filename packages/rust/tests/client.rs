use artifactd_client_feedback::{
    read_report, ClientFeedbackClient, ClientFeedbackOptions, Credentials, FeedbackKind,
};
use std::{
    fs,
    io::{Read, Write},
    net::TcpListener,
    path::PathBuf,
    sync::Arc,
    thread,
};
use uuid::Uuid;

struct Queue(PathBuf);
impl Queue {
    fn new() -> Self {
        let path = std::env::temp_dir().join(format!("artifactd-{}", Uuid::new_v4()));
        fs::create_dir_all(&path).unwrap();
        Self(path)
    }
    fn options(&self) -> ClientFeedbackOptions {
        ClientFeedbackOptions::new(
            "my-app",
            "2.0.0",
            &self.0,
            Credentials {
                base_url: "https://repo.example".into(),
                username: "pull".into(),
                token: "secret".into(),
            },
        )
    }
    fn files(&self) -> Vec<PathBuf> {
        fs::read_dir(&self.0)
            .unwrap()
            .map(|f| f.unwrap().path())
            .collect()
    }
}
impl Drop for Queue {
    fn drop(&mut self) {
        let _ = fs::remove_dir_all(&self.0);
    }
}

#[test]
fn validation_dedup_and_bounded_queue() {
    let queue = Queue::new();
    let mut options = queue.options();
    options.max_pending = 1;
    let mut client = ClientFeedbackClient::new(options).unwrap();
    assert!(client.report_feature_request("", "description").is_err());
    assert!(client
        .report_feature_request("Export", &"x".repeat(1025))
        .is_err());
    assert!(client
        .report_feature_request("Export", "CSV")
        .unwrap()
        .is_some());
    assert!(client
        .report_feature_request("Export", "CSV")
        .unwrap()
        .is_none());
    client.report_bugfix_request("Save", "Fails").unwrap();
    assert_eq!(queue.files().len(), 1);
    let text = fs::read_to_string(&queue.files()[0]).unwrap();
    assert!(!text.contains("secret"));
    assert_eq!(
        read_report(&queue.files()[0]).unwrap().kind,
        FeedbackKind::BugfixRequest
    );
}

#[test]
fn shared_fixture_and_deferred_credentials() {
    let queue = Queue::new();
    let fixture = include_str!("../../../spec/fixtures/feature-request.json");
    let path = queue
        .0
        .join("16d67ec01e024fa8b91cfd278f29bf39.pending.json");
    fs::write(&path, fixture).unwrap();
    let payload = read_report(&path).unwrap();
    assert_eq!(payload.kind, FeedbackKind::FeatureRequest);
    assert_eq!(payload.ga4_client_id.as_deref(), Some("123.456"));
    let mut options = queue.options();
    options.resolve_credentials = Some(Arc::new(|| None));
    let mut client = ClientFeedbackClient::new(options).unwrap();
    let result = client.flush_pending().unwrap();
    assert_eq!(result.len(), 1);
    assert!(!result[0].uploaded());
    assert_eq!(result[0].status_code, None);
    assert!(path.exists());
}

fn serve(status: &str, body: &str) -> (String, thread::JoinHandle<String>) {
    let listener = TcpListener::bind("127.0.0.1:0").unwrap();
    let address = listener.local_addr().unwrap();
    let response = format!(
        "HTTP/1.1 {status}\r\nContent-Length: {}\r\nConnection: close\r\n\r\n{body}",
        body.len()
    );
    let handle = thread::spawn(move || {
        let (mut stream, _) = listener.accept().unwrap();
        stream
            .set_read_timeout(Some(std::time::Duration::from_secs(5)))
            .unwrap();
        let mut data = Vec::new();
        let mut buffer = [0; 4096];
        loop {
            let count = stream.read(&mut buffer).unwrap();
            if count == 0 {
                break;
            }
            data.extend_from_slice(&buffer[..count]);
            if let Some(end) = data.windows(4).position(|s| s == b"\r\n\r\n") {
                let header = String::from_utf8_lossy(&data[..end]).to_lowercase();
                let length: usize = header
                    .lines()
                    .find_map(|s| s.strip_prefix("content-length: "))
                    .unwrap()
                    .trim()
                    .parse()
                    .unwrap();
                if data.len() >= end + 4 + length {
                    break;
                }
            }
        }
        stream.write_all(response.as_bytes()).unwrap();
        String::from_utf8(data).unwrap()
    });
    (format!("http://{address}"), handle)
}

#[test]
fn authenticates_preserves_attribution_retries_and_removes_only_on_success() {
    let queue = Queue::new();
    let path = queue
        .0
        .join("16d67ec01e024fa8b91cfd278f29bf39.pending.json");
    fs::write(
        &path,
        include_str!("../../../spec/fixtures/feature-request.json"),
    )
    .unwrap();
    fs::write(queue.0.join("corrupt.pending.json"), "broken").unwrap();
    let (url, server) = serve("403 Forbidden", "{\"error\":\"access_denied\"}");
    let mut options = queue.options();
    options.credentials.base_url = url;
    options.ga4_client_id_provider = Some(Arc::new(|| Some("999.999".into())));
    let mut client = ClientFeedbackClient::new(options).unwrap();
    let results = client.flush_pending().unwrap();
    assert_eq!(results[0].status_code, Some(403));
    assert_eq!(results[0].error.as_deref(), Some("access_denied"));
    assert!(path.exists());
    let received = server.join().unwrap();
    assert!(received.starts_with("POST /v1/client-feedback "));
    let lowercase = received.to_lowercase();
    assert!(lowercase.contains("authorization: basic chvsbdpzzwnyzxq="));
    // The analytics identity travels in the JSON body only.
    assert!(!lowercase.contains("x-ga4-client-id"));
    let body = received.split("\r\n\r\n").nth(1).unwrap();
    assert_eq!(
        serde_json::from_str::<serde_json::Value>(body).unwrap(),
        serde_json::from_str::<serde_json::Value>(include_str!(
            "../../../spec/fixtures/feature-request.json"
        ))
        .unwrap()
    );
    let (url, server) = serve("201 Created", "{}");
    let mut options = queue.options();
    options.credentials.base_url = url;
    let mut client = ClientFeedbackClient::new(options).unwrap();
    assert!(client.flush_pending().unwrap()[0].uploaded());
    server.join().unwrap();
    assert!(!path.exists());
}

#[test]
fn usage_allowlist_https_and_configuration() {
    let queue = Queue::new();
    let mut options = queue.options();
    options.credentials.base_url = "http://127.0.0.1:1".into();
    options.architecture = Some("amd64".into());
    options.format = Some("deb".into());
    let client = ClientFeedbackClient::new(options).unwrap();
    assert!(!client.track_usage("app_open", None).unwrap());
    assert!(!client
        .track_usage("device_connection_start", Some("telnet"))
        .unwrap());
    assert!(client.track_usage("arbitrary", None).is_err());
    assert!(queue.files().is_empty());
}

#[test]
fn rejects_invalid_urls_and_preserves_transport_failures() {
    let queue = Queue::new();
    for url in [
        "file:///tmp/no",
        "https://user:pass@repo.example",
        "https://repo.example?token=no",
    ] {
        let mut options = queue.options();
        options.credentials.base_url = url.into();
        assert!(ClientFeedbackClient::new(options).is_err());
    }
    let mut options = queue.options();
    options.credentials.base_url = "http://127.0.0.1:1".into();
    let mut client = ClientFeedbackClient::new(options).unwrap();
    let id = client.report_error("save", "failed").unwrap().unwrap();
    let results = client.flush_pending().unwrap();
    assert_eq!(results[0].report_id, id);
    assert_eq!(results[0].status_code, None);
    assert_eq!(queue.files().len(), 1);
}

const REPORT_KEYS: [&str; 15] = [
    "report_id",
    "kind",
    "package_name",
    "version",
    "architecture",
    "format",
    "runtime",
    "os",
    "os_version",
    "occurred_at_utc",
    "operation",
    "exception_type",
    "message",
    "stack_trace",
    "source",
];

#[test]
fn report_fixtures_match_the_wire_contract_and_survive_the_queue_unchanged() {
    let fixtures: [(&str, &str); 5] = [
        (
            "reports/crash.json",
            include_str!("../../../spec/fixtures/reports/crash.json"),
        ),
        (
            "reports/error.json",
            include_str!("../../../spec/fixtures/reports/error.json"),
        ),
        (
            "reports/bugfix-request.json",
            include_str!("../../../spec/fixtures/reports/bugfix-request.json"),
        ),
        (
            "reports/feature-request.json",
            include_str!("../../../spec/fixtures/reports/feature-request.json"),
        ),
        (
            "google/crash-with-ga4.json",
            include_str!("../../../spec/fixtures/google/crash-with-ga4.json"),
        ),
    ];
    for (name, text) in fixtures {
        let expected: serde_json::Value = serde_json::from_str(text).unwrap();
        let object = expected.as_object().unwrap();
        let mut keys: Vec<&str> = REPORT_KEYS.to_vec();
        if name.starts_with("google/") {
            keys.push("ga4_client_id");
        }
        let mut actual: Vec<&str> = object.keys().map(String::as_str).collect();
        actual.sort_unstable();
        keys.sort_unstable();
        assert_eq!(keys, actual, "{name}");
        assert!(
            object["occurred_at_utc"].as_str().unwrap().ends_with('Z'),
            "{name}"
        );
        if object["kind"].as_str().unwrap().ends_with("_request") {
            assert_eq!(object["source"], "user", "{name}");
            let title = object["operation"].as_str().unwrap().chars().count();
            let description = object["message"].as_str().unwrap().chars().count();
            assert!(
                (1..=128).contains(&title) && (1..=1024).contains(&description),
                "{name}"
            );
        }

        let queue = Queue::new();
        let id = object["report_id"].as_str().unwrap().replace('-', "");
        fs::write(queue.0.join(format!("{id}.pending.json")), text).unwrap();
        let (url, server) = serve("201 Created", "{}");
        let mut options = queue.options();
        options.credentials.base_url = url;
        let mut client = ClientFeedbackClient::new(options).unwrap();
        assert!(client.flush_pending().unwrap()[0].uploaded(), "{name}");
        let received = server.join().unwrap();
        let body = received.split("\r\n\r\n").nth(1).unwrap();
        assert_eq!(
            serde_json::from_str::<serde_json::Value>(body).unwrap(),
            expected,
            "{name}"
        );
        assert!(
            !received.to_lowercase().contains("x-ga4-client-id"),
            "{name}"
        );
    }
}

#[test]
fn a_generated_report_has_the_fixture_keys_and_omits_the_identity_unless_supplied() {
    let sorted = |value: serde_json::Value| -> Vec<String> {
        let mut keys: Vec<String> = value.as_object().unwrap().keys().cloned().collect();
        keys.sort();
        keys
    };
    let mut expected: Vec<String> = REPORT_KEYS.iter().map(|k| k.to_string()).collect();
    expected.sort();

    let queue = Queue::new();
    let mut client = ClientFeedbackClient::new(queue.options()).unwrap();
    client
        .report_feature_request("Export CSV", "Take my data")
        .unwrap();
    let text = fs::read_to_string(&queue.files()[0]).unwrap();
    assert_eq!(expected, sorted(serde_json::from_str(&text).unwrap()));

    let consented = Queue::new();
    let mut options = consented.options();
    options.ga4_client_id_provider = Some(Arc::new(|| Some("123.456".into())));
    let mut client = ClientFeedbackClient::new(options).unwrap();
    client
        .report_feature_request("Export CSV", "Take my data")
        .unwrap();
    let text = fs::read_to_string(&consented.files()[0]).unwrap();
    expected.push("ga4_client_id".into());
    expected.sort();
    assert_eq!(expected, sorted(serde_json::from_str(&text).unwrap()));
}
