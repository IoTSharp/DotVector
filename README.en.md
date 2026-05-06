# DotVector

[![CI](https://github.com/IoTSharp/DotVector/actions/workflows/ci.yml/badge.svg)](https://github.com/IoTSharp/DotVector/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/DotVector.svg)](https://www.nuget.org/packages/DotVector)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

> **An embedded-first native vector database for .NET 10**
>
> Positioning: "LanceDB / embedded pgvector / single-node Milvus Lite for the .NET ecosystem"

---

## Project Overview

DotVector is an **embedded, in-process** vector database library, directly referenceable via NuGet with no external process or container required. It fully leverages .NET 10's vector computation capabilities:

- `System.Numerics.Tensors.TensorPrimitives` — hardware-accelerated distance computation
- `Vector512<float>` — AVX-512 / ARM64 NEON / SVE
- `Span<T>` / `Memory<T>` — zero-copy memory operations
- `[InlineArray(N)]` — fixed-dimension vectors on the stack
- Memory-Mapped File — **single-directory persistence** (`.dvec/`), each Segment independently mmap'd for better performance and simpler compaction than a single file
- Native AOT — millisecond startup, minimal container images
- `Microsoft.Extensions.VectorData` — natural integration with Semantic Kernel

---

## Differentiation vs. Competitors

| Feature | DotVector | Milvus | pgvector | Qdrant | LanceDB | Chroma |
|---------|-----------|--------|----------|--------|---------|--------|
| Language | C# / .NET | Go + C++ | C (PG ext) | Rust | Rust | Python |
| Deployment | Embedded NuGet | Distributed | PG extension | Single/cluster | Embedded | Embedded |
| Dependencies | Zero external | Etcd, MinIO, etc | PostgreSQL | None | None | None |
| .NET native | ✅ Full integration | ❌ client needed | ❌ client needed | ❌ client needed | ❌ client needed | ❌ client needed |
| Native AOT | ✅ | ❌ | ❌ | ✅ | ❌ | ❌ |
| Single-dir persistence | ✅ `.dvec/` directory | ❌ | ❌ | Local dir | Local dir | Local dir |
| VectorData integration | ✅ Native | Adapter needed | Adapter needed | Adapter needed | Adapter needed | Adapter needed |
| Scalar filtering | ✅ (M6) | ✅ | ✅ | ✅ | ✅ | Limited |
| Quantization (PQ/SQ) | Planned (M4/M11) | ✅ | Limited | ✅ | ✅ | ❌ |

---

## .NET 10 Vector Computation Advantages

See [`docs/dotnet10-advantages.md`](docs/dotnet10-advantages.md) for details. Highlights:

### `TensorPrimitives` Hardware Acceleration
```csharp
// L2 distance: one line, auto-selects AVX-512 / NEON / scalar fallback
float dist = TensorPrimitives.Distance(queryVec, candidateVec);

// Cosine similarity
float cosine = TensorPrimitives.CosineSimilarity(a, b);
```

### `[InlineArray(N)]` — Fixed Dimension, Zero Allocation
```csharp
// 384-dim embedding, stack-allocated, no GC pressure
[InlineArray(384)]
internal struct Vec384 { private float _e0; }
```

### Native AOT — Millisecond Startup
```bash
dotnet publish src/DotVector.Cli -r linux-x64 -p:PublishAot=true
# → single file < 5 MB, startup < 10 ms
```

---

## Quick Start (placeholder, will be filled in M2)

```csharp
// TODO(M2): Fill in real example after implementation
using DotVector;

using var db = new VectorDatabase();
var collection = db.CreateCollection("my-vectors", dimensions: 384, metric: Metric.Cosine);

collection.Insert(new VectorRecord<string>("doc-1", myEmbedding));

var results = collection.Search(queryEmbedding, topK: 10);
```

---

## Roadmap

See [`ROADMAP.md`](ROADMAP.md):

| Milestone | Content | Status |
|-----------|---------|--------|
| M0 | Project scaffold + docs | ✅ This PR |
| M1 | Distance functions + SIMD kernels | Planned |
| M2 | In-memory index — Brute Force / Flat | Planned |
| M3 | HNSW index | Planned |
| M4 | IVF / IVF-PQ index | Planned |
| M5 | Persistence layer (mmap + WAL) | Planned |
| M6 | Scalar filtering (payload filter) | Planned |
| M7 | `Microsoft.Extensions.VectorData` adapter | Planned |
| M8 | BenchmarkDotNet benchmarks + comparisons | Planned |
| M9 | gRPC server + Native AOT + Docker | Planned |

---

## Contributing

- AI collaboration spec: [`AGENTS.md`](AGENTS.md)
- Roadmap: [`ROADMAP.md`](ROADMAP.md)
- Architecture overview: [`docs/architecture.md`](docs/architecture.md)
- Algorithm references: [`docs/algorithms.md`](docs/algorithms.md)
- .NET 10 advantages: [`docs/dotnet10-advantages.md`](docs/dotnet10-advantages.md)
- Product comparison: [`docs/comparison.md`](docs/comparison.md)

Contributions welcome! Please follow [Conventional Commits](https://www.conventionalcommits.org/en/).

---

*中文版：[README.md](README.md)*
