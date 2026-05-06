# ROADMAP

DotVector 路线图，按 Milestone 划分。每个 Milestone 对应一个或多个 PR，验收标准明确。

**状态图例**：✅ 已完成　🚧 进行中　⏳ 未开始

| Milestone | 状态 | 主题 |
|-----------|:----:|------|
| M0 | ✅ | 工程骨架 + 文档 + 设计基线 |
| M1 | ✅ | 距离函数与 SIMD 内核 |
| M2 | ✅ | 内存索引 — Brute Force / Flat |
| M3 | ✅ | HNSW 索引 |
| M4 | ✅ | IVF / IVF-PQ 索引 |
| M5 | ⏳ | 持久化层（目录格式 + mmap + WAL） |
| M6 | ⏳ | 标量过滤（Payload Filter） |
| M7 | ⏳ | `Microsoft.Extensions.VectorData` 适配 |
| M8 | ⏳ | BenchmarkDotNet 基准 + 对照 |
| M9 | ⏳ | gRPC Server + Native AOT + Docker |

---

## ✅ M0 — 工程骨架 + 文档 + 设计基线

**目标**：建立可构建、可测试的项目骨架，完成所有架构决策文档。

**验收标准**：
- [ ] `dotnet restore` 与 `dotnet build -c Release` 在 .NET 10 SDK 下零警告通过
- [ ] `dotnet test -c Release` 通过，每个测试项目至少 1 个 smoke 测试
- [ ] `DotVector.slnx` 能正确加载所有项目
- [ ] 所有目录都有 `.gitkeep` 或占位文件
- [ ] `AGENTS.md` / `ROADMAP.md` / `docs/` 内容完整
- [ ] CI workflow 在 PR 上跑通（ubuntu / windows / macos 三平台）

**状态**：✅ 本 PR

---

## ✅ M1 — 距离函数与 SIMD 内核

**目标**：实现所有核心距离函数，充分利用 .NET 10 `TensorPrimitives` 与 `Vector512<T>`。

**实现内容**：
- `L2`（欧氏距离）— `TensorPrimitives.Distance`
- `Cosine`（余弦距离）— `TensorPrimitives.CosineSimilarity`
- `InnerProduct`（内积 / 点积）— `TensorPrimitives.Dot`
- `Hamming`（汉明距离）— 二值向量，`BitOperations.PopCount`
- `DotProduct`（归一化内积）
- Scalar 回退路径（确保跨平台一致性）
- fp16 / bf16 / int8 量化精度的通用数学接口（`IFloatingPointIeee754<T>`）

**参考**：FAISS 内核、hnswlib `space_l2.h`、Qdrant `common/common_cpu.rs`

**验收标准**：
- [ ] 所有距离函数通过 SIMD vs scalar 精度一致性测试（差 < 1e-5）
- [ ] BenchmarkDotNet 基准：L2 距离吞吐量与 FAISS C++ 内核在同一数量级（1 亿次/秒量级）
- [ ] 测试覆盖：零向量、单元素、高维（4096 维）、`NaN`/`Infinity` 异常处理

---

## ✅ M2 — 内存索引 — Brute Force / Flat

**目标**：实现内存中的精确最近邻搜索，类似 FAISS `IndexFlat`、Milvus `FLAT`。

**实现内容**：
- `FlatIndex<TKey>` — 线性扫描，所有距离函数支持
- `IIndex<TKey>` 接口（在 `DotVector.Core` 定义）
- `SearchRequest` / `SearchResult<TKey>` 模型
- `VectorDatabase` / `Collection<TKey>` 顶层 API
- 线程安全的并发只读支持（`ReaderWriterLockSlim`）
- 批量插入 API（`InsertBatch`）

**参考**：FAISS `IndexFlatL2`、Milvus `BruteForce`

**验收标准**：
- [ ] Recall@10 = 1.0（精确搜索，无近似损失）
- [ ] 并发只读测试通过（N 线程同时 Search，无数据竞争）
- [ ] 10 万条 384 维向量，搜索延迟 < 50 ms（单线程，M1 SIMD 加速）
- [ ] round-trip 测试：Insert → Search → 结果一致

---

## ✅ M3 — HNSW 索引

**目标**：实现 HNSW（Hierarchical Navigable Small World）图索引，纯托管 C#。

**实现内容**：
- `HnswIndex<TKey>` — 可调参数：`M`、`EfConstruction`、`EfSearch`
- 分层图结构，`HnswNodeHeader`（`[StructLayout(Sequential, Pack=1)]` unmanaged struct）
- 增量插入（`Insert` 时自动构建图）
- 近似 KNN 搜索
- 序列化 / 反序列化索引（预留 M5 持久化接口）

