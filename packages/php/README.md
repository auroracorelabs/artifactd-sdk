# Artifactd Client Feedback for PHP

SDK for integrating feature requests, bug reports, curated errors and crashes with Artifactd.

Requires PHP 8.2+ with cURL and JSON. The API is synchronous and uses Composer PSR-4 autoloading, with no third-party runtime libraries.

## Install the Composer package

Download `artifactd-client-feedback-<version>.zip` from the [GitHub Releases](https://github.com/auroracorelabs/artifactd-sdk-client/releases) page and keep it in a directory such as sdk/php.

From your application directory:

    composer config repositories.artifactd artifact ./sdk/php
    composer require artifactd/client-feedback:<sdk-version>

Replace the version with the release version. Composer artifact repositories require its ZIP extension; enable ext-zip in the PHP installation running Composer. Composer installs the package code and generates the autoloader; an SDK repository checkout is not required. The package is not published on Packagist.

For development in this checkout:

    composer install --working-dir=examples/php
    php examples/php/feedback.php

## Quick start

```php
<?php
require __DIR__ . '/vendor/autoload.php';

use ArtifactD\ClientFeedback\{ClientFeedbackClient, ClientFeedbackOptions, Credentials};

$feedback = new ClientFeedbackClient(new ClientFeedbackOptions(
    packageName: 'my-app',
    version: '2.0.0',
    queueDirectory: __DIR__ . '/.feedback',
    credentials: new Credentials(
        baseUrl: getenv('ARTIFACTD_HOST'),
        username: getenv('ARTIFACTD_PULL_USERNAME'),
        token: getenv('ARTIFACTD_PULL_TOKEN'),
    ),
    architecture: 'amd64',
    format: 'deb',
));
$feedback->reportFeatureRequest('Export CSV', 'Let me take my data anywhere.');
$feedback->reportBugfixRequest('Save fails', 'Saving an empty draft shows an error.');
foreach ($feedback->flushPending() as $result) {
    printf("%s: uploaded=%s, status=%s\n",
        $result->reportId, $result->uploaded() ? 'true' : 'false',
        $result->statusCode ?? 'none');
}
```

Set the host and credentials before running. The host is your instance's HTTPS dataplane base URL. The package name and version must identify a published application release and are independent of the SDK version. Use pull credentials with access to that release.

## Reports and retries

- `reportFeatureRequest($title, $description)` and `reportBugfixRequest(...)` validate user text: nonblank title up to 128 Unicode characters and description up to 1024.
- `reportError($operation, $throwable = null)` records a curated error.
- `reportCrash($throwable, $source)` records a crash explicitly; no global exception or shutdown handlers are installed.
- Report methods persist the queue and return a UUID, or null for an identical pending report.
- `flushPending()` returns UploadResult objects with reportId, statusCode, error and uploaded(). Both 201 and repeated-report 200 are successful. Transport failures and rejections retain the original ID and queued file.

One client exclusively owns each queue directory. Defaults: 20 pending reports and a 15-second total request timeout. Overflow drops the oldest pending report after the new write succeeds. Writes are atomic; Unix queue directories/files use private permissions. Use private application storage and HTTPS in production.

PHP-FPM and typical web applications have concurrent workers. Do not point those workers at one shared queue: use a single owning background worker, or separate queues with a clear delivery owner. Run blocking flushes in an appropriate worker or command, rather than adding network latency to user-facing requests.

## Dynamic credentials and identity

```php
$options = new ClientFeedbackOptions(
    packageName: 'my-app',
    version: '2.0.0',
    queueDirectory: '/private/app-feedback',
    resolveCredentials: static fn(): ?Credentials => currentPullCredentials(),
    ga4ClientIdProvider: static fn(): ?string => consentedAnalyticsClientId(),
);
```

The callbacks above represent functions supplied by your application. Return null credentials to defer delivery until sign-in. Providers resolve per upload; tokens are never persisted in queue files. Consented GA4 IDs must match digits.digits and are captured when the report is queued, preserving attribution across later sign-in changes. Invalid identity values and provider exceptions are ignored.

## Usage events

`trackUsage('app_open')`, `trackUsage('update_check')` and `trackUsage('device_connection_start', 'ssh')` return a success boolean and never persist events. Configure architecture and format first. Usage requires HTTPS, Linux/Windows/macOS, `amd64`/`arm64`/`all` and a catalogue format (`deb`, `arch`, `msi`, `msix`, `dmg`, `nuget`). A third argument, `trackUsage('app_open', null, 12500)`, adds the foreground time in milliseconds. The server accepts usage for every package unless an operator switched off **Accept client usage events**.

## Development

    php packages/php/tests/client.php
    composer validate --no-check-publish packages/php/composer.json
    python3 ci/package-php.py

Tests include the shared protocol fixture, Unicode limits, queue persistence, retry IDs, credential changes and real cURL requests to a local test server. Transport can be injected through the optional second ClientFeedbackClient constructor argument for application tests.

See the [SDK repository](https://github.com/auroracorelabs/artifactd-sdk-client), [server documentation](https://docs.artifactd.net/), and [Artifactd downloads](https://artifactd.net/download).
