#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../.."
(
  cd docs/site
  dotnet tool restore
  dotnet tool run docfx dotnet/docfx.json
)
PYTHONPATH=packages/python/src python -m pdoc artifactd_client_feedback --output-directory build/docs-api/python --logo-link https://auroracorelabs.github.io/artifactd-sdk-client/
npm run api:node --prefix docs/site
cargo doc --manifest-path packages/rust/Cargo.toml --locked --no-deps --target-dir build/docs-rust

bash docs/site/build-php-api.sh
