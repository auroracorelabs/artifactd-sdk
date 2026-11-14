#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../.."
if command -v php >/dev/null 2>&1; then
  php docs/site/php-api.php
elif command -v docker >/dev/null 2>&1; then
  docker run --rm --user "$(id -u):$(id -g)" \
    -v "$PWD:/workspace" -w /workspace \
    php:8.2-cli-alpine php docs/site/php-api.php
else
  echo 'PHP API documentation requires PHP 8.2+ or Docker.' >&2
  exit 1
fi
