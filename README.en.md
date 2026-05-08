# 🚀 DotVector

[![CI](https://github.com/IoTSharp/DotVector/actions/workflows/ci.yml/badge.svg)](https://github.com/IoTSharp/DotVector/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/DotVector.svg)](https://www.nuget.org/packages/DotVector)
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

## 🏗️ Architecture

| Component | Role |
|-----------|------|
| `DotVector.Core` | Embedded core engine |
| `DotVector` | Server host |
| `DotVector.Data` | `VectorData` client adapter |
| `DotVector.Cli` | Command-line tool |
| `connectors/c/native` | C ABI / P-Invoke connector |

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
- NuGet packages: `DotVector`, `DotVector.Core`, `DotVector.Data`, `DotVector.Cli`
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
