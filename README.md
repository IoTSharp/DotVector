# 🚀 DotVector

[![CI](https://github.com/IoTSharp/DotVector/actions/workflows/ci.yml/badge.svg)](https://github.com/IoTSharp/DotVector/actions/workflows/ci.yml)
[![Docs](https://github.com/IoTSharp/DotVector/actions/workflows/pages.yml/badge.svg)](https://github.com/IoTSharp/DotVector/actions/workflows/pages.yml)
[![NuGet Core](https://img.shields.io/nuget/v/DotVector.Core?label=DotVector.Core)](https://www.nuget.org/packages/DotVector.Core)
[![Docker Pulls](https://img.shields.io/docker/pulls/iotsharp/dotvector)](https://hub.docker.com/r/iotsharp/dotvector)
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

项目边界保持清晰：

- `DotVector.Core` 是完整的嵌入式数据库引擎，一个 `VectorDatabase` 实例对应一个 `.dvec/` 数据库目录。
- `DotVector` 是服务端壳，用于在一个进程内托管多个 Core 数据库实例，并对外提供远程访问能力。
- `DotVector.Data` 是客户端 SDK 与 `Microsoft.Extensions.VectorData` 适配层，可走本地嵌入式，也可连接远程服务端。

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

## 📦 NuGet 包与连接器

| 名称 | 小标签 | 下载量 | 版本号 | 作用 |
|------|--------|--------|--------|------|
| `DotVector.Core` | ![Core](https://img.shields.io/badge/Core-Engine-blue) | ![NuGet Downloads](https://img.shields.io/nuget/dt/DotVector.Core) | ![NuGet Version](https://img.shields.io/nuget/v/DotVector.Core) | 嵌入式核心引擎，提供向量数据库、索引、存储、查询与距离计算能力。 |
| `DotVector.Data` | ![Data](https://img.shields.io/badge/Data-Client-green) | ![NuGet Downloads](https://img.shields.io/nuget/dt/DotVector.Data) | ![NuGet Version](https://img.shields.io/nuget/v/DotVector.Data) | 客户端 SDK 与 `Microsoft.Extensions.VectorData` 适配层，用于本地或远程访问 DotVector。 |
| `DotVector.Cli` | ![CLI](https://img.shields.io/badge/CLI-Tool-orange) | ![NuGet Downloads](https://img.shields.io/nuget/dt/DotVector.Cli) | ![NuGet Version](https://img.shields.io/nuget/v/DotVector.Cli) | 命令行工具，用于连接 DotVector gRPC 服务、管理集合与执行基础操作。 |
| `connectors/c/native` | ![Connector](https://img.shields.io/badge/Connector-C%20ABI-lightgrey) |  |  | C ABI / P-Invoke 连接器，用于后续跨语言原生调用。 |

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
- GitHub Release：同时附带连接器产物与示例压缩包
- 文档站：使用 [`JekyllNet`](https://github.com/JekyllNet/JekyllNet) 构建并发布到 GitHub Pages
- NuGet 发布：使用组织级 `NUGET_ORG_API_KEY` secret

发布说明见 [`docs/release.md`](docs/release.md)。

---

## 📦 仓库内容

- `src/`：核心库、服务端、数据适配、CLI
- `connectors/`：原生连接器
- `examples/`：示例工程
- `tests/`：单元、集成、精度、基准测试
- `docs/`：架构、算法、发布说明与产品定位

---

## 🧭 后续方向

DotVector 的底层引擎已经覆盖索引、持久化、量化、VectorData 与服务端部署；后续会继续补强开发体验和服务端管理面：

- Code-First / Attribute 建模
- 服务端 `_system.dvec/` 系统目录
- 数据库创建、连接、用户和权限管理
- Vue3 管理台
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
