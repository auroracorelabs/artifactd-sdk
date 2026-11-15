//! Runs the language-independent tables in spec/conformance against this SDK.
use artifactd_client_feedback::{ClientFeedbackClient, ClientFeedbackOptions, Credentials};
use serde_json::Value;
use std::{
    fs,
    io::{Read, Write},
    net::TcpListener,
    path::PathBuf,
    sync::Arc,
    thread,
};
use uuid::Uuid;

fn table(text: &str) -> Value {
    serde_json::from_str(text).unwrap()
}
fn expand(value: &Value) -> String {
    match value {
        Value::String(s) => s.clone(),
        Value::Null => String::new(),
        _ => match value.get("repeat") {
            Some(r) => r[0]
                .as_str()
                .unwrap()
                .repeat(r[1].as_u64().unwrap() as usize),
            None => value["concat"]
                .as_array()
                .unwrap()
                .iter()
                .map(expand)
                .collect(),
        },
    }
}
fn body_of(response: &Value) -> String {
    match response.get("body_repeat") {
        Some(r) => r[0]
            .as_str()
            .unwrap()
            .repeat(r[1].as_u64().unwrap() as usize),
        None => response["body"].as_str().unwrap_or("").to_string(),
    }
}

struct Queue(PathBuf);
impl Queue {
    fn new() -> Self {
        let path = std::env::temp_dir().join(format!("artifactd-conformance-{}", Uuid::new_v4()));
        fs::create_dir_all(&path).unwrap();
        Self(path)
    }
    fn options(&self, base_url: &str) -> ClientFeedbackOptions {
        let mut options = ClientFeedbackOptions::new(
            "my-app",
            "2.0.0",
            &self.0,
            Credentials {
                base_url: base_url.into(),
                username: "pull".into(),
                token: "secret".into(),
            },
        );
        options.architecture = Some("amd64".into());
        options.format = Some("deb".into());
        options
    }
    fn queued(&self) -> Vec<Value> {
        let mut rows = Vec::new();
        for entry in fs::read_dir(&self.0).unwrap() {
            let path = entry.unwrap().path();
            if path.to_string_lossy().ends_with(".pending.json") {
                rows.push(serde_json::from_str(&fs::read_to_string(path).unwrap()).unwrap());
            }
        }
        rows
    }
}
impl Drop for Queue {
    fn drop(&mut self) {
        let _ = fs::remove_dir_all(&self.0);
    }
}

fn reason(status: u64) -> &'static str {
    match status {
        200 => "OK",
        201 => "Created",
        202 => "Accepted",
        204 => "No Content",
        302 => "Found",
        _ => "Status",
    }
}
/// Serves one canned answer on a local socket; `None` returns a closed port (transport failure).
fn serve(response: &Value) -> (String, Option<thread::JoinHandle<()>>) {
    if response.is_null() {
        return ("http://127.0.0.1:1".into(), None);
    }
    let listener = TcpListener::bind("127.0.0.1:0").unwrap();
    let address = listener.local_addr().unwrap();
    let status = response["status"].as_u64().unwrap();
    let body = body_of(response);
    let mut head = format!(
        "HTTP/1.1 {status} {}\r\nContent-Length: {}\r\nConnection: close\r\n",
        reason(status),
        body.len()
    );
    if let Some(headers) = response.get("headers").and_then(Value::as_object) {
        for (name, value) in headers {
            head.push_str(&format!("{name}: {}\r\n", value.as_str().unwrap()));
        }
    }
    let wire = format!("{head}\r\n{body}");
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
        stream.write_all(wire.as_bytes()).unwrap();
    });
    (format!("http://{address}"), Some(handle))
}

#[test]
fn upload_responses() {
    for case in table(include_str!(
        "../../../spec/conformance/upload-responses.json"
    ))["cases"]
        .as_array()
        .unwrap()
    {
        let name = case["name"].as_str().unwrap();
        let queue = Queue::new();
        let (url, server) = serve(&case["response"]);
        let mut client = ClientFeedbackClient::new(queue.options(&url)).unwrap();
        client
            .report_feature_request("Export CSV", "Take my data")
            .unwrap();
        let result = &client.flush_pending().unwrap()[0];
        if let Some(server) = server {
            server.join().unwrap();
        }
        let expect = &case["expect"];
        assert_eq!(
            result.uploaded(),
            expect["uploaded"].as_bool().unwrap(),
            "{name}: uploaded"
        );
        assert_eq!(
            result.status_code.map(u64::from),
            expect["status_code"].as_u64(),
            "{name}: status"
        );
        assert_eq!(
            result.error.as_deref(),
            expect["error"].as_str(),
            "{name}: error"
        );
        assert_eq!(
            queue.queued().len(),
            usize::from(expect["retained"].as_bool().unwrap()),
            "{name}: queue"
        );
    }
}

