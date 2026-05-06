# DotVector 与同类产品对比

本表从多个维度对比 DotVector 与主流向量数据库，帮助用户选择合适的工具。

---

## 综合对比表

| 维度 | **DotVector** | **Milvus** | **pgvector** | **Qdrant** | **LanceDB** | **Chroma** |
|------|:---:|:---:|:---:|:---:|:---:|:---:|
| **定位** | .NET 嵌入式 | 分布式云原生 | PG 向量扩展 | 高性能单机/集群 | 嵌入式列存 | 开发者友好嵌入式 |
| **实现语言** | C# / .NET | Go + C++ | C | Rust | Rust | Python |
| **部署方式** | 嵌入式 NuGet | 独立集群 | PostgreSQL 插件 | 独立进程 | 嵌入式 / 服务 | 嵌入式 / 服务 |
| **外部依赖** | **零** | Etcd、MinIO、Pulsar | PostgreSQL | 无 | 无 | 无 |
| **.NET 原生集成** | ✅ 完整原生 | ❌ 仅客户端 | ❌ Npgsql 客户端 | ❌ 仅客户端 | ❌ 仅客户端 | ❌ 仅客户端 |
| **Native AOT** | ✅ | ❌ | ❌ | ✅（Rust） | ❌ | ❌ |
| **单目录持久化** | ✅ `.dvec/`（M5） | ❌ | ❌ | ✅ 目录 | ✅ 目录 | ✅ 目录 |
| **索引：Flat/Brute** | ✅（M2） | ✅ FLAT | ✅ | ✅ | ✅ | ✅ |
| **索引：HNSW** | ✅（M3） | ✅ | ✅（v0.5+） | ✅ | ✅ | ✅ |
| **索引：IVF** | ✅（M4） | ✅ IVF_FLAT | ✅（v0.5+） | ❌ | ✅ | ❌ |
| **量化 PQ/SQ** | 计划（M11） | ✅ IVF_PQ、SQ8 | ❌ | ✅ SQ | ✅ PQ | ❌ |
| **DiskANN** | 计划（M10） | ✅ DISKANN | ❌ | ❌ | ✅（实验） | ❌ |
| **标量过滤** | ✅（M6） | ✅ | ✅ SQL WHERE | ✅ Payload | ✅ | 有限 |
| **多向量字段** | 计划 | ✅ | ❌ | ✅ | ✅ | ❌ |
| **持久化方式** | 单目录 mmap（M5）`.dvec/` | 分布式对象存储 | PG heap | 本地目录 | Lance 列存 | SQLite / 内存 |
| **WAL/崩溃恢复** | ✅（M5） | ✅ | ✅ PG WAL | ✅ | ✅ | ❌ |
| **水平扩展** | ❌ 单机 | ✅ 分片 + 副本 | 有限（PG 分区） | ✅（Qdrant Cloud） | 有限 | ❌ |
| **VectorData 集成** | ✅ 原生（M7） | 适配层 | 适配层 | 适配层 | 适配层 | 适配层 |
| **Semantic Kernel** | ✅ 直接支持 | 需插件 | 需插件 | 需插件 | 需插件 | 需插件 |
| **gRPC API** | 计划（M9） | ✅ | ❌ | ✅ | ❌ | ❌ |
| **容器镜像大小** | < 10 MB（AOT, M9） | ~500 MB | ~500 MB（含 PG） | ~50 MB | ~100 MB | ~200 MB |
| **启动时间** | < 10 ms（AOT, M9） | ~30 s（集群） | ~5 s（PG） | ~1 s | < 100 ms | < 500 ms |
| **许可证** | MIT | Apache 2.0 | PostgreSQL | Apache 2.0 | Apache 2.0 | Apache 2.0 |

---

## 使用场景对照

### 场景 1：.NET 桌面/移动应用本地 AI 助手

**推荐**：✅ **DotVector**

原因：
- NuGet 直接引用，无需外部进程
- 适合 WPF、MAUI、Blazor 桌面应用
- 内存向量库（M2），无需磁盘持久化

### 场景 2：ASP.NET Core + Semantic Kernel RAG Pipeline

**推荐**：✅ **DotVector**（嵌入式场景）或 ❓ **Qdrant**（高并发生产）

原因：
- DotVector M7 实现 `IVectorStore`，与 SK 天然集成
- 小中型应用（< 500 万向量）DotVector 足够
- 高并发、大规模（> 5000 万向量）考虑 Qdrant

### 场景 3：已有 PostgreSQL 基础设施

**推荐**：❓ **pgvector**

原因：pgvector 直接集成到 PG，利用 SQL 过滤能力，DotVector 无法替代这个场景。

### 场景 4：Python/Jupyter 快速原型

**推荐**：❓ **Chroma** 或 **LanceDB**（Python 生态）

原因：DotVector 是 .NET 库，不适合 Python 使用场景。

### 场景 5：十亿级向量生产系统

**推荐**：❓ **Milvus**（分布式）或 **Qdrant**（单机超大规模）

原因：DotVector 定位嵌入式单机，不支持水平扩展（M0～M9 阶段）。

### 场景 6：Kubernetes Sidecar / Serverless Function

**推荐**：✅ **DotVector**（M9 AOT 后）

原因：
- Native AOT 单文件 < 10 MB
- 启动时间 < 10 ms，完美适合冷启动场景
- 比 Qdrant 容器（~50 MB）更轻量

---

## 性能预期（M3 HNSW，与 Qdrant 对比）

以下为**预期目标**（将在 M8 基准测试后填入实测数据）：

| 指标 | DotVector M3 | Qdrant | 差距 |
|------|:---:|:---:|:---:|
| 构建速度（10万条/128维） | ~ 5s | ~ 3s | < 2x |
| KNN 搜索 P50（1M条/128维） | ~ 2ms | ~ 1ms | < 2x |
| KNN 搜索 P99 | ~ 10ms | ~ 5ms | < 2x |
| Recall@10（M=16, ef=200） | ≥ 0.95 | ≥ 0.97 | 相当 |
| 内存占用（10万条/128维） | ~ 60 MB | ~ 50 MB | ~ 1.2x |

> 注：DotVector 的目标是 .NET 生态内的最优选择，对比 Qdrant（Rust）差距控制在 2x 以内为合理目标。

---

## 版本历史

| 版本 | 内容 |
|------|------|
| M0（本 PR） | 初始对比表（基于目标定位，无实测数据） |
| M8 | 填入 BenchmarkDotNet 实测数据 |
