# 🚀 DotVector

[![CI](https://github.com/IoTSharp/DotVector/actions/workflows/ci.yml/badge.svg)](https://github.com/IoTSharp/DotVector/actions/workflows/ci.yml)
[![Docs](https://github.com/IoTSharp/DotVector/actions/workflows/pages.yml/badge.svg)](https://github.com/IoTSharp/DotVector/actions/workflows/pages.yml)
[![NuGet](https://img.shields.io/nuget/v/DotVector?label=DotVector)](https://www.nuget.org/packages/DotVector)
[![NuGet Core](https://img.shields.io/nuget/v/DotVector.Core?label=DotVector.Core)](https://www.nuget.org/packages/DotVector.Core)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

> **面向 .NET 10 的嵌入式原生向量数据库**
>
> 单目录持久化、进程内运行、零外部依赖，面向本地库级复用。

---

## ✨ 项目介绍

DotVector 是一个基于 C# / .NET 10 的向量数据库项目，核心引擎可以直接通过 NuGet 引用，在应用进程内运行。它的主定位是本地嵌入式数据库与向量算法 / 索引引擎复用；SonnetDB 集成只依赖库级 API，服务端模式统一由 SonnetDB 承载。

仓库当前覆盖了数据库引擎、客户端适配、命令行工具、连接器和示例代码。独立 gRPC / Docker 服务端项目已删除，不再作为 DotVector 产品路线或 SonnetDB 依赖路径。

项目边界保持清晰：

- `DotVector.Core` 是完整的嵌入式数据库引擎，包含 `VectorDatabase`、`LocalDotVectorClient`、索引、存储、查询、协议 DTO 和距离计算；一个 `VectorDatabase` 实例对应一个 `.dvec/` 数据库目录。
- `DotVector.Primitives` / `DotVector.Indexing` 是面向 SonnetDB adapter 的库级 facade，提供 lower-is-better KNN 距离语义、连续 float32 payload 构建输入和本地索引 reader/builder。
- `DotVector.Data` 是客户端 SDK 项目，NuGet 包名为 `DotVector`，包含高层 `DotVectorClient`、嵌入式工厂和 `Microsoft.Extensions.VectorData` 适配。
- `DotVector.VectorData` 是仓库中保留的独立 VectorData 适配项目，便于后续拆分/兼容演进；当前主要发布门面是 `DotVector` NuGet 包。
- `connectors/c` 与 `connectors/python` 提供本地嵌入式接入基础；远程服务端能力不再作为 DotVector 主路线推进。

---

## 🧠 核心实力

| 维度 | 能力 |
|------|------|
| 向量计算 | `TensorPrimitives`、`Vector<T>`、`Vector512<T>` |
| 索引引擎 | Flat、HNSW、IVF-Flat、IVF-PQ、Vamana |
| 距离度量 | L2、Cosine、InnerProduct、Hamming、DotProduct |
| 量化能力 | SQ8、PQ、OPQ、RQ |
| 存储能力 | `.dvec/` 目录、WAL、Segment、mmap 读取 |
| 查询能力 | 向量检索、标量过滤、payload 持久化 |
| 部署能力 | 嵌入式库、本地单目录、AOT CLI |
| 生态集成 | `Microsoft.Extensions.VectorData`、C ABI、Python connector、NuGet、Release 产物 |

---

## ⚡ 主要优势

- **嵌入式优先**：没有外部数据库进程，适合应用内直接使用。
- **.NET 原生**：围绕 .NET 10 的向量计算能力设计，API 风格统一。
- **单目录持久化**：数据、WAL、Segment 分层清晰，便于恢复和维护。
- **安全实现**：M0 到 M7 坚持 safe-only，不依赖 `unsafe`。
- **AOT 友好**：CLI 与核心库按 AOT / trim 分析思路设计。
- **可扩展**：SonnetDB 通过库级 adapter 复用距离、索引和量化能力，不需要额外启动 DotVector 服务。

---

## 📦 NuGet 包与连接器

| 名称 | 小标签 | 下载量 | 版本号 | 作用 |
|------|--------|--------|--------|------|
| `DotVector.Core` | ![Core](https://img.shields.io/badge/Core-Engine-blue) | ![NuGet Downloads](https://img.shields.io/nuget/dt/DotVector.Core) | ![NuGet Version](https://img.shields.io/nuget/v/DotVector.Core) | 嵌入式核心引擎，提供向量数据库、索引、存储、查询与距离计算能力。 |
| `DotVector` | ![Data](https://img.shields.io/badge/Data-Client-green) | ![NuGet Downloads](https://img.shields.io/nuget/dt/DotVector) | ![NuGet Version](https://img.shields.io/nuget/v/DotVector) | 客户端 SDK 与 `Microsoft.Extensions.VectorData` 适配层，由 `src/DotVector.Data` 项目打包，用于本地访问 DotVector。 |
| `DotVector.Cli` | ![CLI](https://img.shields.io/badge/CLI-Tool-orange) | ![NuGet Downloads](https://img.shields.io/nuget/dt/DotVector.Cli) | ![NuGet Version](https://img.shields.io/nuget/v/DotVector.Cli) | 命令行工具，用于本地数据库管理与基础操作。 |
| `connectors/c/native` | ![Connector](https://img.shields.io/badge/Connector-C%20ABI-lightgrey) |  |  | NativeAOT 共享库，暴露稳定 C ABI，支持嵌入式句柄。 |
| `connectors/python` | ![Connector](https://img.shields.io/badge/Connector-Python-lightgrey) |  |  | Python ctypes Native 客户端。 |

---

## 🚀 快速开始

```csharp
using DotVector.Api;
using DotVector.CodeFirst;
using DotVector.Model;

using var db = new VectorDatabase();
var collection = db.CreateCollection<string>("articles", dimensions: 4, metric: Metric.Cosine);

collection.Insert(new VectorRecord<string>("doc-1", [0.95f, 0.10f, 0.08f, 0.02f]));

var results = collection.Search([0.92f, 0.12f, 0.07f, 0.03f], topK: 5);
```

Code-First 嵌入式体验：

```csharp
public sealed class Article
{
    [DotVectorKey]
    public string Id { get; init; } = "";

    public string Title { get; init; } = "";

    [DotVectorVector(384, Metric = Metric.Cosine)]
    [DotVectorIndex(IndexKind.Hnsw, HnswM = 16, EfSearch = 64)]
    public float[] Embedding { get; init; } = [];
}

public sealed class AppVectorContext : DotVectorDbContext
{
    public AppVectorContext(string path) : base(path) => BindSets();

    public DotVectorSet<Article> Articles { get; private set; } = null!;
}
```

更完整的可运行示例见 [`examples/csharp/QuickStart`](examples/csharp/QuickStart/README.md)。

---

## 📚 发布

- GitHub Release：同时附带 NuGet、连接器产物与示例压缩包
- 文档站：<https://iotsharp.net/DotVector/>，使用 [`JekyllNet`](https://github.com/JekyllNet/JekyllNet) 构建并发布到 GitHub Pages

发布说明见 [`docs/release.md`](docs/release.md)。

---

## 📦 仓库内容

- `src/`：核心库、数据适配、CLI
- `connectors/`：C ABI 与 Python 连接器
- `examples/`：示例工程
- `tests/`：单元、集成、精度、基准测试
- `docs/`：架构、算法、发布说明与产品定位

---

## 🧭 后续方向

DotVector 的底层引擎已经覆盖索引、持久化、量化与 VectorData；后续会继续补强本地开发体验和 SonnetDB adapter 所需的库级边界：

- Code-First / Attribute 建模（M16.1 已完成最小闭环）
- `DotVector.Primitives` / `DotVector.Indexing` 稳定 API
- 索引序列化 blob 与版本兼容
- 数据库创建、连接和本地管理
- Python / C / C# 多语言快速开始

相关规划已写入 [`ROADMAP.md`](ROADMAP.md) 的 M16。

---

## 🤝 规范与贡献

- AI 协作规范：[`AGENTS.md`](AGENTS.md)
- 架构总览：[`docs/architecture.md`](docs/architecture.md)
- 算法参考：[`docs/algorithms.md`](docs/algorithms.md)
- .NET 10 优势：[`docs/dotnet10-advantages.md`](docs/dotnet10-advantages.md)
- 产品对比：[`docs/comparison.md`](docs/comparison.md)

欢迎提交 Issue 和 PR，请遵循 [Conventional Commits](https://www.conventionalcommits.org/zh-hans/) 规范。

---

*English version: [README.en.md](README.en.md)*