**参考**：
- 论文：Malkov & Yashunin, 2016 "Efficient and robust approximate nearest neighbor search using Hierarchical Navigable Small World graphs"
- hnswlib（C++）：https://github.com/nmslib/hnswlib
- Qdrant HNSW：https://github.com/qdrant/qdrant/tree/master/lib/segment/src/index/hnsw_index
- Milvus HNSW：https://github.com/milvus-io/milvus/tree/master/internal/core/src/index/hnsw

**验收标准**：
- [x] Recall@10 ≥ 0.95（1000×64 随机数据 × 4 种距离 × 4 种 seed，见 `HnswRecallTests`）
- [ ] 构建速度：10 万条 128 维向量，构建时间 < 10 秒（待 M8 基准对比）
- [ ] 内存占用与 hnswlib 同数量级（< 2x 差距）（待 M8 基准对比）
- [x] M/EfConstruction/EfSearch 参数可调，有中文 XML 文档注释

---

## ✅ M4 — IVF / IVF-PQ 索引

**目标**：实现倒排文件索引（IVF）和乘积量化（IVF-PQ），适合大规模向量集合。

**实现内容**：
- `IvfFlatIndex<TKey>` — K-Means 聚类，倒排列表，`IvfListHeader`（unmanaged struct，28 字节）
- `IvfPqIndex<TKey>` — 残差 PQ 编码（乘积量化），压缩存储
- `PqCodebook` — PQ 码本训练（每子空间独立 K-Means，`Ksub=2^NBits`）
- `KMeans` — 纯 BCL K-Means++ 训练
- `NProbe` 参数（搜索时探测的倒排列表数）
- `IndexKind.IvfFlat` / `IvfPq` + `VectorDatabase.CreateCollection<TKey>` 类型化重载

**参考**：
- 论文：Jégou et al., "Product Quantization for Nearest Neighbor Search"
- FAISS `IndexIVFFlat` / `IndexIVFPQ`：https://github.com/facebookresearch/faiss
- Milvus `IVF_FLAT` / `IVF_PQ`：https://milvus.io/docs/index.md

**验收标准**：
- [x] IVF-Flat Recall@10 ≥ 0.90（聚类数据集 N=1024×64，NList=16/NProbe=6 ≈ 38% 探查 × 4 距离 × 4 seed，见 `IvfRecallTests`）
- [x] IVF-PQ Recall@10 ≥ 0.50（同数据集，NList=16/NProbe=8/M=8/NBits=8 × 2 seed）
- [x] `IvfListHeader` round-trip 测试通过
- [ ] IVF-PQ 内存压缩比 ≥ 8x（相比 Flat，待 M8 基准对比）

---

## ⏳ M5 — 持久化层（目录格式 + mmap + WAL）

**目标**：实现**单目录持久化**存储格式（`.dvec/` 目录），支持崩溃恢复，性能优于单文件方案。

### 为何选择目录而非单文件？

| 维度 | 单文件（SQLite/LiteDB 风格） | 目录（RocksDB/LanceDB/Qdrant 风格） |
|------|:--------------------------:|:-----------------------------------:|
| **随机写放大** | 高（所有 Segment 共享一个 fd） | 低（每个 Segment 独立 fd） |
| **mmap 粒度** | 整个文件 | 每个 Segment 独立 mmap，OS 可精确管理页面 |
| **增量追加** | 需要内部空闲页管理 | 直接新建文件，无内部碎片 |
| **Compaction** | 需 copy-on-write 整个文件 | 只替换涉及的 Segment 文件 |
| **并行 IO** | 受单文件 fd 限制 | 多 Segment 可并行 mmap / pread |
| **崩溃恢复** | 复杂（内部页校验） | 简单（WAL 文件 + Segment 原子替换） |
| **实现复杂度** | 高（需实现内部页分配器） | 低（文件系统承担分配职责） |
| **备份/迁移** | 简单（复制 1 个文件） | 简单（`rsync` 或 `zip` 整个目录） |

> **结论**：向量数据库场景写入量大、Segment 生命周期独立，目录方案性能更好、实现更简洁，
> 与 LanceDB、Qdrant、RocksDB 的业界实践一致。
> 单目录（`.dvec/`）对用户来说仍是一个逻辑上的"单个数据库"，管理便利性不受影响。

### 目录布局

