<?php
declare(strict_types=1);
// Install the package first, then run: php feedback.php
require __DIR__ . '/vendor/autoload.php';

use ArtifactD\ClientFeedback\{ClientFeedbackClient, ClientFeedbackOptions, Credentials};

function required(string $name): string
{
    $value = getenv($name);
    if ($value === false || $value === '') {
        throw new RuntimeException('Set ' . $name . ' before running this example.');
    }
    return $value;
}

$feedback = new ClientFeedbackClient(new ClientFeedbackOptions(
    packageName: required('ARTIFACTD_PACKAGE_NAME'),
    version: required('ARTIFACTD_PACKAGE_VERSION'),
    queueDirectory: getenv('ARTIFACTD_QUEUE_DIRECTORY') ?: '.artifactd-feedback/php',
    credentials: new Credentials(
        required('ARTIFACTD_HOST'),
        required('ARTIFACTD_PULL_USERNAME'),
        required('ARTIFACTD_PULL_TOKEN'),
    ),
));
$feedback->reportFeatureRequest('Export CSV', 'Let me take my data anywhere.');
foreach ($feedback->flushPending() as $result) {
    printf("%s: uploaded=%s, status=%s\n", $result->reportId, $result->uploaded() ? 'true' : 'false', $result->statusCode ?? 'none');
}
