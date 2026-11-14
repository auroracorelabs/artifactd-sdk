# Artifactd Client Feedback for Rust

Turn bug reports and feature requests into your next release. Blocking API with persistent report queues, HTTPS support and explicit delivery results.

## Install the crate

    cargo add artifactd-client-feedback

Offline: download `artifactd-client-feedback-<sdk-version>.crate` from the [GitHub Release](https://github.com/auroracorelabs/artifactd-sdk/releases), then unpack it into your application:

    mkdir -p sdk/rust-unpacked
    tar -xzf "./sdk/rust/artifactd-client-feedback-<sdk-version>.crate" -C sdk/rust-unpacked
    cargo add artifactd-client-feedback --path "./sdk/rust-unpacked/artifactd-client-feedback-<sdk-version>"

Cargo compiles the packaged source and resolves its dependencies; an SDK repository checkout is not required.

For development in a workspace checkout, add a path dependency:

```toml
[dependencies]
artifactd-client-feedback = { path = "path/to/artifactd-sdk/packages/rust" }
```

After publishing to crates.io, use `cargo add artifactd-client-feedback`.

```rust,no_run
use artifactd_client_feedback::{ClientFeedbackClient, ClientFeedbackOptions, Credentials};

fn main() -> artifactd_client_feedback::Result<()> {
    let credentials = Credentials {
        base_url: std::env::var("ARTIFACTD_HOST")?,
        username: std::env::var("ARTIFACTD_PULL_USERNAME")?,
        token: std::env::var("ARTIFACTD_PULL_TOKEN")?,
    };
    let mut feedback = ClientFeedbackClient::new(ClientFeedbackOptions::new(
        "my-app", "2.0.0", ".feedback", credentials,
    ))?;
    feedback.report_feature_request("Export CSV", "Let me take my data anywhere.")?;
    for result in feedback.flush_pending()? {
        println!("{}: {}", result.report_id, result.uploaded());
    }
    Ok(())
}
```

`report_bugfix_request(title, description)`, `report_error(operation, message)` and `report_crash(message, source, stack_trace)` use the same queue. Methods take a mutable client for queue serialization; one client exclusively owns each queue directory. The blocking HTTP client must be used from a worker thread in async applications.

`ClientFeedbackOptions` supports live credential and consented GA4 ID providers via `Arc` callbacks; return `None` for deferred credentials. Providers must not panic. Defaults: 20 pending reports, 15-second timeout. Duplicate pending reports return `Ok(None)`. Queue writes are atomic, rejected reports remain pending, and transport failures return a null HTTP status. No global panic hooks are installed.

Set architecture and format to enable best-effort `track_usage("app_open", None)`, `track_usage("update_check", None)` or `track_usage("device_connection_start", Some("ssh"))`. Usage requires HTTPS and Linux/Windows/macOS; `track_usage_with_engagement(event, protocol, Some(ms))` adds the foreground time in milliseconds. Use private queue directories and HTTPS in production.

```sh
cargo test --manifest-path packages/rust/Cargo.toml
cargo package --manifest-path packages/rust/Cargo.toml --allow-dirty
```

See the shared contract and release instructions in the repository for protocol details and publishing setup.
