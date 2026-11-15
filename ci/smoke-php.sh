#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
root="$PWD"
smoke="$(mktemp -d)"
trap 'rm -rf "$smoke"' EXIT
cd "$smoke"
composer init --name artifactd/package-smoke --require="artifactd/client-feedback:$(cat "$root/VERSION")" --no-interaction
composer config repositories.packagist.org false
composer config repositories.artifactd artifact "$root/packages/php/dist"
composer install --no-interaction --no-progress
php -r '
require "vendor/autoload.php";
$options = new ArtifactD\ClientFeedback\ClientFeedbackOptions("my-app", "2.0.0", "queue", resolveCredentials: fn() => null);
$client = new ArtifactD\ClientFeedback\ClientFeedbackClient($options);
$id = $client->reportFeatureRequest("Export", "CSV");
$result = $client->flushPending()[0];
if (!$id || $result->reportId !== $id || $result->statusCode !== null || $result->uploaded()) exit(1);
echo "Installed Composer archive and PSR-4 autoload smoke test passed.\n";
'
