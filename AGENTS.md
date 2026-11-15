# Agent instructions (Artifactd SDK client)

This repository is **public on GitHub**. Treat every commit, issue, pull request, and published package as visible to external consumers.

## Never commit or publish

- Secrets, API keys, tokens, or passwords
- Connection strings or database credentials
- Internal hostnames, private IP addresses, or local filesystem paths
- Customer names, private email addresses, or other identifiable operator data

## External-facing content only

- Documentation, README text, examples, and commit messages must stand alone for public readers.
- Do not reference internal Aurora GitLab projects, the shared `pipeline` repository, or other private infrastructure unless the task is explicitly to remove such references.
- Use generic public URLs (for example `https://docs.artifactd.net/`, `https://github.com/artifactd/artifactd-sdk-client`) in examples.

## CI and release

- Registry publish credentials belong only in GitHub Actions secrets or the `sdk-release` environment — never in the tree.
- Follow repository `VERSION`, `CHANGELOG.md`, and existing workflow patterns for releases.