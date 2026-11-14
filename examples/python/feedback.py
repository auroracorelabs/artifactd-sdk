import os
from artifactd_client_feedback import ClientFeedbackClient

feedback = ClientFeedbackClient(
    base_url=os.environ["ARTIFACTD_HOST"],
    username=os.environ["ARTIFACTD_PULL_USERNAME"],
    token=os.environ["ARTIFACTD_PULL_TOKEN"],
    package_name=os.environ["ARTIFACTD_PACKAGE_NAME"],
    version=os.environ["ARTIFACTD_PACKAGE_VERSION"],
    queue_directory=os.environ.get(
        "ARTIFACTD_QUEUE_DIRECTORY", ".artifactd-feedback/python"
    ),
)
feedback.report_feature_request("Export CSV", "Let me take my data anywhere.")
for result in feedback.flush_pending():
    print(result.report_id, result.uploaded, result.status_code)
