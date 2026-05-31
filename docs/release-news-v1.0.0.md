---
title: DotVector v1.0.0 发布
layout: default
---

# DotVector v1.0.0 发布：面向 .NET 10 的嵌入式向量数据库

> 单目录持久化、进程内运行、零外部依赖，现已覆盖 Flat / HNSW / IVF / Vamana 五大索引、SQ8 / PQ / OPQ / RQ 全量化管线，以及本地 Native AOT CLI / C / Python 连接器。

---

## 版本概览

DotVector v1.0.0 是一个功能完整的嵌入式向量数据库引擎，以 NuGet 包、本地 CLI 和连接器产物发布。核心引擎 `DotVector.Core` 可以直接嵌入 .NET 应用进程内运行，无需任何外部数据库进程。独立 gRPC Server / Docker 服务端形态已删除；需要服务端 endpoint 时由 SonnetDB 承载。

| 发布产物 | 获取方式 |
|----------|----------|
| `DotVector.Core` (引擎) | `dotnet add package DotVector.Core` |
| `DotVector` (客户端 SDK) | `dotnet add package DotVector` |
| `DotVector.Cli` (命令行) | `dotnet tool install -g DotVector.Cli` |
| C / Python connector | GitHub Release assets |

---

## 索引引擎：五种索引覆盖全场景

- **FlatIndex** — 暴力精确检索，适合小规模数据集和 ground-truth 验证
- **HNSW** — 分层可导航小世界图，Recall@10 ≥ 0.95（1000×64 随机数据），适合高召回在线服务
- **IVF-Flat** — 倒排文件 + K-Means 聚类，Recall@10 ≥ 0.90，适合百万级数据
- **IVF-PQ** — 倒排文件 + 乘积量化，大幅压缩内存占用，适合内存受限的边缘设备
- **Vamana (DiskANN)** — 图索引 + mmap 磁盘驻留，支持 `vamana.bin` 持久化与 Flush/Restore，适合十亿级磁盘向量检索

所有索引均支持 L2 / Cosine / InnerProduct / DotProduct 四种距离度量，以及 `ReaderWriterLockSlim` 并发读写。

---

## 量化管线：从 SQ8 到 RQ 的完整覆盖

v1.0.0 完成了 `IVectorQuantizer` 统一量化抽象，提供四种量化器：

| 量化器 | 压缩率 | 特点 |
|--------|--------|------|
| SQ8 (ScalarQuantizer8) | 4× (32→8 bit) | 逐维 min/max 量化，零额外分配 |
| PQ (ProductQuantizer) | 4×–16× | 子空间 K-Means 编码 + ADC LUT 距离查表 |
| OPQ (OptimizedProductQuantizer) | 4×–16× | 旋转矩阵优化 + PQ 联合训练，纯托管 Jacobi SVD |
| RQ (ResidualQuantizer) | 4×–32× | 多级残差编码，更高级数严格降低 MSE |

所有量化器通过 `QuantizerSerializer` 实现自描述二进制持久化（`quantizer.bin` sidecar），与 Segment 格式向后兼容。新增 `QuantizedFlatIndex<TKey>` 支持量化后的线性扫描索引。

---

## 持久化层：WAL + Segment + mmap

- **WAL（预写日志）**：CRC32 校验、torn write 自动截断、按 seq 轮转
- **Segment 快照**：`seg.hdr` + `keys.bin` + `vectors.bin` + `payload.bin` + `quantizer.bin`，原子写入（tmp → Directory.Move）
- **mmap 零拷贝读取**：`MemoryMappedFile` + `MemoryMappedViewAccessor.ReadArray<T>`，safe-only 实现
- **Compaction**：合并多个 Segment 为单一文件，删除冗余数据
- **Crash Recovery**：重启自动跳过 `.tmp` 残留目录，按 manifest 恢复

持久化模型采用 `.dvec/` 单目录结构，与 LanceDB、Qdrant 的 Segment 粒度设计一致。

---

## 标量过滤：Payload + B-tree 索引

- Filter AST：`Eq / Ne / Range / Exists / Missing / And / Or / Not`，AOT 友好
- B-tree 倒排索引：`ScalarIndex` 支持 `Eq` + `Range` 下推，字符串/数值/布尔分桶
- Pre-filter 与 post-filter 双保险策略：下推候选集做精确扫描，不可下推条件回退到 over-fetch + post-filter

---

## 本地 CLI 与连接器

- **Native AOT CLI**：`DotVector.Cli` 支持 `PublishAot=true`，打开本地 `.dvec/` 目录执行集合管理命令
- **C 连接器**：C ABI 共享库暴露本地嵌入式句柄、payload/filter JSON 协议和基础集合操作
- **Python 连接器**：Python ctypes 客户端复用 C ABI，不再依赖 gRPC / protobuf
- **服务端模式**：不在 DotVector 中提供，统一进入 SonnetDB

---

## 生态集成

- **`Microsoft.Extensions.VectorData.Abstractions`**：完整适配 `VectorStore` / `VectorStoreCollection<TKey, TRecord>` / Dynamic Collection
- **LINQ Filter 翻译**：`Expression<Func<TRecord, bool>>` → `DotVector.Query.Filter`，支持 `== / != / < / <= / > / >= / && / || / !`
- **DI 集成**：`AddDotVectorVectorStore()` 一行注册到 `IServiceCollection`
- **文档站**：<https://iotsharp.net/DotVector/>，JekyllNet 构建，GitHub Pages 托管

---

## 路线图状态

| M0–M7 | M8 | M9–M11 | M12 | M13 | M14 | M16 |
|:-----:|:--:|:------:|:---:|:---:|:---:|:---:|
| ✅ 完成 | ⏳ 基准 | ✅ 完成 | ✅ 完成 | ✅ 完成 | ✅ 完成 | ⏳ 进行中 |

当前已完成 M0–M14（除 M8 基准体系），M16 开发体验补强进行中，覆盖 Code-First 建模、本地数据库生命周期、本地 CLI、调试界面和多语言快速开始。

---

## 快速开始

```bash
# NuGet 嵌入式
dotnet add package DotVector.Core

# CLI 本地数据库
dotnet tool install -g DotVector.Cli
dotvector ping --data ./data.dvec
```

```csharp
using DotVector.Api;
using DotVector.Model;

using var db = new VectorDatabase();
var coll = db.CreateCollection<string>("docs", dimensions: 4, metric: Metric.Cosine);
coll.Insert(new VectorRecord<string>("id-1", [0.95f, 0.10f, 0.08f, 0.02f]));
var results = coll.Search([0.92f, 0.12f, 0.07f, 0.03f], topK: 5);
```

---

## 相关链接

- GitHub: <https://github.com/IoTSharp/DotVector>
- 文档站: <https://iotsharp.net/DotVector/>
- NuGet: <https://www.nuget.org/packages/DotVector>
- 企业加速版: <https://github.com/IoTSharp/DotVectorEE>
