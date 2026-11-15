# Building and distributing the SDKs

All five SDKs share a stable version from root `VERSION`. Change it with `python3 ci/version.py --set X.Y.Z`, which updates Python, Node, Rust and PHP manifests including the PHP example dependency and the Rust lockfile. The .NET package reads root `VERSION`; its CI build passes an explicit release/prerelease version. Run `python3 ci/version.py --check` before releasing.

The SDK workflow tests and packages every language on pull requests, `main`, and `v*` tags. Tags must exactly match `VERSION`. CI uploads NuGet packages, Python wheels/source distributions, npm tarballs, Rust crates and Composer ZIP archives as separate downloadable artifacts, retained for 180 days. These artifacts can be distributed directly without publishing to public registries.

Python/Node/Rust/PHP branch artifacts retain the stable manifest version for local evaluation; publish those packages only from the corresponding version tag. The .NET branch builds use `VERSION-ci.<run>` prereleases. The SDK workflow produces downloadable packages without deploying to any server.

## Where users get the packages

- **Registries** (PyPI, npm, crates.io, NuGet) on every release tag — see [Releasing](#releasing).
- **GitHub Releases**: every `vX.Y.Z` tag attaches all packages, `SHA256SUMS` and `manifest.json`. This is the source for PHP (Composer artifact repository) and offline installs.
- **GitHub Actions artifacts** are build output for maintainers and reviewers (pull requests and `main`), kept for 180 days. They are not the distribution channel for users.

| Actions artifact | Contents |
| --- | --- |
| `dotnet-packages` | `.nupkg` with the compiled .NET Standard 2.0 DLL and `.snupkg` symbols |
| `python-packages` | `.whl` and source distribution |
| `node-packages` | npm `.tgz` with TypeScript declarations |
| `rust-packages` | Cargo `.crate` |
| `php-packages` | Composer-installable `.zip` |
| `client-feedback-packages` | All seven files by language, plus `SHA256SUMS` and `manifest.json` |

The combined bundle is assembled only after every language check and the Windows .NET Framework consumer check pass; missing, duplicate or malformed packages fail the job. Verify a downloaded bundle with `sha256sum -c SHA256SUMS`. Branch builds of .NET carry a `-ci.<run>` suffix; tag builds use the plain `VERSION`.

## Local packages

From the repository root:

```sh
dotnet pack packages/dotnet/ArtifactD.ClientFeedback -c Release -o nupkgs -p:Version="$(cat VERSION)"
python3 -m pip install build twine
python3 -m build packages/python
python3 -m twine check packages/python/dist/*
npm pack ./packages/node
cargo package --manifest-path packages/rust/Cargo.toml --locked --allow-dirty
php packages/php/tests/client.php
python3 ci/package-php.py
bash ci/smoke-php.sh
```

The Rust crate includes only its source, README, manifest and lockfile. Its repository integration tests consume shared fixtures, so run them from the checkout before packaging. PHP, Python and Node consumers need no repository-level files.

## Releasing

1. Update `CHANGELOG.md`, then `python3 ci/version.py --set X.Y.Z` and commit.
2. Push the tag `vX.Y.Z`. The **SDK** workflow tests everything, then:
   - creates a **GitHub Release** with every package, `SHA256SUMS` and `manifest.json` attached (permanent, no login-wrapped ZIP);
   - calls **Publish SDK**, which pushes to PyPI, npm, crates.io and NuGet. Each job waits for approval on the `sdk-release` environment and is skipped with a warning when its secret is not configured.
3. Secrets (environment `sdk-release`): `PYPI_TOKEN`, `NPM_TOKEN`, `CARGO_REGISTRY_TOKEN`. NuGet uses trusted publishing (OIDC): set the environment variable `NUGET_USER` to the nuget.org profile name; no API key is stored. Confirm ownership of the package names (`artifactd-client-feedback` on PyPI/crates.io, `@artifactd` on npm, `ArtifactD.ClientFeedback` on NuGet) first.
4. To retry a single registry, run **Publish SDK** manually from the tag and choose the language.

The SDKs are MIT licensed; each package ships its own `LICENSE` (NuGet declares it in metadata). CI verifies the copies match the root `LICENSE`.

## Composer and Packagist

PHP ZIP packages are immediately installable through a Composer artifact repository; see packages/php/README.md. No Packagist publication is performed by these workflows. Packagist expects composer.json at the root of a package repository: this multi-language repository keeps it under packages/php, so a future Packagist release requires a dedicated subtree split or Composer repository metadata pointing to the generated ZIP. Do not register the whole multi-language repository as the PHP package without that packaging step.
