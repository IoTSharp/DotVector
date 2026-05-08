# 🚀 DotVector

[![CI](https://github.com/IoTSharp/DotVector/actions/workflows/ci.yml/badge.svg)](https://github.com/IoTSharp/DotVector/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/DotVector.svg)](https://www.nuget.org/packages/DotVector)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

> **面向 .NET 10 的嵌入式原生向量数据库**
>
> 单目录持久化、进程内运行、零外部依赖，也支持 gRPC 服务器模式与 Docker 部署。

---

## ✨ 项目介绍

DotVector 是一个基于 C# / .NET 10 的向量数据库项目，核心引擎可以直接通过 NuGet 引用，在应用进程内运行。

它适合两种典型形态：

- 嵌入式模式：直接 `new VectorDatabase()`，本地使用
- 服务器模式：通过 `DotVector` 服务端宿主对外提供 gRPC 接口

仓库当前覆盖了数据库引擎、客户端适配、命令行工具、服务端宿主、连接器和示例代码。

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
| 部署能力 | 嵌入式库、gRPC 服务、Docker 镜像、AOT CLI |
| 生态集成 | `Microsoft.Extensions.VectorData`、NuGet、Release 产物 |

---

## ⚡ 主要优势

- **嵌入式优先**：没有外部数据库进程，适合应用内直接使用。
- **.NET 原生**：围绕 .NET 10 的向量计算能力设计，API 风格统一。
- **单目录持久化**：数据、WAL、Segment 分层清晰，便于恢复和维护。
- **安全实现**：M0 到 M7 坚持 safe-only，不依赖 `unsafe`。
- **AOT 友好**：CLI 和服务端宿主都按 AOT / trim 分析思路设计。
- **可扩展**：从本地嵌入式到远程服务器，接口层保持一致。

---

## 🏗️ 架构分层

| 组件 | 作用 |
|------|------|
| `DotVector.Core` | 嵌入式核心引擎 |
| `DotVector` | 服务端宿主 |
| `DotVector.Data` | `VectorData` 客户端适配 |
| `DotVector.Cli` | 命令行工具 |
| `connectors/c/native` | C ABI / P-Invoke 连接器 |

---

## 🚀 快速开始

```csharp
using DotVector.Api;
using DotVector.Model;

using var db = new VectorDatabase();
var collection = db.CreateCollection<string>("articles", dimensions: 4, metric: Metric.Cosine);

collection.Insert(new VectorRecord<string>("doc-1", [0.95f, 0.10f, 0.08f, 0.02f]));

var results = collection.Search([0.92f, 0.12f, 0.07f, 0.03f], topK: 5);
```

更完整的可运行示例见 [`examples/csharp/QuickStart`](examples/csharp/QuickStart/README.md)。

---

## 🐳 服务与发布

- Docker 镜像：`iotsharp/dotvector`
- NuGet 包：`DotVector`、`DotVector.Core`、`DotVector.Data`、`DotVector.Cli`
- GitHub Release：同时附带连接器产物与示例压缩包

发布说明见 [`docs/release.md`](docs/release.md)。

---

## 📦 仓库内容

- `src/`：核心库、服务端、数据适配、CLI
- `connectors/`：原生连接器
- `examples/`：示例工程
- `tests/`：单元、集成、精度、基准测试
- `docs/`：架构、算法、发布说明

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
