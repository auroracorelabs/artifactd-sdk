use artifactd_client_feedback::{ClientFeedbackClient, ClientFeedbackOptions, Credentials};

fn main() -> artifactd_client_feedback::Result<()> {
    let credentials = Credentials {
        base_url: std::env::var("ARTIFACTD_HOST")?,
        username: std::env::var("ARTIFACTD_PULL_USERNAME")?,
        token: std::env::var("ARTIFACTD_PULL_TOKEN")?,
    };
    let mut feedback = ClientFeedbackClient::new(ClientFeedbackOptions::new(
        std::env::var("ARTIFACTD_PACKAGE_NAME")?,
        std::env::var("ARTIFACTD_PACKAGE_VERSION")?,
        std::env::var("ARTIFACTD_QUEUE_DIRECTORY")
            .unwrap_or_else(|_| ".artifactd-feedback/rust".into()),
        credentials,
    ))?;
    feedback.report_feature_request("Export CSV", "Let me take my data anywhere.")?;
    for result in feedback.flush_pending()? {
        println!(
            "{}: uploaded={}, status={:?}",
            result.report_id,
            result.uploaded(),
            result.status_code
        );
    }
    Ok(())
}
