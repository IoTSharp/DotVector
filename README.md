# DotVector

[![CI](https://github.com/IoTSharp/DotVector/actions/workflows/ci.yml/badge.svg)](https://github.com/IoTSharp/DotVector/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/DotVector.svg)](https://www.nuget.org/packages/DotVector)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

> **嵌入式优先的原生向量数据库，基于 C# / .NET 10**
>
> 定位：".NET 生态里的 LanceDB / 嵌入式 pgvector / 单机 Milvus Lite"

---

## 项目定位

DotVector 是一个**嵌入式、进程内**向量数据库类库，可通过 NuGet 直接引用，无需外部进程或容器。充分利用 .NET 10 在向量计算方面的新能力：

- `System.Numerics.Tensors.TensorPrimitives` — 硬件加速距离计算
- `Vector512<float>` — AVX-512 / ARM64 NEON / SVE
- `Span<T>` / `Memory<T>` — 零拷贝内存操作
- `[InlineArray(N)]` — 固定维度向量栈上构造
- Memory-Mapped File — **单目录持久化**（`.dvec/`），每个 Segment 独立 mmap，zero-copy 读取，性能优于单文件
- Native AOT — 毫秒级启动，容器镜像极小
- `Microsoft.Extensions.VectorData` — 与 Semantic Kernel 天然集成

---

## 与同类产品的差异化定位

| 特性 | DotVector | Milvus | pgvector | Qdrant | LanceDB | Chroma |
|------|-----------|--------|----------|--------|---------|--------|
| 语言 | C# / .NET | Go + C++ | C (PG 扩展) | Rust | Rust | Python |
| 部署方式 | 嵌入式 NuGet | 分布式集群 | PG 扩展 | 单机/集群 | 嵌入式 | 嵌入式 |
| 依赖 | 零外部依赖 | Etcd, MinIO 等 | PostgreSQL | 无 | 无 | 无 |
| .NET 原生 | ✅ 完整集成 | ❌ 需客户端 | ❌ 需客户端 | ❌ 需客户端 | ❌ 需客户端 | ❌ 需客户端 |
| Native AOT | ✅ | ❌ | ❌ | ✅ | ❌ | ❌ |
| 单目录持久化 | ✅ `.dvec/` 目录 | 分布式对象存储 | PG heap | 本地目录 | 本地目录 | 本地目录 |
| `VectorData` 集成 | ✅ 原生 | 需适配层 | 需适配层 | 需适配层 | 需适配层 | 需适配层 |
| 标量过滤 | ✅ (M6) | ✅ | ✅ | ✅ | ✅ | 有限 |
| 量化(PQ/SQ) | 计划(M4/M11) | ✅ | 有限 | ✅ | ✅ | ❌ |

---

## .NET 10 向量计算优势

详见 [`docs/dotnet10-advantages.md`](docs/dotnet10-advantages.md)，简要列举：

### `TensorPrimitives` 硬件加速

```csharp
// L2 距离：一行代码，自动选择 AVX-512 / NEON / 标量回退
float dist = TensorPrimitives.Distance(queryVec, candidateVec);

// 余弦相似度
float cosine = TensorPrimitives.CosineSimilarity(a, b);
```

### `[InlineArray(N)]` — 固定维度零分配

```csharp
// 384 维 embedding，栈上构造，无 GC 压力
[InlineArray(384)]
internal struct Vec384 { private float _e0; }
```

### Native AOT — 毫秒启动

```bash
dotnet publish src/DotVector.Cli -r linux-x64 -p:PublishAot=true
# → 单文件 < 5 MB，启动 < 10 ms
```

---

## 快速开始（占位，将在 M2 填充）

```csharp
// TODO(M2): 实现后填写真实示例
using DotVector;

// 创建内存索引
using var db = new VectorDatabase();
var collection = db.CreateCollection("my-vectors", dimensions: 384, metric: Metric.Cosine);

// 插入向量
collection.Insert(new VectorRecord<string>("doc-1", myEmbedding));

// 近似最近邻搜索
var results = collection.Search(queryEmbedding, topK: 10);
```

---

## 路线图

详见 [`ROADMAP.md`](ROADMAP.md)：

| Milestone | 内容 | 状态 |
|-----------|------|------|
| M0 | 工程骨架 + 文档 | ✅ 本 PR |
| M1 | 距离函数与 SIMD 内核 | 计划中 |
| M2 | 内存索引 — Brute Force / Flat | 计划中 |
| M3 | HNSW 索引 | 计划中 |
| M4 | IVF / IVF-PQ 索引 | 计划中 |
| M5 | 持久化层 (mmap + WAL) | 计划中 |
| M6 | 标量过滤 (payload filter) | 计划中 |
| M7 | `Microsoft.Extensions.VectorData` 适配 | 计划中 |
| M8 | BenchmarkDotNet 基准 + 对比 | 计划中 |
| M9 | gRPC server + Native AOT + Docker | 计划中 |

---

## 规范与贡献

- AI 协作规范：[`AGENTS.md`](AGENTS.md)
- 路线图：[`ROADMAP.md`](ROADMAP.md)
- 架构总览：[`docs/architecture.md`](docs/architecture.md)
- 算法参考：[`docs/algorithms.md`](docs/algorithms.md)
- .NET 10 优势：[`docs/dotnet10-advantages.md`](docs/dotnet10-advantages.md)
- 产品对比：[`docs/comparison.md`](docs/comparison.md)

欢迎提交 Issue 和 PR，请遵循 [Conventional Commits](https://www.conventionalcommits.org/zh-hans/) 规范。

---

*English version: [README.en.md](README.en.md)*