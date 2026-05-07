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
| M5 | ✅ | 持久化层（目录格式 + WAL） |
| M6 | ✅ | 标量过滤（Payload Filter） |
| M7 | ✅ | `Microsoft.Extensions.VectorData` 适配 |
| M7.1 | ✅ | VectorData GetAsync(key/keys) + IncludeVectors（M7 延续） |
| M7.2 | ✅ | VectorData LINQ Filter Expression 翻译（M7 延续） |
| M7.3 | ✅ | VectorData Dynamic / ListCollectionNames / Definition（M7 延续） |
| M8 | ⏳ | BenchmarkDotNet 基准 + 对照 |
| M9 | ✅ | gRPC Server + Native AOT + Docker |
| M10 | ✅ | Segment Flush + mmap 零拷贝读路径 + Compaction（M5 延续） |
| M11 | ✅ | Payload 持久化 + 标量 B-tree 索引（M6 延续） |
| M12 | ✅ | DiskANN（Vamana 图）+ 磁盘驻留索引 |
| M13 | ✅ | 量化扩展：SQ8 / OPQ / RQ + 通用量化抽象 |
| M14 | ✅ | 硬件加速接口：`IBatchScorer` 抽象（CE 仅保留 M14.1；具体加速后端见 [DotVectorEE](https://github.com/IoTSharp/DotVectorEE)）|

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

## ✅ M5 — 持久化层（目录格式 + WAL）

**目标**：实现**单目录持久化**存储格式（`.dvec/` 目录），支持崩溃恢复，性能优于单文件方案。

> 当前已落地：`.dvec/` 目录布局、`catalog.bin` 原子写入、WAL（CRC32 + torn-write 截断）+ 重启回放。
> mmap 读路径与 Compaction 留作后续 milestone（不阻塞 M6/M7）。

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
- [x] 写入 → 关闭 → 重新打开 → 搜索，结果一致（round-trip，见 `PersistenceTests`）
- [x] WAL replay 测试：torn write / CRC mismatch 截断后仍能恢复完整记录（见 `WalReaderWriterTests`）
- [x] `CatalogStore` round-trip 测试 + 原子覆盖写入（见 `CatalogStoreTests`）
- [x] 格式版本不匹配（Magic / Version）→ `DotVectorException`（见 `CatalogStoreTests`）
- [x] 目录布局在 Windows / Linux / macOS 上均能正确创建（CI 三平台）
- [x] mmap 单拷贝读路径（已在 **M10** 实现）
- [x] Compaction 测试：Segment 合并后结果与合并前一致（已在 **M10** 实现）

---

## ✅ M6 — 标量过滤（Payload Filter）

**目标**：支持在向量搜索时附加标量条件过滤，类似 Qdrant payload index / pgvector `WHERE`。

**实现内容**：
- `VectorRecord<TKey>` 支持 payload 字段（`Dictionary<string, object>`）✅
- `Filter` AST：`Eq` / `Ne` / `Range` / `Exists` / `Missing` / `And` / `Or` / `Not`（reflection-free，AOT 友好）✅
- `Collection<TKey>.Search(query, topK, Filter?)` 重载：底层索引 over-fetch + Collection 层 post-filter（默认过取倍率 8）✅
- `Collection<TKey>.GetPayload(key)` 暴露 in-memory payload 快照 ✅
- 简单标量索引（B-tree 风格）— 推迟到 **M11**
- payload 持久化（写入 WAL / Segment）— 推迟到 **M11**（当前 payload 仅保存在内存中）

**参考**：
- Qdrant payload index：https://qdrant.tech/documentation/concepts/filtering/
- Milvus scalar field filter：https://milvus.io/docs/boolean.md
- pgvector WHERE 子句

**验收标准**：
- [x] 带过滤搜索 Recall 与无过滤版本差 < 5%（`tests/DotVector.Accuracy.Tests/FilteredRecallTests.cs`，FlatIndex 上 Recall = 1.0）
- [x] 过滤条件测试：equality / range / null check（`tests/DotVector.Core.Tests/Query/FilterTests.cs` + `FilteredSearchTests.cs`）
- [ ] 大集合（100 万条）带过滤搜索延迟 < 100 ms（推迟到 **M8** BenchmarkDotNet 基准与 **M11** B-tree 索引上线后验证）

---

## ✅ M7 — `Microsoft.Extensions.VectorData` 适配

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
- [x] 通过 `VectorStore` / `VectorStoreCollection<TKey, TRecord>` 抽象完成增删改查（Upsert / Delete / Search；`GetAsync` 暂未实现，标 TODO M7+）
- [ ] 与 Semantic Kernel TextMemory 集成 smoke 测试通过（推迟，待 SK 升级到 VectorData 9.5.0 后补）
- [x] 符合 `[VectorStoreKey]` / `[VectorStoreVector]` / `[VectorStoreData]` 特性规范（`DotVectorRecordMapper`）
- [x] `DotVector.Data` 项目无对 `DotVector`（服务端）程序集的直接引用（`tests/DotVector.Tests/SmokeTests.cs` 程序集引用断言）

**已知局限（拆分至 M7.1 / M7.2 / M7.3）**：
- `GetAsync(key)` / `GetAsync(keys)` 未实现 → **M7.1**
- `SearchAsync` 不支持 `IncludeVectors=true` → **M7.1**
- `SearchAsync` 不支持非空 `Filter` 表达式（LINQ Expression → DotVector Filter 翻译器） → **M7.2**
- `GetAsync(filter)` 未实现（依赖 M7.2 翻译器） → **M7.2**
- `GetDynamicCollection` / `ListCollectionNamesAsync` 未实现 → **M7.3**
- `VectorStoreCollectionDefinition` 仅支持反射推断模式，不支持显式定义传入 → **M7.3**

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

## ✅ M9 — gRPC Server + Native AOT 单文件部署 + Docker 镜像

**目标**：提供可选的 gRPC server 模式，支持 Native AOT 编译，生成 Docker 镜像。同时完善客户端/服务端的双向连接实现。

**实现内容**：
- `src/DotVector/Server/DotVectorServer.cs` — `Build(dataDirectory, port, args, loopbackOnly, httpsCertificate)` Kestrel HTTP/2 宿主，默认 h2c，可选 HTTPS（h2 over TLS、ALPN）
- `protos/dotvector.proto` + `src/DotVector/Grpc/VectorServiceImpl.cs` — VectorService（Ping / CreateCollection / DeleteCollection / ListCollections / Upsert / Delete / Search / Get / Scroll）
- `src/DotVector.Data/Grpc/GrpcDotVectorClient.cs : IDotVectorClient` — gRPC 传输；默认 `SocketsHttpHandler` 关闭代理，避免本机 loopback 被 HTTP_PROXY/PAC 劫持；`ModuleInitializer` 提前开 `Http2UnencryptedSupport` 支持 h2c prior-knowledge
- `src/DotVector.Data/LocalDotVectorClient.cs : IDotVectorClient` — 进程内直连，零序列化，供嵌入式使用
- `src/DotVector.Cli` — 远程命令（`ping`、`collections list/create/delete`、`--endpoint`），`PublishAot=true` 单文件发布通过
- `Dockerfile` 多阶段构建 + `docker-compose.yml` / `docker-compose.override.yml` / `docker-compose.dcproj` 一键启动

**参考**：
- Qdrant gRPC API：https://github.com/qdrant/qdrant/blob/master/lib/api/src/grpc/proto/qdrant.proto
- Milvus gRPC API：https://github.com/milvus-io/milvus-proto

**验收标准**：
- [x] gRPC server 启动，接受 Insert / Search 请求（`tests/DotVector.Tests/GrpcServerIntegrationTests.cs` 端到端验证）
- [x] Native AOT 单文件发布通过（0 trim/AOT 警告）
- [x] Docker 镜像构建通过
- [x] CI 中 AOT 构建通过

---

## ✅ M10 — Segment Flush + mmap 零拷贝读路径 + Compaction（M5 延续）

**背景**：M5 只实现了 catalog + WAL 顺写与 replay，以下项从 M5 验收标准中推迟到本 milestone。

**实现内容**：
- `MemTable` 阈值触发 flush，写入 `seg-{seq}/seg.hdr`、`vectors.bin`（float32 行优先）、`index.bin`
- `MemoryMappedFile` + `MemoryMappedViewAccessor` 封装，使用 `MemoryMarshal.Cast<byte, float>(view.AsSpan())` 零拷贝读取 Segment
  - safe-only 约束下，走 `MemoryMappedViewStream` + `Span` 路径；AGENTS.md M0–M7 禁 unsafe 仍然适用
- WAL 裁剪：Segment 落盘后裁剪对应 WAL 段
- Compaction：多个小 Segment 合并为大 Segment，写新目录后原子提交（`File.Move` + `catalog.bin` 重写）
- 崩溃恢复测试：flush 中途 / Compaction 中途 中断后重启一致性

**验收标准**：
- [x] mmap 单拷贝（safe-only：`MemoryMappedViewAccessor.ReadArray<float>`）读路径上线
- [x] Compaction 测试：Segment 合并后搜索结果与合并前一致（`CompactionTests`）
- [x] flush 中途崩溃后重启仍能恢复（`CrashRecoveryTests`：残留 `.tmp` 段被忽略）
- [x] WAL 裁剪：Segment 落盘后已覆盖的 WAL 段被删除（`WalTrimTests`）

---

## ✅ M11 — Payload 持久化 + 标量 B-tree 索引（M6 延续）

**背景**：M6 仅实现了内存 payload 与 Collection 层 post-filter 过滤，以下项从 M6 验收标准推迟到本 milestone。

**实现内容**：
- Payload 序列化与持久化：`PayloadCodec` 手写 TLV（key=UTF-8 + 类型 tag：null/bool/long/double/string/bytes），纯 `BinaryPrimitives` 小端读写，零第三方依赖
  - WAL 新增 type=3 `SetPayload` 记录类型；`WalReader` 同步 replay
  - Segment flush 时写 `seg-{seq}/payload.bin`，mmap 读后 `RestorePayload` 注入运行时 dict
- 标量 B-tree 索引：`ScalarIndex<TKey>`，数值字段 `SortedDictionary<double, HashSet<TKey>>` 支持 Eq+Range，字符串/布尔用 hash 桶；`Collection.SetPayload/Delete` 路径上同步维护
- Filter pre-filter 下推：`FilterIntrospection` reflection-free 桥接私有 sealed Filter 节点 → record view → `ScalarIndex.TryResolveCandidates`；命中后走 `FlatIndex.SearchSubset` 仅扫描候选行；不可下推（Or/Not/Ne/Exists/Missing）回退到 8× over-fetch + post-filter
- `Collection.GetPayload(key)` 重启后通过 WAL replay + Segment payload.bin 完整恢复

**验收标准**：
- [x] payload 重启后不丢失（WAL replay + Segment 重载）
- [ ] 大集合（100 万条）带过滤搜索延迟 < 100 ms（需 M8 基准体系验证）
- [x] B-tree 索引在 Eq/Range/And 可下推场景下优于 post-filter（pre-filter 仅扫描候选行）

---

## ✅ M7.1 — VectorData GetAsync(key/keys) + IncludeVectors（M7 延续）

**背景**：M7 适配层完成了 Upsert / Delete / Search 的最小闭环，但未实现按 key 取回与向量回传，下游 RAG / 召回审计场景必需。本 milestone 闭合这一缺口。

**实现内容**：
- `IDotVectorClient` 协议扩展：
  - `GetAsync(string collectionName, IReadOnlyList<string> ids, bool includeVector, CancellationToken)` → `IReadOnlyList<VectorRecordDto>`
  - `VectorRecordDto`：`Id` + `Vector?`（`includeVector=false` 时为 null）+ `Payload`
- `DotVector.Core.Api.Collection<TKey>` 新增：
  - `TryGet(TKey key, out VectorRecord<TKey>? record)` — MemTable 查；未命中再扫 Segment（mmap 切片读 vectors.bin + 已加载的 payload）
  - `GetMany(ReadOnlySpan<TKey> keys, bool includeVectors)` — 批量
- `LocalDotVectorClient` / `InMemoryDotVectorClient` 同步实现 `GetAsync`
- `Protocol.VectorSearchResult` 新增 `float[]? Vector` 字段；`SearchAsync` 路径在 `IncludeVectors=true` 时回填
- `DotVectorCollection<TKey,TRecord>`：
  - `GetAsync(TKey key, RecordRetrievalOptions?)` / `GetAsync(IEnumerable<TKey>, ...)` 走 `IDotVectorClient.GetAsync`
  - `SearchAsync` 透传 `options.IncludeVectors` 到 protocol，Mapper 反向把 `Vector` 写回 `[VectorStoreVector]` 属性

**验收标准**：
- [x] `Collection<TKey>.TryGet` round-trip 测试：Upsert → Flush（强制 Segment 化）→ 重新打开 DB → `TryGet` 命中且向量 / payload 字节级一致
- [x] `DotVectorCollection.GetAsync(key)` 与 `GetAsync(keys)` 在嵌入式与 InMemory 客户端下行为一致
- [x] `SearchAsync` 在 `IncludeVectors=true` 时返回 `TRecord` 的向量属性非空
- [x] AOT 编译路径无新增 IL 警告

---

## ✅ M7.2 — VectorData LINQ Filter Expression 翻译（M7 延续）

**背景**：M7 的 `SearchAsync` 与未来的 `GetAsync(filter)` 都要求把 `Expression<Func<TRecord,bool>>` 翻译为 DotVector 的 sealed Filter AST，才能复用 M11 已落地的 B-tree pre-filter 下推。

**已交付**（详见 CHANGELOG `[Unreleased]` PR #M7.2）：`LinqFilterTranslator`、`VectorScrollRequest`、`IDotVectorClient.ScrollAsync`、`DotVectorCollection.GetAsync(filter, top)`、`SearchAsync` 透传 `VectorSearchOptions.Filter`，配套 10 个 LINQ Filter 单元测试全部通过。

**实现内容**：
- `DotVector.Data.Filters.LinqFilterTranslator`：`ExpressionVisitor` 翻译器
  - 支持 `==` / `!=` / `<` / `<=` / `>` / `>=` / `&&` / `||` / `!`
  - 支持 `string.Equals` / `Contains`（仅 Eq 等价场景）
  - 支持 `payload[key]` 与映射后的 `[VectorStoreData] property` 两种访问形式（reflection-free 经由 mapper 元数据查表）
  - 不支持的节点（方法调用、子查询）→ 抛 `NotSupportedException` 并提示降级方案
- `IDotVectorClient.GetAsync(string collectionName, FilterDto filter, int? limit, CancellationToken)` — 全量 Scan + filter
- `DotVector.Core.Api.Collection<TKey>.Scan(Filter, int? limit)` — 顺序遍历 MemTable + Segment，复用 `FilterIntrospection` 命中 B-tree 时走候选集
- `DotVectorCollection<TKey,TRecord>`：
  - `SearchAsync(...).Filter` 非空时翻译 → 透传到 `IDotVectorClient.SearchAsync.Filter`
  - `GetAsync(Expression<Func<TRecord,bool>> filter, int top, ...)` 实现

**验收标准**：
- [ ] 翻译器单元测试覆盖所有支持运算符的真值表；不支持节点抛清晰异常
- [ ] `SearchAsync` 带 LINQ Filter 等价 `Collection<TKey>.Search(query, topK, Filter)` 结果（顺序、score、命中集）
- [ ] B-tree pre-filter 在 LINQ 翻译路径上仍生效（断言 SearchSubset 被调用）
- [ ] reflection-free：翻译器无 `MethodInfo.Invoke` / `PropertyInfo.GetValue`，所有访问通过预计算的 mapper accessor delegate

---

## ✅ M7.3 — VectorData Dynamic Collection / ListCollectionNames / Definition（M7 延续）

**背景**：补齐 `VectorStore` 抽象层最后两块：枚举集合、动态 schema（无 POCO 定义）。这是 SK Plugin / 通用 RAG framework 接入的前提。

**实现内容**：
- `IDotVectorClient.ListCollectionsAsync(CancellationToken)` → `IReadOnlyList<CollectionInfo>`（name + dim + metric + record count）
  - `LocalDotVectorClient` 走 `VectorDatabase.Catalog`；`InMemoryDotVectorClient` 走内部 dict
- `DotVectorVectorStore`：
  - `ListCollectionNamesAsync` 实现
  - `GetDynamicCollection(string name, VectorStoreCollectionDefinition definition)` 返回 `DotVectorDynamicCollection : VectorStoreCollection<object, Dictionary<string,object?>>`
- `DotVector.Data.Internal.DotVectorRecordMapper<TKey,TRecord>`：新增基于 `VectorStoreCollectionDefinition` 的构造函数（与原 attribute 反射构造共用同一类型），不依赖反射 attribute 扫描
- `DotVectorCollection` 构造接受可选 `VectorStoreCollectionDefinition` 覆盖反射推断结果

**验收标准**：
- [x] `ListCollectionNamesAsync` 在 InMemory 客户端下返回所有已建集合（嵌入式 `LocalDotVectorClient` 留待 M9）
- [x] `GetDynamicCollection` 端到端：建集合 → upsert `Dictionary<string,object?>` → search 命中
- [x] 显式 `VectorStoreCollectionDefinition` 与反射推断模式行为一致（同一组测试参数化两次）
- [x] `DotVector.Data` 仍无对 `DotVector`（服务端壳）的程序集引用（断言保留）

---

## ✅ M12 — DiskANN（Vamana 图）+ 磁盘驻留索引

**目标**：实现 microsoft/DiskANN 的 **Vamana** 图算法，让图本身存盘 + mmap 按需访问，使 1 亿条 384 维向量在 16 GB 内存机器上仍能 P99 < 50 ms。与 M5/M10 衔接：复用现有 `.dvec/segments/seg-{seq}/` 目录与 mmap 读路径，新增 `vamana.bin`。

**实现内容**（新增 `src/DotVector.Core/Index/DiskAnn/`）：
- `VamanaIndex<TKey>` — 实现 `IIndex<TKey>`，纯 BCL + `TensorPrimitives`
- `VamanaBuilder` — 离线/批量构建：随机初始化 → RobustPrune → α 松弛 → 双向边修补；K-Means 选 medoid 作为入口
- `VamanaNodeHeader` — `[StructLayout(Sequential, Pack=1)] unmanaged struct`，固定字段 + 紧随其后的邻居 ID（uint32）
- `VamanaFileFormat` — `vamana.bin` 布局：Header (Magic="DVAN" + Version + R + Alpha + EntryPointId + NodeCount) + per-node `[VamanaNodeHeader, uint[R] neighbors, float[D] vector?]`
- `DiskVamanaReader` — mmap 只读访问；按节点 id 偏移寻址；支持 "向量 inline / 向量分离" 两种布局
- `BeamSearch` — L 大小可调的 best-first；`PriorityQueue<int,float>` + visited bitset（`BitArray`）
- `IndexKind.Vamana` — 新增枚举值 + `VectorDatabase.CreateCollection` 重载
- 与 M11 B-tree pre-filter 集成（候选集投影到 BeamSearch start set）
- 与 M13 `IDistanceKernel<byte>` 互通，支持 "图驻盘 + 距离量化" 组合

**算法参数**（公开为 `VamanaOptions`）：`R` / `L` / `Alpha` / `LSearch` / `BuildBatchSize` / `InlineVectorsInGraph`

**持久化**：升级 `FileHeader.Version`，新增 magic "DVAN"；`SegmentHeader` 增加 `IndexKind` 字段（保留位）。

**参考**：
- 论文：Subramanya et al., 2019, *DiskANN: Fast Accurate Billion-point Nearest Neighbor Search on a Single Node*
- microsoft/DiskANN（C++）：https://github.com/microsoft/DiskANN（仅作算法参考，不做 P/Invoke）
- 现有 `HnswIndex` 作为图索引的工程模板

**验收标准**：
- [x] `VamanaNodeHeader` / `VamanaFileHeader` round-trip 测试通过（little-endian、`MemoryMarshal.Read/Write`）
- [x] 1000×64 随机数据 × 4 距离 × 4 seed Recall@10 ≥ 0.92（与 HNSW 同基准用例）
- [ ] 100k 条 128 维数据：构建时间 < 60 s（单核 CI），查询 P99 < 5 ms（mmap 冷启动后）
- [ ] mmap 模式下进程 RSS < 数据集大小的 1.5x（确认确实是按需读盘）
- [x] 与 `FilterIntrospection` pre-filter 路径联动：候选集 N < 全集时仍能命中
- [ ] AOT 编译路径无新增 IL trim 警告
- [x] `DotVector.Core` 仍零第三方运行时依赖

**PR 切分**：
| PR | 内容 |
|---|---|
| #M12.1 ✅ | `VamanaNodeHeader` + `VamanaFileHeader` unmanaged struct + round-trip 测试 |
| #M12.2 ✅ | `VamanaBuilder`（内存版）+ `BeamSearch` + Recall 测试（与 Flat 比 ≥ 0.92）|
| #M12.3 ✅ | `DiskVamanaReader`（mmap 路径）+ `IndexKind.Vamana` 端到端持久化 round-trip |
| #M12.4 ✅ | 与 M11 B-tree pre-filter 集成（候选集投影到 BeamSearch start set）|

---

## ✅ M13 — 量化扩展：SQ8 / OPQ / RQ + 通用量化抽象

**目标**：把 "量化" 从 IVF 内部细节升格为**一等抽象**（`IVectorQuantizer`），让 Flat / HNSW / Vamana 都能选用 SQ / PQ / OPQ / RQ；同时把 PQ 提升到生产级（OPQ 旋转、ADC 距离表、SIMD 化）。

**实现内容**（沿用 `src/DotVector.Core/Compression/`）：
- `IVectorQuantizer` — 统一接口：`Train(samples)` / `Encode(vector)→bytes` / `Decode(bytes)→vector`（仅调试用）/ `BuildDistanceTable(query)→IDistanceKernel<byte>`
- `ScalarQuantizer8` (SQ8) — per-dim min/max → uint8；`Encode/Decode` 全部走 `TensorPrimitives` 通道；零内存分配热路径
- `ProductQuantizer` — M4 `PqCodebook` 重构升级，分离训练与编码；ADC 距离表 SIMD 加速（`Vector256<float>` gather）
- `OptimizedProductQuantizer` (OPQ) — 学习正交旋转矩阵 R（迭代：固定 R 训 PQ；固定 PQ 解 Procrustes 求 R）；`Encode` = `R·x` 后走 PQ；纯托管 SVD（Householder QR + Jacobi）
- `ResidualQuantizer` (RQ) — 多级残差码本（M 级，每级 K 中心）；适合 8–16 字节预算下的高召回
- `QuantizedDistanceKernel` — 实现 `IDistanceKernel<byte>`，封装 "查询预计算 LUT + 编码扫描"；FlatIndex 与 Vamana 共用

**索引侧适配**：
- `FlatIndex<TKey>` 新增 `WithQuantizer(IVectorQuantizer)` 工厂；存储 `byte[]` 而非 `float[]`
- `IvfPqIndex<TKey>` 重构：内部改用 `ProductQuantizer`，把 OPQ/RQ 作为 drop-in 替换（API 不变，新增 `IvfQuantizerKind`）
- `HnswIndex<TKey>` / `VamanaIndex<TKey>`：可选量化模式下，图边遍历仍用全精度，叶子距离用 ADC 表

**持久化**：
- 每个量化器序列化为 `seg-{seq}/quantizer.bin`，自描述前缀 1 字节 `QuantizerKind`（None/SQ8/PQ/OPQ/RQ）
- `SegmentHeader` 增加 `QuantizerKind` 字段（保留位足够），升级 `FileHeader.Version`
- 码本 / 旋转矩阵 R 全部走 `MemoryMarshal.Cast` + little-endian

**参考**：
- Jégou et al., *Product Quantization for Nearest Neighbor Search*
- Ge et al., *Optimized Product Quantization* (CVPR 2013)
- Chen et al., *Quantization based Fast Inner Product Search* (RQ)
- FAISS `IndexPQ` / `IndexOPQ` / `IndexResidualQuantizer`

**验收标准**：
- [ ] **SQ8**：相对 Flat 内存压缩 4×，Recall@10 ≥ 0.97（SIFT-1M 子集）
- [ ] **PQ**（M=8, NBits=8）：内存压缩 ≥ 16×，Recall@10 ≥ 0.65（取代 M4 验收的 0.50 基线）
- [ ] **OPQ**：在同 M/NBits 下 Recall@10 比 PQ 提升 ≥ 5pp
- [ ] **RQ**（2 级 8-bit）：Recall@10 ≥ 0.80
- [ ] ADC 距离计算吞吐 ≥ 1×10⁹ pair/s（128 维，单核，`Vector256<float>`）
- [ ] `quantizer.bin` round-trip + 跨平台（little-endian）测试通过
- [ ] `IvfPqIndex` 切换到新 `IVectorQuantizer` 后 M4 既有测试全部仍绿
- [ ] 全部 SIMD vs scalar 一致性差 < 1e-4（量化数值容忍度）
- [ ] `DotVector.Core` 仍零第三方运行时依赖

**PR 切分**：
| PR | 内容 |
|---|---|
| #M13.1 | `IVectorQuantizer` 抽象 + `ScalarQuantizer8` + 单测（重建误差 / round-trip）|
| #M13.2 | `ProductQuantizer` 重构（从 `PqCodebook` 抽离）+ ADC `QuantizedDistanceKernel` + SIMD 加速 |
| #M13.3 | `OptimizedProductQuantizer`（含纯托管 SVD 实现 + 收敛单测）|
| #M13.4 | `ResidualQuantizer` + 召回率回归 |
| #M13.5 | `IvfPqIndex` / `FlatIndex` 接入新抽象，`quantizer.bin` 持久化（拆分为 #M13.5a + #M13.5b 完成；按用户决策未升级 `FileHeader.Version`，改为按 sidecar 文件存在性向后兼容）|
| #M13.5a | `IVectorQuantizer.BuildScorer` 全量化器统一 + `QuantizerSerializer` + `QuantizedFlatIndex<TKey>` |
| #M13.5b | `quantizer.bin` 接入 `SegmentWriter` / `SegmentReader` + `IvfPqIndex` 复用 `IQuantizedScorer` |

---

## ✅ M14 — 硬件加速接口：`IBatchScorer`（CE 仅保留抽象）

**范围说明（CE 社区版）**：
为保持 `DotVector.Core` **零第三方运行时依赖**约束，本仓库（CE）**只承担 M14.1**：在 Core 中定义 `IBatchScorer` 抽象与默认 `CpuTensorPrimitivesScorer` 实现，并在 `FlatIndex<TKey>` 上提供注入点。所有具体加速器后端（ONNX Runtime CPU EP / DirectML / CUDA / Metal 等）一律下沉到独立企业版仓库 [DotVectorEE](https://github.com/IoTSharp/DotVectorEE)，作为可选 NuGet 包发布。

**架构**：
```
[CE]  DotVector.Core                       ← 零第三方运行时依赖；定义 IBatchScorer + CpuTensorPrimitivesScorer
[EE]  DotVector.Acceleration.Onnx          ← 依赖 Microsoft.ML.OnnxRuntime（CPU EP）
[EE]  DotVector.Acceleration.Onnx.DirectML ← 依赖 Microsoft.ML.OnnxRuntime.DirectML（Windows）
[EE]  DotVector.Acceleration.Cuda          ← 可选（linux-x64 / win-x64），依赖 Microsoft.ML.OnnxRuntime.Gpu
```

**CE 侧实现内容**：
- `IBatchScorer`（Core）— 接口：`Score(ReadOnlySpan<float> query, ReadOnlySpan<float> dataset, Span<float> scores, Metric)`；批量打分而非单点距离
- `CpuTensorPrimitivesScorer`（Core）— 默认实现，包装现有 `Distance.cs`
- `FlatIndex<TKey>` 注入点 — 接受 `IBatchScorer`，默认 CPU；用户可在构造时替换为 EE 加速器

**CE 验收标准**：
- [x] `DotVector.Core` 项目文件未新增任何第三方包引用
- [x] `IBatchScorer` 接口 round-trip：CPU scorer 与 `Distance.cs` 计算结果在标量路径下 bit-identical
- [x] `FlatIndex<TKey>` 通过可选构造参数注入 `IBatchScorer`，默认参数保持向后兼容

**PR 切分（CE）**：
| PR | 内容 |
|---|---|
| #M14.1 | `IBatchScorer` 接口 + `CpuTensorPrimitivesScorer` + `FlatIndex` 注入点；保持现有 API 兼容（默认参数走 CPU）|

> M14.2（`Acceleration.Onnx` CPU EP）、M14.3（DirectML EP + 基准）、M14.4（CUDA EP）等具体加速后端全部转入 [DotVectorEE](https://github.com/IoTSharp/DotVectorEE) 企业版仓库；进度与基准数据请参考 EE 仓库的 ROADMAP / CHANGELOG。

---

## ⏳ 后续候选 Milestone（未排期）

| Milestone | 内容 | 参考 |
|-----------|------|------|
| M15 | 分布式分片 — 一致性哈希路由，多节点扩展 | Milvus 分布式架构 |

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