/// Usage is HTTPS-only and this test has no TLS server, so only the transport failure of the usage table can be
/// exercised end to end; the HTTP-status cases are covered by the SDKs whose transport can be substituted.
#[test]
fn usage_transport_failure_is_false_and_never_queued() {
    let table = table(include_str!(
        "../../../spec/conformance/usage-responses.json"
    ));
    let case = table["cases"]
        .as_array()
        .unwrap()
        .iter()
        .find(|c| c["response"].is_null())
        .unwrap();
    let queue = Queue::new();
    let client = ClientFeedbackClient::new(queue.options("https://127.0.0.1:1")).unwrap();
    assert_eq!(
        client.track_usage("app_open", None).unwrap(),
        case["expect"]["sent"].as_bool().unwrap()
    );
    assert!(queue.queued().is_empty());
}

#[test]
fn user_request_validation() {
    for case in table(include_str!("../../../spec/conformance/validation.json"))["user_requests"]
        .as_array()
        .unwrap()
    {
        for bugfix in [false, true] {
            let name = case["name"].as_str().unwrap();
            let queue = Queue::new();
            let mut client =
                ClientFeedbackClient::new(queue.options("https://repo.example")).unwrap();
            let (title, description) = (expand(&case["title"]), expand(&case["description"]));
            let result = if bugfix {
                client.report_bugfix_request(&title, &description)
            } else {
                client.report_feature_request(&title, &description)
            };
            if case["valid"].as_bool().unwrap() {
                assert!(result.unwrap().is_some(), "{name}: accepted");
                assert_eq!(queue.queued().len(), 1, "{name}: queued");
            } else {
                assert!(result.is_err(), "{name}: rejected");
                assert!(queue.queued().is_empty(), "{name}: nothing queued");
            }
        }
    }
}

#[test]
fn correlation_ids() {
    let ids = &table(include_str!("../../../spec/conformance/validation.json"))["correlation_ids"];
    for case in ids["valid"].as_array().unwrap() {
        let name = case["name"].as_str().unwrap();
        let queue = Queue::new();
        let mut client = ClientFeedbackClient::new(queue.options("https://repo.example")).unwrap();
        assert!(
            client
                .report_error_correlated("op", "x", &expand(&case["value"]))
                .unwrap()
                .is_some(),
            "{name}"
        );
        assert_eq!(
            queue.queued()[0]["correlation_id"].as_str().unwrap(),
            expand(&case["sent"]),
            "{name}"
        );
    }
    for case in ids["invalid"].as_array().unwrap() {
        let name = case["name"].as_str().unwrap();
        let queue = Queue::new();
        let mut client = ClientFeedbackClient::new(queue.options("https://repo.example")).unwrap();
        assert!(
            client
                .report_error_correlated("op", "x", &expand(&case["value"]))
                .is_err(),
            "{name}: rejected"
        );
        assert!(queue.queued().is_empty(), "{name}: nothing queued");
    }
}

#[test]
fn ga4_client_ids() {
    let ids = &table(include_str!("../../../spec/conformance/validation.json"))["ga4_client_ids"];
    for (accepted, key) in [(true, "accepted"), (false, "omitted")] {
        for case in ids[key].as_array().unwrap() {
            // A provider that throws has no Rust equivalent (providers must not panic); it maps to `None`.
            let name = case["name"].as_str().unwrap();
            let value = if case["throws"].as_bool() == Some(true) {
                None
            } else {
                Some(expand(&case["value"]))
            };
            let queue = Queue::new();
            let mut options = queue.options("https://repo.example");
            let provided = value.clone();
            options.ga4_client_id_provider = Some(Arc::new(move || provided.clone()));
            let mut client = ClientFeedbackClient::new(options).unwrap();
            client.report_feature_request("Export", "CSV").unwrap();
            let payload = &queue.queued()[0];
            if accepted {
                assert_eq!(
                    payload["ga4_client_id"].as_str(),
                    value.as_deref(),
                    "{name}"
                );
            } else {
                assert!(
                    payload.get("ga4_client_id").is_none_or(Value::is_null),
                    "{name}: omitted"
                );
            }
        }
    }
}

#[test]
fn diagnostics_are_truncated_not_rejected() {
    let limits = &table(include_str!("../../../spec/conformance/validation.json"))["diagnostics"];
    let queue = Queue::new();
    let mut client = ClientFeedbackClient::new(queue.options("https://repo.example")).unwrap();
    assert!(client
        .report_error("op", &"m".repeat(5000))
        .unwrap()
        .is_some());
    let message = queue.queued()[0]["message"]
        .as_str()
        .unwrap()
        .chars()
        .count();
    assert_eq!(message as u64, limits["message_limit"].as_u64().unwrap());
}
