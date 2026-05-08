# Release Publishing

DotVector release publishing is handled by `.github/workflows/publish.yml`.

## Required Secrets

- `NUGET_ORG_API_KEY`: nuget.org organization API key with permission to publish DotVector packages.
- `DOCKERHUB_USERNAME`: Docker Hub account that can push to the `iotsharp` namespace.
- `DOCKERHUB_TOKEN`: Docker Hub access token for the account above.

## Release Event

Publishing a GitHub Release runs the full pipeline:

- builds and tests the solution
- publishes the CLI Native AOT smoke artifact during CI/release validation
- publishes the C NativeAOT connector artifact for release assets
- packs and pushes `DotVector.Core`, `DotVector.Data`, and `DotVector.Cli` to nuget.org
- builds and pushes `iotsharp/dotvector:<version>` to Docker Hub
- also tags `iotsharp/dotvector:latest` for non-prerelease releases
- uploads NuGet packages, symbol packages, and `dotvector-<version>-connectors-examples.zip` to the GitHub Release

The package and image version is taken from the GitHub Release tag. A leading `v` is removed for NuGet package versions, so `v0.1.0` becomes `0.1.0`.

## Manual Dispatch

Manual dispatch can be used for targeted publishing:

- `publish_nuget=true` pushes NuGet packages for the provided version
- `push_docker=true` pushes the Docker image for the provided version

GitHub Release asset upload only runs for the `release.published` event, because it needs an existing release tag.

## GitHub Pages Documentation

Documentation is published by `.github/workflows/pages.yml`.

- Source directory: `docs/`
- Entry page: `docs/index.md`
- Build step: `JekyllNet/action@v2.5`
- Deploy step: `actions/deploy-pages`

The workflow follows GitHub Pages custom workflow deployment. `JekyllNet/action@v2.5` installs the .NET 10 SDK, installs the pinned `JekyllNet` dotnet tool version `0.2.5`, and runs `jekyllnet build --source ./docs --destination ./_site`. The generated `_site` directory is then uploaded as the GitHub Pages artifact.

## NuGet Organization API Key

Create the API key under the NuGet organization that owns the `DotVector.*` package IDs, then save it in GitHub repository or organization secrets as `NUGET_ORG_API_KEY`.

The key should be scoped as narrowly as possible:

- package glob: `DotVector.*`
- operation: push new package and package version
- source: `https://api.nuget.org/v3/index.json`

The publish workflow fails fast when `NUGET_ORG_API_KEY` is missing, so manual dispatch can be used safely for dry build/test runs with `publish_nuget=false`.

## CI Coverage

`.github/workflows/ci.yml` is the pre-release guardrail. It runs restore, Release build, tests, CLI Native AOT publish, C NativeAOT connector publish, and on Ubuntu also NuGet pack, Docker image build, and documentation build. This keeps release-only failures visible before a tag is cut.
