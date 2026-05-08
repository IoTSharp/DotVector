# 🚀 DotVector

[![CI](https://github.com/IoTSharp/DotVector/actions/workflows/ci.yml/badge.svg)](https://github.com/IoTSharp/DotVector/actions/workflows/ci.yml)
[![Docker Pulls](https://img.shields.io/docker/pulls/iotsharp/dotvector)](https://hub.docker.com/r/iotsharp/dotvector)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

> **An embedded-native vector database for .NET 10**
>
> Single-directory persistence, in-process execution, zero external dependencies, plus optional gRPC server and Docker deployment.

---

## ✨ Project Overview

DotVector is a C# / .NET 10 vector database project whose core engine can be referenced directly from NuGet and run inside the application process.

It supports two common modes:

- Embedded mode: use `new VectorDatabase()` locally
- Server mode: expose the engine through the `DotVector` gRPC host

The repository covers the engine, client adapter, CLI, server host, connectors, and example code.

---

## 🧠 Core Strengths

| Area | Capability |
|------|------------|
| Vector compute | `TensorPrimitives`, `Vector<T>`, `Vector512<T>` |
| Index engines | Flat, HNSW, IVF-Flat, IVF-PQ, Vamana |
| Distance metrics | L2, Cosine, InnerProduct, Hamming, DotProduct |
| Quantization | SQ8, PQ, OPQ, RQ |
| Storage | `.dvec/` directory, WAL, Segment, mmap reads |
| Querying | ANN search, scalar filtering, payload persistence |
| Deployment | Embedded library, gRPC service, Docker image, AOT CLI |
| Ecosystem | `Microsoft.Extensions.VectorData`, NuGet, release assets |

---

## ⚡ Why It Stands Out

- **Embedded-first**: no external database process required.
- **.NET-native**: designed around .NET 10 vector primitives and APIs.
- **Single-directory persistence**: clear WAL / Segment layout for recovery and maintenance.
- **Safe implementation**: M0 through M7 stay within safe-only constraints.
- **AOT-friendly**: CLI and server host are designed with trim/AOT analysis in mind.
- **Expandable**: the same API surface supports local embedded and remote server usage.

---

## 📦 NuGet Packages and Connectors

| Name | Label | Downloads | Version | Role |
|------|-------|-----------|---------|------|
| `DotVector.Core` | ![Core](https://img.shields.io/badge/Core-Engine-blue) | ![NuGet Downloads](https://img.shields.io/nuget/dt/DotVector.Core) | ![NuGet Version](https://img.shields.io/nuget/v/DotVector.Core) | Embedded core engine for vector database, indexing, storage, query, and distance computation. |
| `DotVector.Data` | ![Data](https://img.shields.io/badge/Data-Client-green) | ![NuGet Downloads](https://img.shields.io/nuget/dt/DotVector.Data) | ![NuGet Version](https://img.shields.io/nuget/v/DotVector.Data) | Client SDK and `Microsoft.Extensions.VectorData` adapter for local or remote DotVector access. |
| `DotVector.Cli` | ![CLI](https://img.shields.io/badge/CLI-Tool-orange) | ![NuGet Downloads](https://img.shields.io/nuget/dt/DotVector.Cli) | ![NuGet Version](https://img.shields.io/nuget/v/DotVector.Cli) | Command-line tool for connecting to the DotVector gRPC service and managing collections. |
| `connectors/c/native` | ![Connector](https://img.shields.io/badge/Connector-C%20ABI-lightgrey) |  |  | C ABI / P-Invoke connector for future native cross-language calls. |

---

## 🚀 Quick Start

```csharp
using DotVector.Api;
using DotVector.Model;

using var db = new VectorDatabase();
var collection = db.CreateCollection<string>("articles", dimensions: 4, metric: Metric.Cosine);

collection.Insert(new VectorRecord<string>("doc-1", [0.95f, 0.10f, 0.08f, 0.02f]));

var results = collection.Search([0.92f, 0.12f, 0.07f, 0.03f], topK: 5);
```

A fuller runnable sample lives in [`examples/csharp/QuickStart`](examples/csharp/QuickStart/README.md).

---

## 🐳 Service and Release

- Docker image: `iotsharp/dotvector`
- GitHub Release: includes connector artifacts and example archives

Release details are documented in [`docs/release.md`](docs/release.md).

---

## 📦 Repository Contents

- `src/`: core library, server, client adapter, CLI
- `connectors/`: native connector
- `examples/`: sample projects
- `tests/`: unit, integration, accuracy, benchmark tests
- `docs/`: architecture, algorithms, release notes

---

## 🤝 Contributing

- AI collaboration spec: [`AGENTS.md`](AGENTS.md)
- Architecture overview: [`docs/architecture.md`](docs/architecture.md)
- Algorithm references: [`docs/algorithms.md`](docs/algorithms.md)
- .NET 10 advantages: [`docs/dotnet10-advantages.md`](docs/dotnet10-advantages.md)
- Product comparison: [`docs/comparison.md`](docs/comparison.md)

Please follow [Conventional Commits](https://www.conventionalcommits.org/en/) for issues and PRs.

---

*中文版：[README.md](README.md)*
