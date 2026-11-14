# Contributing

Thanks for helping improve the Artifactd SDKs.

1. Open an issue first for larger changes.
2. The five SDKs (`packages/dotnet`, `node`, `python`, `rust`, `php`) share behaviour defined by `spec/`; keep them consistent and add fixtures when behaviour changes.
3. Run the tests of every SDK you touch (commands in [docs/RELEASING.md](docs/RELEASING.md#local-packages)).
4. Add an entry under **Unreleased** in `CHANGELOG.md`.
5. Contributions are licensed under the [MIT License](LICENSE).

Versions are managed centrally: `python3 ci/version.py --set X.Y.Z`.
