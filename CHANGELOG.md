# CHANGELOG

本文件遵循 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.0.0/) 格式，版本号遵循 [语义化版本](https://semver.org/lang/zh-CN/)。

---

## [Unreleased]

### Added

- PR #M5：M5 — 持久化层（目录格式 + WAL）
  - `.dvec/` 单目录持久化：`catalog.bin` + `wal/wal-{seq:D6}.log` + `collections/{guid:N}/segments/...`
  - `src/DotVector.Core/Catalog/CatalogStore.cs`：`CatalogEntry`（required init 属性）+ `CatalogStore.Read/Write`
    - 文件头 `MagicBytes = "DOTVEC\0\0"u8` + `CurrentVersion = 1`，所有多字节字段 little-endian
    - 原子写入：`.tmp` + `Flush(flushToDisk:true)` + `File.Move(overwrite:true)`
    - Magic / Version 不匹配抛出 `DotVectorException`
  - `src/DotVector.Core/Wal/WalRecord.cs`：`WalRecordType { None, Insert, Delete }` + `WalRecord` 只读结构体
  - `src/DotVector.Core/Wal/WalWriter.cs`：单文件追加式写入器
    - 记录格式：`u32 bodyLen + body + u32 crc32(body)`
    - body：`u8 type + Guid collId + u8 keyTypeCode + key bytes + (Insert: u32 dim + dim*4 字节)`
    - `lock(_lock)` 串行写入；`FileShare.Read` 允许并发读取（崩溃恢复）
  - `src/DotVector.Core/Wal/WalReader.cs`：`ReadAll` / `ReadFile`
    - 截断尾部记录（torn write）→ 停止读取
    - CRC32 校验失败 → 停止读取
    - `FileShare.ReadWrite | FileShare.Delete` 允许同进程内并发写入
  - `src/DotVector.Core/IO/KeyCodec.cs`：`KeyTypeCode` + 通用 key 编解码（Int32/Int64/Guid/String，UTF-8 长度前缀）
    - `Write<TKey>(scoped ref SpanWriter, TKey)` / `Read<TKey>(ref SpanReader)` / `ComputeSize` / `GetCode`
  - `src/DotVector.Core/IO/SpanReader.cs`：新增 `ref struct SpanWriter`（与 `SpanReader` 配套），`WriteBytes(scoped ReadOnlySpan<byte>)`
  - `src/DotVector.Core/Storage/PersistentDirectory.cs`：目录管理 + WAL 写入器复用 + `IWriteSink<TKey>` 注入
    - `Open(directoryPath)` → 创建子目录 + 读 catalog
    - `RegisterCollection` / `UnregisterCollection` / `CreateSink<TKey>` / `ReadWalFor`
    - `Dispose` 关闭并刷新 WAL 句柄
  - `src/DotVector.Core/Api/Collection.cs`：新增 `internal void AttachWriteSink(IWriteSink<TKey>?)`，`Insert` / `Delete` 在写索引前回调 sink 落 WAL
  - `src/DotVector.Core/Api/VectorDatabase.cs`：新增 `VectorDatabase(string directoryPath)` 构造函数
    - 启动时按 catalog 重建集合并回放 WAL（按 KeyType 分发到泛型 `RestoreCollectionTyped<TKey>`）
    - 重放完成后再 `AttachWriteSink`，避免回放阶段触发 WAL 重复写入
    - `CreateCollection` / `DropCollection` 同步 catalog
  - `tests/DotVector.Core.Tests/Persistence/WalReaderWriterTests.cs`：5 个单测
    - Insert/Delete round-trip（Int32 key）
    - 多 key 类型 round-trip（long/Guid/string，Theory）
    - Torn write（截断尾部 5 字节）→ 仅读到完整记录
    - CRC mismatch（翻转 body 字节）→ 停止读取
    - 空目录 → 0 条记录
  - `tests/DotVector.Core.Tests/Persistence/CatalogStoreTests.cs`：4 个单测
    - 多条 entry round-trip（含中文 collection name）
    - 不存在文件 → 空列表
    - Bad magic → `DotVectorException`
    - 原子覆盖写入：第二次写入后第一份内容消失，且无 `.tmp` 残留
  - `tests/DotVector.Core.Tests/Persistence/PersistenceTests.cs`：5 个端到端测试
    - Open → Insert → Dispose → Reopen 数据保持一致
    - Delete 通过 WAL 重放恢复
    - DropCollection 跨 reopen 持久生效
    - 4 种 key 类型（int/long/Guid/string）共存且独立重建
    - 验证目录结构（`wal/` / `collections/` / `catalog.bin`）

- PR #M4：M4 — IVF / IVF-PQ 倒排索引
  - `src/DotVector.Core/Format/IvfListHeader.cs`：`[StructLayout(Sequential, Pack=1)]` `unmanaged struct`，28 字节固定布局，描述每个 IVF 倒排桶的元信息
  - `src/DotVector.Core/Index/Ivf/IvfOptions.cs`：`IvfOptions`（`NList=64` / `NProbe=8` / `MaxIterations=25` / `Seed?`）+ `IvfPqOptions`（继承 `M=8` / `NBits=8`）+ `Validate()`
  - `src/DotVector.Core/Index/Ivf/KMeans.cs`：纯 BCL K-Means++ 训练（`Train(data, count, dim, k, maxIterations, seed, out centroids, out assignments)`）+ `FindNearest`，使用 `TensorPrimitives` 计算距离
  - `src/DotVector.Core/Index/Ivf/IvfFlatIndex.cs`：`IvfFlatIndex<TKey> : IIndex<TKey>, IDisposable`
    - 首次 `Search` 触发自动训练（要求 N ≥ NList）；之后增量分配新向量到最近簇
    - NProbe 簇并行扫描；与 FlatIndex 一致的 Top-K 堆约定（smaller-better 取 `-score`，larger-better 取 `+score`，`EnqueueDequeue`）
    - `ReaderWriterLockSlim(NoRecursion)` 多读单写并发；`Remove` 使用 swap-with-last
    - 拒绝 `Hamming`（`NotSupportedException`）
  - `src/DotVector.Core/Compression/PqCodebook.cs`：PQ 子量化训练（每子空间独立 K-Means，`Ksub = 2^NBits = 256`）+ `Encode` / `BuildLut`
  - `src/DotVector.Core/Index/Ivf/IvfPqIndex.cs`：`IvfPqIndex<TKey> : IIndex<TKey>, IDisposable`
    - 残差量化（vector − centroid）+ PQ 子空间编码；首次 `Search` 训练（要求 N ≥ 256）
    - 通过 PQ 距离查找表（LUT）做近似距离估计；命中向量直接以原始距离 rerank
  - `src/DotVector.Core/Model/IndexKind.cs`：扩展 `IvfFlat=2` / `IvfPq=3`
  - `src/DotVector.Core/Api/Collection.cs`：构造函数新增 `IvfOptions? ivfOptions` / `IvfPqOptions? ivfPqOptions`，按 `IndexKind` 分发到 IVF 实现
  - `src/DotVector.Core/Api/VectorDatabase.cs`：新增类型化重载 `CreateCollection<TKey>(name, dim, metric, IvfOptions)` 和 `CreateCollection<TKey>(name, dim, metric, IvfPqOptions)`
  - `tests/DotVector.Core.Tests/Index/Ivf/KMeansTests.cs`：K-Means 训练正确性 + `FindNearest` 单测
  - `tests/DotVector.Core.Tests/Index/Ivf/IvfFlatIndexTests.cs`：9 个单测（Hamming 拒绝 / 维度校验 / 重复键 / Top-K 排序 4 距离 × Theory / Remove）
  - `tests/DotVector.Core.Tests/Index/Ivf/IvfPqIndexTests.cs`：7 个单测（Hamming 拒绝 / dim%M 校验 / 维度校验 / 近似最近邻）
  - `tests/DotVector.Accuracy.Tests/IvfRecallTests.cs`：聚类数据集（16 簇 / σ=0.3 高斯扰动）上的 Recall@10 验收
    - IVF-Flat：N=1024×64，NList=16 / NProbe=6（≈38% 探查），4 种距离 × 4 seed，Recall@10 ≥ 0.90
    - IVF-PQ：N=1024×64，NList=16 / NProbe=8 / M=8 / NBits=8，2 seed，Recall@10 ≥ 0.50

- PR #M3：M3 — HNSW 图索引
  - `src/DotVector.Core/Index/Hnsw/HnswOptions.cs`：HNSW 参数（`M=16` / `EfConstruction=200` / `EfSearch=50` / `Seed`）+ `Default` + `Validate()`
  - `src/DotVector.Core/Format/HnswNodeHeader.cs`：`[StructLayout(Sequential, Pack=1)]` `unmanaged struct`，40 字节固定布局，含 `[InlineArray(16)]` `NeighborCounts16`
  - `src/DotVector.Core/Index/Hnsw/HnswIndex.cs`：`HnswIndex<TKey> : IIndex<TKey>, IDisposable`（~430 行，安全代码，无 `unsafe`）
    - 多层图（`mL = 1/ln(M)`，`MaxLN=M` / `MaxL0=2*M`），随机层级生成（`Random(Seed)`）
    - 贪心下降 + ef-search（Algorithm 2）+ Algorithm 4 启发式邻居选择（heuristic neighbor selection）
    - 内部统一以 "smaller-better" 度量驱动堆，larger-better 度量（`InnerProduct`）入堆前取负
    - `ReaderWriterLockSlim(NoRecursion)` 多读单写并发；`Remove` 使用 tombstone 软删除
    - 拒绝 `Hamming`（`NotSupportedException`）
  - `src/DotVector.Core/Model/IndexKind.cs`：`enum IndexKind { Flat=0, Hnsw=1 }`
  - `src/DotVector.Core/Api/Collection.cs`：`IIndex<TKey>` 多态化，按 `IndexKind` 选择构造的索引实现
  - `src/DotVector.Core/Api/VectorDatabase.cs`：新增 `CreateCollection<TKey>(name, dim, metric, IndexKind, HnswOptions?)` 重载
  - `tests/DotVector.Core.Tests/Index/Hnsw/HnswIndexTests.cs`：12 个单测（构造校验 / 维度校验 / 重复键 / Top-K 排序 / Remove tombstone / 并发读）
  - `tests/DotVector.Core.Tests/Format/HnswNodeHeaderTests.cs`：`SizeOf` + `MemoryMarshal.Read/Write` round-trip（含全部 16 个 `NeighborCounts` 字段）
  - `tests/DotVector.Accuracy.Tests/HnswRecallTests.cs`：1000×64 随机数据 × 4 种距离 × 4 种 seed 的 Recall@10 ≥ 0.95 验收

- PR #3：M2 — 内存索引（Brute Force / Flat）
  - `src/DotVector.Core/Index/Flat/FlatIndex.cs`：`FlatIndex<TKey> : IIndex<TKey>, IDisposable` 暴力检索索引
    - 行优先 `List<float>` 向量存储 + `Dictionary<TKey,int>` 主键到行号映射
    - `ReaderWriterLockSlim`（NoRecursion）多读单写并发；写路径包括 `Add` / `AddBatch`（重复主键原子拒绝） / `Remove`（swap-with-last）
    - `Search` 使用 BCL `PriorityQueue<int,float>` 维护 K 受限堆：smaller-better 取 `-score`、larger-better 取 `+score`，确保堆顶为最差候选可被替换；`EnqueueDequeue` 替换；最终反序写出"最佳→最差"
    - `Hamming` 在构造时即抛 `NotSupportedException`（fp32 内核暂不支持）
  - `src/DotVector.Core/Model/MetricExtensions.cs`：`IsLargerBetter()` 扩展（仅 `InnerProduct` / `DotProduct`）
  - `src/DotVector.Core/Api/Collection.cs`：接入 `FlatIndex<TKey>`
    - `Insert` / `InsertBatch`（一次性打包成 `float[]` 调用 `AddBatch`，原子性） / `Delete` / `Search`
    - `Search` 通过 `ArrayPool<(TKey,float)>.Shared` 复用结果缓冲，归还时 `clearArray: true`
  - `src/DotVector.Core/Api/VectorDatabase.cs`：基于 `ConcurrentDictionary<string, IDisposable>`（`StringComparer.Ordinal`）的多集合注册表
    - `CreateCollection<TKey>` / `GetCollection<TKey>`（TKey 不匹配抛 `InvalidOperationException`） / `DropCollection` / `Dispose`
    - 预留 `VectorDatabase(string directoryPath)` 构造（M5 持久化占位）
  - `tests/DotVector.Core.Tests/Index/Flat/FlatIndexTests.cs`：13 个 FlatIndex 单测（构造校验 / 维度校验 / 重复键 / 排序正确性 / 删除 / 批量 / 并发读一致性）
  - `tests/DotVector.Tests/CollectionTests.cs`：8 个 Collection / VectorDatabase 集成测试（注册冲突、TKey 错配、Dispose 释放、并发读 Top-1 一致）
  - `tests/DotVector.Accuracy.Tests/FlatRecallTests.cs`：1000×64 随机数据集 × 4 种距离的 Recall@10 = 1.0 精确性回归

### Changed

- 项目分工调整：`src/DotVector.Core` 升级为完整的嵌入式向量数据库实现（一次"打开"即对应一个数据库目录）；`src/DotVector` 改为服务器壳（M9 进程内托管多个 `VectorDatabase` 实例，每个目录一个实例）
  - 14 个引擎子目录（Api / Buffers / Catalog / Compression / Compute / Exceptions / Format / Index / IO / Model / PageStore / Query / Storage / Wal）从 `src/DotVector` 迁移到 `src/DotVector.Core`
  - `DotVector.Core` 新增 `System.Numerics.Tensors` 引用
  - `DotVector` 仅引用 `DotVector.Core`，新增 `Server/DotVectorServer.cs` 占位类（TODO M9）
  - `DotVector.Data` 仍只引用 `DotVector.Core`，与服务端保持隔离

- PR #2：M1 — 距离函数与 SIMD 内核
  - `src/DotVector/Compute/Distance.cs`：基于 `System.Numerics.Tensors.TensorPrimitives` 与 `System.Numerics.Vector<float>` 实现 SIMD 距离函数
    - `L2Squared` / `L2`：手写 `Vector<float>` 累加器（diff*diff），尾部 scalar 处理
    - `Cosine`：自定义 `DotAndNorms` 同时累加点积与范数平方，零向量返回 1f（避免 NaN），结果 clamp 到 `[0, 2]`
    - `InnerProduct` / `DotProduct`：`TensorPrimitives.Dot`
    - `Hamming(ReadOnlySpan<byte>, ReadOnlySpan<byte>)`：`MemoryMarshal.Cast<byte,ulong>` + `BitOperations.PopCount`，处理尾部字节
    - `Compute(a, b, Metric)`：按 `Metric` 枚举分发
    - 内部 `L2SquaredScalar` / `InnerProductScalar` / `CosineScalar` 参考实现，使用 double 累加器，用于 SIMD 一致性测试
  - `src/DotVector/Compute/FloatDistanceKernel.cs`：实现 `IDistanceKernel<T>` 适配
    - `FloatDistanceKernel`：fp32 SIMD 实现，委派到 `Distance`
    - `GenericFloatDistanceKernel<T>`：基于 `IFloatingPointIeee754<T>` 通用泛型数学的 scalar 实现，支持 `float` / `double`
  - `src/DotVector`：新增对 `DotVector.Core` 的项目引用，并以 `InternalsVisibleTo` 暴露 scalar 参考给测试与基准项目
  - `tests/DotVector.Core.Tests/Compute/DistanceTests.cs`：51 个单元测试
    - 长度不匹配 / 空向量 / 已知值 / 零向量 / 正交 / 反向
    - SIMD vs scalar 高维一致性（dim ∈ {1,7,8,15,16,128,384,1536,4096}，差 &lt; 1e-5）
    - Hamming 全等 / 全异 / 尾字节
    - `Compute` 分发与 `Hamming` 抛 `NotSupportedException`
    - `FloatDistanceKernel` / `GenericFloatDistanceKernel<float|double>` 验证
  - `tests/DotVector.Benchmarks/DistanceBenchmark.cs`：BenchmarkDotNet 基准
    - 维度 128 / 384 / 1536 / 4096
    - L2Squared / Cosine / InnerProduct 的 SIMD vs scalar 对比；Hamming PopCount

- PR #1：初始化工程骨架（M0）
  - `global.json`：固定 SDK `10.0.100`，`rollForward: latestMinor`
  - `Directory.Build.props`：统一 `net10.0`、`Nullable`、`ImplicitUsings`、`TreatWarningsAsErrors`、`IsAotCompatible`
  - `Directory.Packages.props`：集中包版本管理，含 Core / Test / Benchmark 包组
  - `.editorconfig`：统一代码风格规范
  - `LICENSE`：MIT 许可证
  - `DotVector.slnx`：多项目解决方案文件
  - `src/DotVector`：核心库骨架（Api / Buffers / Catalog / Compute / Compression / Format / Index / IO / Model / PageStore / Query / Storage / Wal / Exceptions）
  - `src/DotVector.Core`：抽象与接口骨架（`IIndex` / `IStorage` / `IDistanceKernel<T>`）
  - `src/DotVector.Data`：`Microsoft.Extensions.VectorData` 适配层占位
  - `src/DotVector.Cli`：命令行工具骨架
  - `tests/DotVector.Tests`：集成测试骨架（smoke 测试）
  - `tests/DotVector.Core.Tests`：单元测试骨架（smoke 测试）
  - `tests/DotVector.Accuracy.Tests`：召回率测试骨架（smoke 测试）
  - `tests/DotVector.Benchmarks`：BenchmarkDotNet 基准测试骨架
  - `eng/benchmarks/run-benchmarks`：基准运行脚手架
  - `eng/benchmarks/start-benchmark-env`：基准环境启动脚手架
  - `connectors/c/native/DotVector.Native`：C ABI 连接器骨架
  - `.github/workflows/ci.yml`：CI 工作流（ubuntu / windows / macos）
  - `.github/workflows/publish.yml`：NuGet 发布工作流（骨架）
  - `docker-compose.yml` / `docker-compose.override.yml` / `docker-compose.dcproj`：Docker 占位
  - `AGENTS.md`：AI 协作规范（DotVector 版），含目录持久化约束
  - `ROADMAP.md`：9 个 Milestone 路线图（M0–M9）及预留（M10–M13）
  - `docs/architecture.md`：架构总览（Mermaid 图）
  - `docs/dotnet10-advantages.md`：.NET 10 向量数据库优势详述
  - `docs/algorithms.md`：算法参考清单（HNSW / IVF / PQ / DiskANN 等）
  - `docs/comparison.md`：产品对比表

### Changed

- `README.md`：用中文重写，包含定位、差异化表、.NET 10 优势、快速开始占位

### Decided

- **持久化方案**：采用**单目录**（`.dvec/`）而非单文件。
  原因：Segment 独立 mmap 粒度更细，OS 页面管理更精确；增量写入无需内部页分配器；
  Compaction 只需原子 rename 涉及的 Segment 目录；与 LanceDB、Qdrant、RocksDB 业界实践一致。
  详见 [ROADMAP.md M5](ROADMAP.md#m5) 对比表。

- **客户端/服务端架构分离**：`DotVector.Data`（VectorData 适配层）禁止直接引用 `DotVector`（服务端）。
  二者通过 `DotVector.Core` 中的 `IDotVectorClient` 协议接口通信。
  原因：`DotVector` 可以作为独立进程（M9 gRPC server）运行，客户端 SDK 不应硬依赖服务端实现；
  传输实现（gRPC/进程内）在运行时注入，使 `DotVector.Data` 可在纯客户端场景中单独发布。
  - M9 实现 `GrpcDotVectorClient`（gRPC 传输，位于 `DotVector.Data`）
  - M9 实现 `LocalDotVectorClient`（进程内直连，零序列化，位于 `DotVector`）

---

<!-- 发布时在此处添加版本号标签，例如： -->
<!-- ## [0.1.0] - 2025-XX-XX -->
