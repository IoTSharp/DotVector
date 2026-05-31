# 🚀 DotVector

[![CI](https://github.com/IoTSharp/DotVector/actions/workflows/ci.yml/badge.svg)](https://github.com/IoTSharp/DotVector/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/DotVector?label=DotVector)](https://www.nuget.org/packages/DotVector)
[![NuGet Core](https://img.shields.io/nuget/v/DotVector.Core?label=DotVector.Core)](https://www.nuget.org/packages/DotVector.Core)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

> **An embedded-native vector database for .NET 10**
>
> Single-directory persistence, in-process execution, zero external dependencies, and local library reuse.

---

## ✨ Project Overview

DotVector is a C# / .NET 10 vector database project whose core engine can be referenced directly from NuGet and run inside the application process. Its primary role is local embedded storage plus reusable vector algorithms and indexing engines. SonnetDB integrations use DotVector through library-level APIs; server mode belongs in SonnetDB.

The repository covers the engine, client adapter, CLI, connectors, and example code. The standalone gRPC / Docker server project has been removed and is no longer the DotVector product direction or the SonnetDB dependency path.

The main boundaries are:

- `DotVector.Core` is the embedded engine: `VectorDatabase`, `LocalDotVectorClient`, indexes, storage, query, protocol DTOs, and distance kernels.
- `DotVector.Primitives` / `DotVector.Indexing` are library-level facades for SonnetDB adapters: lower-is-better KNN distances, contiguous float32 payload input, and local index builder/reader APIs.
- `DotVector.Data` is the client SDK project and publishes as the `DotVector` NuGet package, including the high-level `DotVectorClient`, embedded factory, and `Microsoft.Extensions.VectorData` adapter.
- `DotVector.VectorData` is kept as a standalone VectorData adapter project for compatibility and future split-out work.
- `connectors/c` and `connectors/python` provide local embedded access foundations; remote server access is no longer the DotVector roadmap.

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
| Deployment | Embedded library, local single-directory storage, AOT CLI |
| Ecosystem | `Microsoft.Extensions.VectorData`, C ABI, Python connector, NuGet, release assets |

---

## ⚡ Why It Stands Out

- **Embedded-first**: no external database process required.
- **.NET-native**: designed around .NET 10 vector primitives and APIs.
- **Single-directory persistence**: clear WAL / Segment layout for recovery and maintenance.
- **Safe implementation**: M0 through M7 stay within safe-only constraints.
- **AOT-friendly**: the CLI and core libraries are designed with trim/AOT analysis in mind.
- **Expandable**: SonnetDB reuses distance, indexing, and quantization through library-level adapters without starting a DotVector service.

---

## 📦 NuGet Packages and Connectors

| Name | Label | Downloads | Version | Role |
|------|-------|-----------|---------|------|
| `DotVector.Core` | ![Core](https://img.shields.io/badge/Core-Engine-blue) | ![NuGet Downloads](https://img.shields.io/nuget/dt/DotVector.Core) | ![NuGet Version](https://img.shields.io/nuget/v/DotVector.Core) | Embedded core engine for vector database, indexing, storage, query, and distance computation. |
| `DotVector` | ![Data](https://img.shields.io/badge/Data-Client-green) | ![NuGet Downloads](https://img.shields.io/nuget/dt/DotVector) | ![NuGet Version](https://img.shields.io/nuget/v/DotVector) | Client SDK and `Microsoft.Extensions.VectorData` adapter, packed from `src/DotVector.Data`, for local DotVector access. |
| `DotVector.Cli` | ![CLI](https://img.shields.io/badge/CLI-Tool-orange) | ![NuGet Downloads](https://img.shields.io/nuget/dt/DotVector.Cli) | ![NuGet Version](https://img.shields.io/nuget/v/DotVector.Cli) | Command-line tool for local database management and basic operations. |
| `connectors/c/native` | ![Connector](https://img.shields.io/badge/Connector-C%20ABI-lightgrey) |  |  | NativeAOT shared library exposing the stable C ABI for embedded handles. |
| `connectors/python` | ![Connector](https://img.shields.io/badge/Connector-Python-lightgrey) |  |  | Python ctypes native client. |

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

## 📚 Release

- GitHub Release: includes NuGet packages, connector artifacts, and example archives

Release details are documented in [`docs/release.md`](docs/release.md).

---

## 📦 Repository Contents

- `src/`: core library, client adapter, CLI
- `connectors/`: C ABI and Python connectors
- `examples/`: sample projects
- `tests/`: unit, integration, accuracy, benchmark tests
- `docs/`: architecture, algorithms, release notes

---

## 🧭 Next

The engine already covers indexes, persistence, quantization, and VectorData. The next roadmap focus is local developer experience and library-level boundaries needed by SonnetDB adapters: Code-First modeling, stable `DotVector.Primitives` / `DotVector.Indexing` APIs, index blob serialization, local database lifecycle, and broader language quick starts. See [`ROADMAP.md`](ROADMAP.md) M16.

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