```
my-database.dvec/
├── catalog.bin               # 集合元数据（unmanaged struct FileHeader + CollectionHeader[]）
├── wal/
│   ├── wal-000001.log        # WAL 段文件（顺序追加，定期截断）
│   └── wal-000002.log
└── collections/
    └── {collection-name}/
        ├── segments/
        │   ├── seg-000001/
        │   │   ├── seg.hdr   # SegmentHeader（unmanaged struct, little-endian）
        │   │   ├── vectors.bin  # float32 向量数据（mmap'd, 行优先）
        │   │   └── index.bin    # 索引数据（HNSW 图 / IVF 倒排列表）
        │   └── seg-000002/
        │       └── ...
        └── snapshots/        # Compaction 后的快照（原子替换 segments/）
```

**实现内容**：
- `FileHeader`（已有骨架）+ `SegmentHeader` + `CollectionHeader`（全部 `[StructLayout(Sequential, Pack=1)]`）
- WAL（Write-Ahead Log）— `WalWriter` / `WalReader`（顺序追加 + 定期截断）
- Memory-Mapped File — 每个 `vectors.bin` 独立 `MemoryMappedFile`，零拷贝读取
- `SpanReader` / `SpanWriter` — 基于 `BinaryPrimitives` + `MemoryMarshal`
- Catalog 持久化（`catalog.bin` — 集合元数据原子写入）
- 崩溃恢复：WAL replay（重放未 flush 的写操作）
- Compaction — 合并小 Segment，原子替换目录

**参考**：
- LanceDB 目录格式：https://github.com/lancedb/lance/blob/main/docs/format.rst（Fragment + Dataset 模型）
- RocksDB SST + WAL 设计（Segment 不可变原则）
- Qdrant 目录格式：https://github.com/qdrant/qdrant/tree/master/lib/segment

**验收标准**：
- [ ] 写入 → 关闭 → 重新打开 → 搜索，结果一致（round-trip）
- [ ] WAL replay 测试：模拟崩溃（直接 kill 进程）后数据不丢失
- [ ] 并发只读：多线程同时 mmap 读取同一 Segment，无数据竞争
- [ ] Compaction 测试：Segment 合并后结果与合并前一致
- [ ] `SegmentHeader` / `CollectionHeader` round-trip 测试（`AsBytes` → `MemoryMarshal.Read`）
- [ ] 格式版本升级测试：旧版本 `catalog.bin` 被正确拒绝或迁移
- [ ] 目录布局在 Windows / Linux / macOS 上均能正确创建

---

## ⏳ M6 — 标量过滤（Payload Filter）

**目标**：支持在向量搜索时附加标量条件过滤，类似 Qdrant payload index / pgvector `WHERE`。

**实现内容**：
- `VectorRecord<TKey>` 支持 payload 字段（`Dictionary<string, object>`）
- `SearchRequest` 支持 `Filter` 条件（简单 AND / OR / range / equality）
- Pre-filtering（先过滤再搜索）和 Post-filtering（先搜索再过滤）策略
- 简单标量索引（B-tree 风格）

**参考**：
- Qdrant payload index：https://qdrant.tech/documentation/concepts/filtering/
- Milvus scalar field filter：https://milvus.io/docs/boolean.md
- pgvector WHERE 子句

**验收标准**：
- [ ] 带过滤搜索 Recall 与无过滤版本差 < 5%
- [ ] 过滤条件测试：equality / range / null check
- [ ] 大集合（100 万条）带过滤搜索延迟 < 100 ms

---

## ⏳ M7 — `Microsoft.Extensions.VectorData` 适配

**目标**：实现 `IVectorStore` / `IVectorStoreRecordCollection` 接口，与 Semantic Kernel 深度集成。

**架构说明**：`DotVector.Data`（客户端适配层）通过 `IDotVectorClient` 接口与服务端通信，**不直接引用** `DotVector`（服务端）。

**实现内容**：
- `DotVectorVectorStore` — 实现 `IVectorStore`，注入 `IDotVectorClient`
- `DotVectorCollection<TKey, TRecord>` — 实现 `IVectorStoreRecordCollection<TKey, TRecord>`
- `VectorStoreRecordDefinition` 支持
- 与 Semantic Kernel Memory / RAG pipeline 集成示例
- DI 扩展方法：`services.AddDotVectorVectorStore(client)`

**参考**：
- `Microsoft.Extensions.VectorData.Abstractions`：https://github.com/dotnet/extensions
- Semantic Kernel VectorData providers

**验收标准**：
- [ ] 通过 `IVectorStore` 抽象完成增删改查
- [ ] 与 Semantic Kernel TextMemory 集成 smoke 测试通过
- [ ] 符合 `VectorStoreRecordDefinition` 规范（字段映射、向量字段标注）
- [ ] `DotVector.Data` 项目无对 `DotVector`（服务端）程序集的直接引用（CI 检查）

---

## ⏳ M8 — BenchmarkDotNet 基准 + 对照 Qdrant / Milvus / pgvector

