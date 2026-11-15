# ArtifactD.ClientFeedback for .NET

The library targets **.NET Standard 2.0**, so applications can consume the same NuGet package from modern .NET, including .NET 8 and .NET 10, and compatible .NET Framework applications. For .NET Framework, use 4.7.2 or newer. The package declares its JSON and channel dependencies; NuGet restores them automatically.

Consumers install the `.nupkg` through NuGet and do not need SDK source code or a .NET 10 application target. Building this repository requires a .NET 10 SDK; the test suite runs on .NET 8 and .NET 10, and the sample targets .NET 8. A Windows CI smoke test consumes the packed NuGet package from a .NET Framework 4.7.2 application.

JSON serialization still uses generated metadata. The portable assembly does not advertise blanket trimming or NativeAOT compatibility; validate those deployment modes in the consuming application.

See the [integration guide](ArtifactD.ClientFeedback/README.md) and [sample](samples/ArtifactD.ClientFeedback.Sample/Program.cs).

```sh
dotnet restore ArtifactD.Sdk.slnx
dotnet build ArtifactD.Sdk.slnx -c Release --no-restore
dotnet test packages/dotnet/ArtifactD.ClientFeedback.Tests -c Release --no-build
dotnet pack packages/dotnet/ArtifactD.ClientFeedback -c Release --no-build -o nupkgs
```

Run these commands from the repository root. The package version is supplied from the root `VERSION` by CI. Install the published package with `dotnet add package ArtifactD.ClientFeedback`, or from the `.nupkg` attached to a [GitHub Release](https://github.com/auroracorelabs/artifactd-sdk-client/releases) via a local NuGet source.
