#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../.."
npm ci --prefix docs/site
if [[ ! -x docs/site/.venv/bin/python ]]; then
  python3 -m venv docs/site/.venv
fi
docs/site/.venv/bin/python -m pip install -r docs/site/requirements.txt
export PATH="$PWD/docs/site/.venv/bin:$PATH"
bash docs/site/build-api.sh
npm run build --prefix docs/site