**目标**：建立完整的性能基准，与主流向量数据库横向对比。

**实现内容**：
- `tests/DotVector.Benchmarks` — BenchmarkDotNet 完整基准套件
- `eng/benchmarks/start-benchmark-env` — 用 Testcontainers 自动拉起 Qdrant / Milvus / pgvector
- 基准项目：
  - 批量插入吞吐量（向量/秒）
  - KNN 搜索延迟（P50 / P99）
  - 内存占用
  - 构建时间
  - AOT vs JIT 性能差异
- 对照数据集：SIFT-1M（128 维）、GloVe-1.2M（100 维）

**参考**：
- ANN-Benchmarks：https://github.com/erikbern/ann-benchmarks

**验收标准**：
- [ ] 与 Qdrant（Rust）在相同硬件上对比，DotVector M3 HNSW 差距 < 2x
- [ ] 与 pgvector（C）在相同硬件上对比，DotVector M3 HNSW 性能相当或更好
- [ ] 基准报告自动生成 Markdown 表格
- [ ] CI 中 benchmark 在 PR 上生成对比报告（基线 vs 当前）

---

## ⏳ M9 — gRPC Server + Native AOT 单文件部署 + Docker 镜像

**目标**：提供可选的 gRPC server 模式，支持 Native AOT 编译，生成 Docker 镜像。同时完善客户端/服务端的双向连接实现。

**实现内容**：
- `DotVector.Cli` — gRPC server 模式（`dotnet-grpc`）
- Protobuf 定义（VectorService：Insert / Search / Delete / CreateCollection）
- `GrpcDotVectorClient : IDotVectorClient`（位于 `DotVector.Data`）— gRPC 传输，供远程访问使用
- `LocalDotVectorClient : IDotVectorClient`（位于 `DotVector`）— 进程内直连，零序列化，供嵌入式使用
- Native AOT 发布配置（`PublishAot=true`）
- `Dockerfile` — 多阶段构建，最终镜像基于 `mcr.microsoft.com/dotnet/runtime-deps`
- `docker-compose.yml` — 一键启动

**参考**：
- Qdrant gRPC API：https://github.com/qdrant/qdrant/blob/master/lib/api/src/grpc/proto/qdrant.proto
- Milvus gRPC API：https://github.com/milvus-io/milvus-proto

**验收标准**：
- [ ] gRPC server 启动，接受 Insert / Search 请求
- [ ] Native AOT 单文件 < 10 MB，启动时间 < 10 ms
- [ ] Docker 镜像 < 50 MB
- [ ] CI 中 AOT 构建通过

---

## ⏳ 预留 Milestone

| Milestone | 内容 | 参考 |
|-----------|------|------|
| M10 | DiskANN（Vamana 图）— 磁盘索引，适合内存放不下的大规模数据集 | microsoft/DiskANN |
| M11 | 量化：SQ8 / PQ / OPQ / 残差量化 | FAISS, Milvus |
| M12 | GPU / ONNX-Runtime 加速（可选，若平台支持） | ONNX Runtime ExecutionProvider |
| M13 | 分布式分片 — 一致性哈希路由，多节点扩展 | Milvus 分布式架构 |

---

## 参考资源

### 学术论文
- HNSW：[Efficient and robust approximate nearest neighbor search using Hierarchical Navigable Small World graphs](https://arxiv.org/abs/1603.09320)
- IVF-PQ：[Product Quantization for Nearest Neighbor Search](https://inria.hal.science/inria-00514462/document)
- DiskANN：[DiskANN: Fast Accurate Billion-point Nearest Neighbor Search on a Single Node](https://proceedings.neurips.cc/paper/2019/hash/09853c7fb1d3f8ee67a61b6bf4a7f8e6-Abstract.html)
- ScaNN：[Accelerating Large-Scale Inference with Anisotropic Vector Quantization](https://arxiv.org/abs/1908.10396)

### 开源实现
- [hnswlib](https://github.com/nmslib/hnswlib) — HNSW C++ 参考实现
- [FAISS](https://github.com/facebookresearch/faiss) — Facebook AI 向量索引库
- [Qdrant](https://github.com/qdrant/qdrant) — Rust 向量数据库
- [Milvus](https://github.com/milvus-io/milvus) — 分布式向量数据库
- [pgvector](https://github.com/pgvector/pgvector) — PostgreSQL 向量扩展
- [LanceDB](https://github.com/lancedb/lancedb) — 嵌入式列存向量数据库
- [DiskANN](https://github.com/microsoft/DiskANN) — 微软磁盘向量索引
- [SPTAG](https://github.com/microsoft/SPTAG) — 微软 SPTAG 图索引
