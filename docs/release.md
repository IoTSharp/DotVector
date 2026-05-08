# Release Publishing

DotVector release publishing is handled by `.github/workflows/publish.yml`.

## Required Secrets

- `NUGET_API_KEY`: nuget.org API key with permission to publish DotVector packages.
- `DOCKERHUB_USERNAME`: Docker Hub account that can push to the `iotsharp` namespace.
- `DOCKERHUB_TOKEN`: Docker Hub access token for the account above.

## Release Event

Publishing a GitHub Release runs the full pipeline:

- builds and tests the solution
- packs and pushes `DotVector`, `DotVector.Core`, `DotVector.Data`, and `DotVector.Cli` to nuget.org
- builds and pushes `iotsharp/dotvector:<version>` to Docker Hub
- also tags `iotsharp/dotvector:latest` for non-prerelease releases
- uploads NuGet packages, symbol packages, and `dotvector-<version>-connectors-examples.zip` to the GitHub Release

The package and image version is taken from the GitHub Release tag. A leading `v` is removed for NuGet package versions, so `v0.1.0` becomes `0.1.0`.

## Manual Dispatch

Manual dispatch can be used for targeted publishing:

- `publish_nuget=true` pushes NuGet packages for the provided version
- `push_docker=true` pushes the Docker image for the provided version

GitHub Release asset upload only runs for the `release.published` event, because it needs an existing release tag.
