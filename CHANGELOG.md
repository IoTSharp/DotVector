# CHANGELOG

本文件遵循 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.0.0/) 格式，版本号遵循 [语义化版本](https://semver.org/lang/zh-CN/)。

---

## [Unreleased]

### Added

- PR #M16.3：新增本地数据库生命周期管理
  - `LocalVectorDatabaseManager`：新增 `CreateDatabase`、`OpenDatabase`、`ListDatabases`、`CloseDatabase`、`DeleteDatabase`，按根目录管理多个命名 `.dvec/` 数据库
  - 每个本地数据库名称对应独立 `.dvec/` 目录与独立 `DotVector.Core.VectorDatabase` 实例；同一管理器禁止重复打开同名数据库，删除前必须先关闭
  - `tests/DotVector.Core.Tests/Persistence/LocalDatabaseLifecycleTests.cs`：覆盖创建、列表、关闭、重新打开、删除、目录隔离和非法名称校验

- PR #M16.2：新增 Code-First 便捷查询 API
  - `DotVectorSet<TEntity>`：新增 `SearchTop1`、`SearchByThreshold`、`Upsert`、`Find` / `Get` 便捷方法，`Upsert` 对重复主键执行覆盖写入，`Find` 未命中返回 `null`，`Get` 未命中抛 `KeyNotFoundException`
  - 多向量字段查询支持 `vectorFieldName` 与 `Expression<Func<TEntity, object?>>` selector 两种选择方式；Attribute schema 会把 CLR 属性名映射到 `[DotVectorVector(Name=...)]` 的存储字段名，显式单向量 schema 支持 selector 兜底
  - `SearchTop1` / `SearchByThreshold` 继续复用现有 `Search(..., Filter?)` 路径；VectorData 侧已有 `SearchAsync` / `GetAsync(filter)` 通过 LINQ Filter 翻译器复用表达式过滤
  - `tests/DotVector.Core.Tests/CodeFirst/CodeFirstTests.cs`：新增 3 个测试覆盖覆盖写入、`Find` / `Get`、阈值过滤、字段名 / selector 多向量查询和显式 schema selector

- PR #M16.1：新增 Code-First 嵌入式体验
  - `src/DotVector.Core/CodeFirst/`：新增 `[DotVectorKey]`、`[DotVectorVector]`、`[DotVectorIndex]` Attribute，支持声明实体主键、多个向量字段、维度、Metric、IndexKind 与 HNSW / IVF / Vamana 索引参数
  - `DotVectorDbContext` / `DotVectorSet<TEntity>`：自动发现上下文集合属性并绑定到 `VectorDatabase`，一个实体的多个向量字段会映射到彼此独立的底层集合
  - `DotVectorEntitySchema<TEntity,TKey>` / `DotVectorEntityAccessors<TEntity,TKey>`：提供显式 schema registration 入口，用户可传入编译期 lambda 作为 AOT 兜底路径；Attribute 路径会预编译访问器，避免插入与搜索热路径反射
  - `tests/DotVector.Core.Tests/CodeFirst/CodeFirstTests.cs`：新增 6 个测试，覆盖 Attribute 自动绑定、多向量字段、显式 schema 注册、缺少 schema、未指定多向量字段和维度不匹配

- 新增 `DotVector.Primitives` / `DotVector.Indexing` 库级 API：提供 lower-is-better `KnnMetric` / `VectorDistance` facade，以及 `IVectorIndexBuilder` / `IVectorIndexReader`、`LocalVectorIndexBuilder`、连续 float32 payload 构建和搜索入口，供 SonnetDB adapter 复用 DotVector 本地引擎而不依赖服务端模式。

### Changed

- M16 路线收口：M16.4-M16.8 不再作为 DotVector 独立产品化任务推进，后续重心转向 SonnetDB 对 DotVector 的库级集成；DotVector 继续提供向量算法、索引、量化和 VectorData 能力。

- 收紧 DotVector V1 定位：后续 SonnetDB 集成只走本地嵌入式 / 库级 API，独立 gRPC Server、Docker 服务端和远程数据库形态不再作为新的产品路线或 SonnetDB 依赖路径。

- `src/DotVector.Data` 客户端 SDK 项目显式设置 NuGet `PackageId=DotVector`，发布产物从 `DotVector.Data` 包名调整为 `DotVector`，程序集名与命名空间保持 `DotVector.Data` 不变。

- CI / Release 发布流程移除 DotVector Docker 镜像构建与推送，只保留 NuGet、CLI Native AOT、C NativeAOT connector、文档站和 release assets。

- `DotVectorClient.Connect(...)`、`DotVectorClientOptions` 与 C ABI `dotvector_database_connect` 仅作为旧 API / ABI 兼容入口保留，调用时明确返回远程服务端模式已删除；CLI 改为通过 `--data` / `DOTVECTOR_DATA` 打开本地 `.dvec/` 目录。

### Removed

- 删除独立 DotVector Server 项目：移除 `src/DotVector`、`docker-compose.yml`、`docker-compose.override.yml`、`docker-compose.dcproj` 和 `tests/DotVector.Tests/GrpcServerIntegrationTests.cs`；服务端模式后续统一由 SonnetDB 承载。

- 删除 DotVector gRPC 客户端与协议生成链路：移除 `src/DotVector.Data/Grpc/GrpcDotVectorClient.cs`、`protos/dotvector.proto`、`Grpc.Net.Client` / `Grpc.Tools` / `Google.Protobuf` 依赖，以及 Python gRPC client / generated proto 文件；Python connector 仅保留 ctypes Native 本地嵌入式路径。

- 新增 `docs/release-news-v1.0.0.md` 发布新闻页，并在文档首页与发布说明中加入入口。

- 文档站视觉设计：新增 JekyllNet `default` layout、静态样式与 DotVector 向量网络首屏视觉资源，首页改为面向向量 AI / 嵌入式数据库的导航入口，同时保持 `JekyllNet/action@v2.5` 可构建。

- GitHub Pages 文档站配置子目录发布：`docs/_config.yml` 设置 `url: https://iotsharp.net` 与 `baseurl: /DotVector`，文档首页和发布说明同步公开地址 `https://iotsharp.net/DotVector/`，首页站内链接改为 `/DotVector/.../`。

- 梳理项目门面与路线图：补齐 `DotVector.Core` / `DotVector` / `DotVector.Data` / `DotVector.VectorData` / C/Python 连接器职责说明，修正架构文档中 CLI/server、Core API 路径、LocalDotVectorClient 位置等旧描述；同步 `ROADMAP.md` M7/M9/M13 当前落地状态，并更新 comparison / algorithms / docs index 的 DiskANN、量化、gRPC 与连接器信息。

- CI 补强：`ci.yml` 在三平台测试后增加 CLI Native AOT publish 验证，Windows/Linux 增加 C NativeAOT connector publish 验证，Ubuntu 增加 NuGet pack、Docker build 与 JekyllNet docs build；测试结果 logger 改为默认 trx 文件名，避免多个测试程序集覆盖同一个 `test-results.trx`。

- `DotVector.Cli` 显式声明 `win-x64` / `linux-x64` / `osx-x64` / `osx-arm64` RuntimeIdentifiers，修正带 `--runtime` 的 Native AOT publish 缺少 RID asset target 的问题。

- 恢复历史 C / Python 连接器与 `DotVector.VectorData` 项目源码；恢复 `DotVector.Data` 高层客户端门面，并补回服务端多数据库 selector/registry 的 gRPC 路由与隔离测试。

- PR #M16.6：M16 — 开发体验规划、文档站、组织级 NuGet 发布与 README 增补
  - `README.md`：在原有项目门面基础上增补 `DotVector.Core` 嵌入式引擎、`DotVector` 服务端宿主、`DotVector.Data` 客户端 / VectorData 适配三层职责，以及文档站和组织级 NuGet 发布说明
  - `ROADMAP.md`：新增 M16 开发体验补强 milestone，覆盖 Code-First、便捷查询 API、服务端 `_system.dvec/`、管理 API / CLI、Vue3 管理台、文档站、多语言快速开始与可选 KDTree
  - `docs/index.md` / `docs/_config.yml` / `.github/workflows/pages.yml`（新增）：以 `docs/` 为 GitHub Pages 文档源，使用 `JekyllNet/action@v2.5` 构建并发布到 GitHub Pages


- PR #M9.2：M9.2 — README 门面重构与项目介绍更新
  - `README.md` / `README.en.md`：标题与小标题统一加入 emoji，移除路线图式展示，改为项目介绍、核心实力、主要优势、NuGet 包与连接器表格、快速开始、发布与仓库内容说明
  - `docs/release.md` 继续作为发布补充说明，保留 NuGet / Docker Hub / GitHub Release 的产物说明

- PR #M9.1：M9.1 — 发布流水线覆盖 Docker Hub、NuGet 与 GitHub Release 资产
  - `.github/workflows/publish.yml`：GitHub Release 发布时自动构建测试，打包并推送 `DotVector.Core` / `DotVector.Data` / `DotVector.Cli` 到 nuget.org；`DotVector` 作为服务端宿主不发布 NuGet 包，仅构建并推送 `iotsharp/dotvector:<version>` Docker 镜像到 Docker Hub，正式版同步更新 `latest` 标签
  - `src/DotVector/Dockerfile`：接收发布流水线传入的版本号并写入 OCI image labels，便于 Docker Hub 展示与追踪
  - Release 资产上传：将 `.nupkg` / `.snupkg` 以及 `dotvector-<version>-connectors-examples.zip` 上传到 GitHub Release；压缩包包含 C native connector 发布产物、示例源码与示例发布产物
  - `examples/csharp/QuickStart`（新增）：提供可运行的内存集合插入与搜索示例，作为 Release 示例源码与发布产物来源
  - `docs/release.md`（新增）：记录所需 `NUGET_API_KEY` / `DOCKERHUB_USERNAME` / `DOCKERHUB_TOKEN` secrets、触发方式与发布产物

- PR #M14.1：M14.1 — `IBatchScorer` 抽象 + 默认 `CpuTensorPrimitivesScorer` + `FlatIndex<TKey>` 注入点
  - `src/DotVector.Core/Compute/IBatchScorer.cs`（新增）：声明 `Score(query, dataset, scores, metric)` 批量打分接口；约定无锁、与 `Distance.Compute` 同语义、热路径零分配；维度由 `dataset.Length / scores.Length` 隐式推导
  - `src/DotVector.Core/Compute/CpuTensorPrimitivesScorer.cs`（新增）：默认 CPU 实现，单例 `Instance`；逐行委托 `Distance.Compute`，与既有路径 bit-identical；显式拒绝 `Metric.Hamming`，并校验 `dataset.Length == scores.Length × query.Length`
  - `src/DotVector.Core/Index/Flat/FlatIndex.cs`：构造函数末尾追加可选参数 `IBatchScorer? scorer = null`（向后兼容）；`Search` 在注入 scorer 时通过 `ArrayPool<float>.Shared` 租借一次性批量打分缓冲，否则保留既有逐行 SIMD 路径，零额外分配；`SearchSubset` 暂保持逐行路径（稀疏行收集后续 PR 处理）
  - `tests/DotVector.Core.Tests/Compute/BatchScorerTests.cs`（新增 6 个测试）：`CpuTensorPrimitivesScorer` 与 `Distance.Compute` 在 L2/Cosine/InnerProduct/DotProduct 下完全相等；Hamming 抛出 `NotSupportedException`；dataset 长度错配抛 `ArgumentException`；空数据集 no-op；`FlatIndex` 注入 scorer 与默认路径返回的 Top-K 键序与分数完全一致
  - 为外部硬件加速包（如 ONNX Runtime / CUDA / DirectML 等，详见 [DotVectorEE](https://github.com/IoTSharp/DotVectorEE) 企业版仓库）预留 `IBatchScorer` 注入点；CE 仓库自身不引入任何加速器运行时依赖

- PR #M13.5b：M13.5b — `quantizer.bin` 接入 `SegmentWriter`/`SegmentReader` + `IvfPqIndex` 复用 `IQuantizedScorer`
  - `src/DotVector.Core/Storage/SegmentWriter.cs`：新增 `Write<TKey>(..., IReadOnlyList<byte[]?>? payloads, IVectorQuantizer? quantizer)` 6 参重载；`quantizer is not null` 时在原子 `Directory.Move` 之前通过 `QuantizerSerializer.Write` 落盘 `quantizer.bin`，并 `FlushToDisk(true)`；为 `null` 时不生成该文件，旧 5 参重载与既有调用点签名保持不变（向后兼容）
  - `src/DotVector.Core/Storage/SegmentReader.cs`：新增 public 属性 `IVectorQuantizer? Quantizer { get; }`；`Open` 在校验 `seg.hdr` / `keys.bin` / `vectors.bin` 后增加可选分支：若 `quantizer.bin` 存在则 `QuantizerSerializer.Read` 反序列化注入构造，不存在则保持 `null`（按文件存在性向后兼容）；空 Segment 与有数据 Segment 路径均传递 `quantizer`
  - `src/DotVector.Core/Index/Ivf/IvfPqIndex.cs`：内部新增 `ProductQuantizer? _pq`，训练完成 PQ 码本后用新 internal `ProductQuantizer(PqCodebook)` 包装零拷贝复用；`SearchCore` 从直接调用 `PqCodebook.PrecomputeAdcLutL2` + `ScoreAdcL2Sq` 切换为 `IVectorQuantizer.BuildScorer(residual)` → `IQuantizedScorer.Score(code)` 抽象路径；写回阶段引入 `Dictionary<int, IQuantizedScorer>` 缓存避免对同一 list 重复构建打分器；`residual` 改用堆分配 `new float[_dimensions]` 以适配大维度
  - `tests/DotVector.Core.Tests/Persistence/SegmentQuantizerRoundTripTests.cs`（新增 2 个测试）：未传 quantizer 时不生成 `quantizer.bin` 且重读 `Quantizer == null`；传入已训练 SQ8 后 `quantizer.bin` 落盘、重读 `Kind/Dimensions/CodeBytes/IsTrained` 一致、`Encode` 字节序列与 `BuildScorer.Score` 在 round-trip 前后完全一致
  - **未升级 `FileHeader.Version`**（覆盖 AGENTS.md 默认规则，由用户决策）：`quantizer.bin` 是纯增量可选 sidecar，老 Segment 无该文件读端走 `Quantizer = null` 兜底；`SegmentHeader` / `vectors.bin` / `keys.bin` / `payload.bin` 二进制布局零变更，前后双向兼容
  - 全量回归：341 个测试通过（baseline 339 + 新增 2）

- PR #M13.5a：M13.5a — `IVectorQuantizer.BuildScorer` 全量化器统一 + `QuantizerSerializer` 持久化 + `QuantizedFlatIndex<TKey>` 量化线性扫描索引
  - `src/DotVector.Core/Compression/IVectorQuantizer.cs`：在接口层声明 `BuildScorer(ReadOnlySpan<float> query) → IQuantizedScorer`，统一 SQ8/PQ/OPQ/RQ 四种量化器的查询打分入口
  - `src/DotVector.Core/Compression/ScalarQuantizer8.cs`：新增 internal `Sq8DecompressScorer`（重建 + L2² via `TensorPrimitives.SumOfSquares`），并新增 internal `Min/Scale` 只读视图与 `LoadState(min, scale)` 重算 `_invScale`，供反序列化无暴露公共 setter 重建
  - `src/DotVector.Core/Compression/PqCodebook.cs`：新增 internal `LoadCentroids(ReadOnlySpan<float>)` 校验长度并复制
  - `src/DotVector.Core/Compression/ProductQuantizer.cs` / `OptimizedProductQuantizer.cs` / `ResidualQuantizer.cs`：新增 internal `LoadState(...)` 重建训练后状态
  - `src/DotVector.Core/Compression/QuantizerSerializer.cs`（新增）：自描述二进制持久化（首字节 `QuantizerKind`），SQ8 = `i32 dim + f32[dim] min + f32[dim] scale`；PQ = `i32 dim/m/ksub + f32[m·ksub·subDim]`；OPQ = PQ 头 + `f32[d²] R + f32 centroids`；RQ = `i32 dim/levels/ksub + f32[levels·ksub·dim]`；little-endian + `MemoryMarshal.AsBytes<float>` 直拷
  - `src/DotVector.Core/Index/Flat/QuantizedFlatIndex.cs`（新增）：实现 `IIndex<TKey>`，使用已训练 `IVectorQuantizer` 在 Add 时编码为 `byte[]`，Search 时通过 `BuildScorer` 单次构建打分器后线性扫描；`ReaderWriterLockSlim` 多读单写、`PriorityQueue<int,float>(priority=-score)` 维护 Top-K，AddBatch 在锁外编码、`Snapshot(out keys, out codes)` 用于 Segment 落盘；当前仅支持 `Metric.L2`（其他度量需量化感知打分器，后续 PR 扩展）
  - `tests/DotVector.Core.Tests/Compression/QuantizerSerializerTests.cs`（新增 6 个测试）：null / 未训练抛出 / SQ8|PQ|OPQ|RQ 各自 Write→Read 后 Encode 字节相同
  - `tests/DotVector.Core.Tests/Index/Flat/QuantizedFlatIndexTests.cs`（新增 9 个测试）：未训练量化器 / 非 L2 / 重复键 / 维度错配 / 空索引搜索 / Remove / SQ8 vs raw FlatIndex Top-10 重合率 ≥ 0.8 / 自查询 top1 命中 ≥ 45/50 / Snapshot 形状
  - 全量回归：339 个测试通过（baseline 324 + 新增 15）
  - 后续 PR：M13.5b（`SegmentWriter`/`Reader` 接入 `quantizer.bin` + IvfPq 复用 `IQuantizedScorer` + `FileHeader.Version` 升级）

- PR #M13.4：M13.4 — `ResidualQuantizer`（RQ）多级残差码本量化器 + 召回率回归
  - `src/DotVector.Core/Compression/ResidualQuantizer.cs`（新增）：实现 `IVectorQuantizer`（`Kind=Rq`，`CodeBytes=Levels`，每级 `Ksub=256`）；`Train` 逐级 K-Means（`KMeans.Train`，per-level `seed = baseSeed + level`），训练完一级后用 `TensorPrimitives.Subtract` 在 ArrayPool 残差缓冲上原位扣除，得到下一级训练数据；`Encode` 逐级 `KMeans.FindNearest` + 残差更新，`stackalloc 1024` 阈值之下走栈；`Decode` 累加各级被选中心；`BuildScorer` 返回内部 `RqDecompressScorer`，按 FAISS ST_decompress 风格直接重建向量后用 `TensorPrimitives.SumOfSquares` 计算 L2²，避免 `M*(M-1)/2 * K²` 交叉项 LUT 内存
  - `tests/DotVector.Core.Tests/Compression/ResidualQuantizerTests.cs`（新增 10 个测试）：构造参数校验 / Encode|Decode|BuildScorer 未训练抛 `InvalidOperationException` / Train 数据不足 K=256 抛 `ArgumentOutOfRangeException` / `Kind=Rq` / Encode→Decode round-trip 形状与有限性 / **更高级数严格降低 MSE**（levels 2 → 4 → 8）/ **ADC 与解码后 L2² 一致性**（容差 `max(1e-2, refScore × 1e-4)`）/ **合成高斯簇 4 级 Recall@10 ≥ 0.80**
  - 全量回归：324 个测试通过（baseline 314 + RQ 10）
  - 后续 PR：M13.5（`quantizer.bin` 持久化 + IvfPq/Flat 索引集成 + `FileHeader.Version` 升级）

- PR #M13.3：M13.3 — `OptimizedProductQuantizer`（OPQ）+ 纯托管 one-sided Jacobi SVD
  - `src/DotVector.Core/Compression/JacobiSvd.cs`（新增）：`internal static` 工具类；`Decompose` 实现一边 Jacobi SVD（双精度内部累加，输出 float），`SolveOrthogonalProcrustes` 解 R = V·U^T 最大化 tr(R·A)；不依赖任何第三方数值库
  - `src/DotVector.Core/Compression/OptimizedProductQuantizer.cs`（新增）：实现 `IVectorQuantizer`（`Kind=Opq`，`CodeBytes=M`）；持有 D×D 旋转矩阵 R，`Train` 迭代：固定 R 训练 PQ → 编码再解码得到 ŷ → 累加 cross-covariance A = X^T·Ŷ → Procrustes 求新 R；最终再做一次 PQ 训练以保持一致；`Encode/Decode/BuildScorer` 在 R·x 域上委托内部 PQ；`ApplyRotation/ApplyTransposeRotation` 走纯托管 GEMV，`stackalloc` 阈值 1024 floats
  - `tests/DotVector.Core.Tests/Compression/JacobiSvdTests.cs`（新增 5 个测试）：单位矩阵 / 对角矩阵奇异值恢复 / 随机矩阵重构 + U V 正交性 / Procrustes 输出正交 / Y=Q·X 设定下恢复出 R≈Q
  - `tests/DotVector.Core.Tests/Compression/OptimizedProductQuantizerTests.cs`（新增 8 个测试）：dim%m / opqIterations 校验 / 未训练时 Encode|Decode|BuildScorer 抛 `InvalidOperationException` / `Kind=Opq` / Train 后 R^T·R≈I / Encode→Decode 形状与有限性 / **ADC 与解码后 L2² 一致性** / Train 重构误差 ≤ 基线 PQ × 1.05
  - 全量回归：314 个测试通过（baseline 299 + JacobiSvd 5 + OPQ 8 = 312；其余因 OPQ 修订少量重构 +2）
  - 后续 PR：M13.4（RQ — 多级残差码本）、M13.5（`quantizer.bin` 持久化与索引集成）

- PR #M13.2：M13.2 — `ProductQuantizer` 实现 `IVectorQuantizer` + `IQuantizedScorer` ADC 打分抽象
  - `src/DotVector.Core/Compression/IQuantizedScorer.cs`（新增）：量化打分内核统一接口，遵循 L2 平方距离语义；`Score(ReadOnlySpan<byte>)` 单条编码评分，由 `IVectorQuantizer` 的 `BuildScorer(query)` 创建（持有预计算 LUT，按查询独立实例）
  - `src/DotVector.Core/Compression/ProductQuantizer.cs`（新增）：包装现有 `PqCodebook`，实现 `IVectorQuantizer` 契约（`Kind=Pq`，`CodeBytes=M`），暴露 `Train` / `Encode` / `Decode` / `BuildScorer`；`Decode` 重建为各子空间被选中心的拼接；内部 `PqAdcScorer` 持有 `M × 256` LUT，4 路展开累加，零额外分配；构造参数支持 `maxIterations` 与 `seed`，便于确定性测试
  - `tests/DotVector.Core.Tests/Compression/ProductQuantizerTests.cs`（新增 11 个测试）：dim 不能整除 m / Encode|Decode|BuildScorer 未训练抛 `InvalidOperationException` / Train 后 CodeBytes 与 IsTrained 正确 / Decode 拼接重建 / **ADC 与标量参考一致性 |Δ| < 1e-4** / Score 缓冲过小 / Encode|Decode 维度不匹配 / Kind=Pq
  - 兼容性：`PqCodebook` 与 `IvfPqIndex` 维持原状未改动；M5 持久化将在 M13.5 引入 `quantizer.bin`
  - 全量回归：299 个测试通过（baseline 288 + PQ 新增 11）
  - 后续 PR：M13.3（OPQ — 旋转 + PQ 联合训练）

- PR #M13.1：M13.1 — `IVectorQuantizer` 通用量化抽象 + `ScalarQuantizer8`（SQ8）实现
  - `src/DotVector.Core/Compression/QuantizerKind.cs`（新增）：枚举 `None=0 / Sq8=1 / Pq=2 / Opq=3 / Rq=4`，固定数值用于 `quantizer.bin` 持久化首字节
  - `src/DotVector.Core/Compression/IVectorQuantizer.cs`（新增）：统一接口，含 `Kind` / `Dimensions` / `CodeBytes` / `IsTrained` / `Train` / `Encode` / `Decode`，约束训练后 Encode/Decode 线程安全
  - `src/DotVector.Core/Compression/ScalarQuantizer8.cs`（新增）：逐维 min/max → uint8 量化器，训练阶段使用 `TensorPrimitives.Min/Max` 逐行更新；Encode 走 `MathF.Round` + clamp；Decode 反量化为 `min + code * scale`；零方差维度自动短路（scale=0）；Encode/Decode 零额外分配；提供 internal `Min/Scale` 调试只读视图
  - `tests/DotVector.Core.Tests/Compression/ScalarQuantizer8Tests.cs`（新增 9 个测试）：构造参数校验 / 未训练抛出 / Train 长度校验 / 学习的 min 与重算结果一致 / 256 行 32 维随机数据 round-trip 误差 ≤ step/2 / 编码端到端覆盖 0 与 255 / 中点落在 [126, 129] / 零方差维度优雅处理 / 缓冲过小与维度不匹配抛 `ArgumentException`
  - 全量回归：288 个测试通过（baseline 279 + SQ8 新增 9）
  - 后续 PR：M13.2（PqCodebook 升级为 ProductQuantizer 并实现 ADC 距离内核）

- PR #M12.4：M12.4 — Vamana 索引与 M11 ScalarIndex pre-filter 集成
  - `src/DotVector.Core/Index/DiskAnn/VamanaIndex.cs`：新增 `SearchSubset(query, topK, candidateKeys, results)`，在 ScalarIndex 解析出的候选键集合上做精确扫描（mirrors `FlatIndex<TKey>.SearchSubset`），保证子集上 100% 召回；候选集为空 / 全部命中 tombstone 时返回 0；`Hamming` 度量隐式继承 `Search` 的拒绝行为；XML 注释中说明 DiskANN-Filter 风格的 FilteredBeamSearch 留待后续 milestone
  - `src/DotVector.Core/Api/Collection.cs`：`Search` 的过滤下推路径同时支持 `FlatIndex<TKey>` 与 `VamanaIndex<TKey>`；当 `ScalarIndex.TryResolveCandidates(filter, ...)` 返回完整候选集时，统一通过 `ArrayPool` 缓冲走 `SearchSubset` 路径，再用 `Filter.Matches` 做兜底校验；候选集为空时短路返回
  - `tests/DotVector.Core.Tests/Index/DiskAnn/VamanaSubsetSearchTests.cs`（新增 5 个测试）：空候选集 / 未知键被忽略 / tombstone 行被跳过 / 在候选集上 ground-truth Top-K 完全命中（L2 / Cosine / InnerProduct）/ 缓冲区过小抛 `ArgumentException`
  - `tests/DotVector.Core.Tests/Query/VamanaFilterIntegrationTests.cs`（新增 4 个测试）：`Collection.Search` 在 Vamana 集合上分别走 `Filter.Eq` / 空候选集 / `Filter.And(Eq, Range)` 路径；与 Flat 集合在同一 `Filter.Eq("tag", "A")` + 同一查询下结果完全一致
  - 全量回归：279 个测试通过（Core 211 + Tests 47 + Accuracy 21）

- PR #M12.3：M12.3 — Vamana / DiskANN mmap 磁盘持久化
  - `src/DotVector.Core/Index/DiskAnn/VamanaGraphIO.cs`（新增）：纯 safe 的 `vamana.bin` 读写器
    - `Write(path, dimensions, metric, options, entryPoint, neighbors, tombstones)`：原子 tmp + `File.Move`，固定大小节点条目（`8 + 4*MaxDegree`），空槽位用 `EmptySlot=0xFFFFFFFF` 填充
    - `Read(path, out header, out neighbors, out tombstones)`：`MemoryMappedFile` + `MemoryMappedViewAccessor.Read<T>` / `ReadArray<T>` 安全读取，校验 magic / version / 文件长度 / NodeId 单调 / NeighborCount 上限 / 槽位有效范围
    - `EmptySlot = 0xFFFFFFFFu`、`NoEntryPoint = 0xFFFFFFFFu` 常量
  - `src/DotVector.Core/Index/DiskAnn/VamanaIndex.cs`：新增 `Snapshot(...)`（读锁深拷贝）与 `RestoreBulk(...)`（写锁批量装载，校验维度/键唯一/EntryPoint，移除 tombstone 行的键映射）
  - `src/DotVector.Core/Storage/PersistentDirectory.cs`：新增 `FlushVamanaCollection<TKey>(...)` 与 `TryGetLatestSegmentDir(...)`，Vamana 采用单 segment 全量快照模型（每次 Flush 写入新 `seg-{seq}` 并删除所有旧 segment 目录），WAL 同步 rotate + LastCoveredWalSequence 推进 + `TryTrimWal()`
  - `src/DotVector.Core/Api/Collection.cs`：`Flush()` 新增 `VamanaIndex<TKey>` 分支；新增 internal `RestoreVamanaSnapshot(...)` 把磁盘快照交给 `VamanaIndex.RestoreBulk`
  - `src/DotVector.Core/Api/VectorDatabase.cs`：新增 `LoadVamanaSegmentInto<TKey>(...)`，`OpenCollection` 路径在 `IndexKind.Vamana` 时优先加载最新 segment 的 `vamana.bin` 再回放 WAL（`LastCoveredWalSequence` 之后的记录）
  - `tests/DotVector.Core.Tests/Persistence/VamanaPersistenceTests.cs`（新增 5 个测试）：
    - Flush + Reopen 后向量计数保持
    - Flush + Reopen 后同一 query 的 top-K 结果（key 序列 + score）完全一致
    - `vamana.bin` 文件含正确的 "DVAN" magic
    - 二次 Flush 后只剩单个 `seg-*` 目录（验证旧 segment 被清理）
    - Flush 后增量 Insert 落入 WAL，重开时能被回放
  - 全量回归：267 个测试通过（Core 199 + Tests 47 + Accuracy 21）

- PR #M12.1：M12.1 — Vamana / DiskANN 索引格式头与单元测试
  - `src/DotVector.Core/Model/IndexKind.cs`：新增 `Vamana = 4` 枚举值
  - `src/DotVector.Core/Format/VamanaNodeHeader.cs`（新增）：8 字节 `[StructLayout(Sequential, Pack=1)]` 节点头（`NodeId` + `NeighborCount` + `Tombstone` + `Reserved0`），后跟 `uint[R] neighbors` 邻居数组与可选的 `float[D]` 内联向量
  - `src/DotVector.Core/Format/VamanaFileHeader.cs`（新增）：48 字节文件头（`Magic8` "DVAN\0\0\0\0" + `Version=1` + `MaxDegree` + `Alpha` + `EntryPointId` + `NodeCount` + `Dimensions` + `MetricKind` + `InlineVectors` + 14 字节保留区，`[InlineArray(14)] Reserved14`）；`VamanaFileHeaderConstants.MagicAscii` 提供 4 字节 ASCII "DVAN"
  - `tests/DotVector.Core.Tests/Format/VamanaNodeHeaderTests.cs`（新增）：`SizeOf` + round-trip
  - `tests/DotVector.Core.Tests/Format/VamanaFileHeaderTests.cs`（新增）：`SizeOf` + round-trip + Magic ASCII 校验

- PR #M12.2：M12.2 — 内存版 VamanaIndex（RobustPrune + BeamSearch）
  - `src/DotVector.Core/Index/DiskAnn/VamanaOptions.cs`（新增）：`MaxDegree=32`、`SearchListSize=75`、`Alpha=1.2`、`BeamWidth=4`、可选 `Seed`，并提供 `Default` 与 `Validate()`
  - `src/DotVector.Core/Index/DiskAnn/VamanaIndex.cs`（新增）：单层 Vamana 图索引，`IIndex<TKey>` 实现，纯 safe 代码（`List<float>` + `CollectionsMarshal.AsSpan` + `PriorityQueue`）；`Add` 走 BeamSearch 收集候选 → `RobustPrune(focal, V, alpha, R)` → 双向边并对邻居超出 `R` 时再次 RobustPrune；`Remove` 采用 tombstone-only；`Search` 用 `L=max(SearchListSize, topK)` 的 BeamSearch；`Hamming` 度量显式拒绝
  - `src/DotVector.Core/Api/Collection.cs` / `Api/VectorDatabase.cs`：新增 `CreateCollection<TKey>(name, dimensions, metric, VamanaOptions options)` 重载，`IndexKind.Vamana` 自动构造 `VamanaIndex<TKey>`
  - `tests/DotVector.Core.Tests/Index/DiskAnn/VamanaIndexTests.cs`（新增 11 个测试）：构造校验、维度/重复键校验、空索引、精确命中、四种度量结果序列、`Remove` tombstone、并发只读
  - `tests/DotVector.Accuracy.Tests/VamanaRecallTests.cs`（新增 4 个 Theory 用例）：在 1000×64 维随机数据集上对 L2 / Cosine / DotProduct / InnerProduct 验证 Recall@10 ≥ 0.92
  - 全量回归：262 个测试通过（含 19 个 Vamana 相关用例）

- PR #M9：M9 — gRPC 服务端 / 客户端 / CLI 远程命令 / Native AOT 发布 / Docker 化
  - `src/DotVector/Server/DotVectorServer.cs`：`Build(dataDirectory, port, args, loopbackOnly, httpsCertificate)` 构建 Kestrel HTTP/2 宿主，端到端封装 `VectorDatabase` + `VectorServiceImpl` + `LocalDotVectorClient`；默认 h2c，可传 `X509Certificate2` 启用 HTTPS（h2 over TLS，ALPN）。
  - `src/DotVector/Grpc/VectorServiceImpl.cs` + `protos/dotvector.proto`：定义并实现 `VectorService`（Ping/CreateCollection/DeleteCollection/ListCollections/Upsert/Delete/Search/Get/Scroll），与 `IDotVectorClient` 一一对应。
  - `src/DotVector.Data/Grpc/GrpcDotVectorClient.cs`：`IDotVectorClient` 的 gRPC 实现，默认 `SocketsHttpHandler` 关闭代理（`UseProxy=false`、`Proxy=null`）以避免本机 loopback 被 HTTP_PROXY/PAC 劫持；`ModuleInitializer` 提前开启 `Http2UnencryptedSupport` 开关支持 h2c prior-knowledge。
  - `src/DotVector.Data/LocalDotVectorClient.cs`：进程内 `IDotVectorClient` 适配器，复用同一份契约让 CLI / 服务端共享代码路径。
  - `src/DotVector.Cli/Program.cs`：新增远程命令（`ping`、`collections list/create/delete`），通过 `--endpoint` 选择 gRPC 通道或本地 `.dvec` 目录。
  - `src/DotVector.Cli/DotVector.Cli.csproj`：开启 `PublishAot=true` + 单文件配置，`dotnet publish -c Release -r win-x64` 通过（0 trim/AOT 警告）。
  - `Dockerfile` + `docker-compose.yml` + `docker-compose.override.yml` + `docker-compose.dcproj`：服务端容器化（端口 5180/h2c），并提供本地 compose 文件挂载持久化数据卷。
  - `tests/DotVector.Tests/GrpcServerIntegrationTests.cs`：M9 端到端集成测试，进程内自签证书走 HTTPS+ALPN h2，覆盖 Ping → CreateCollection → Upsert → Search → Delete → ListCollections 全链路。

### Added

- PR #M7.3：M7.3 — Dynamic Collection / `ListCollectionNames` / `VectorStoreCollectionDefinition`
  - `src/DotVector.Core/Protocol/ProtocolDtos.cs`：新增 `CollectionInfo`（Name + Dimensions + Metric + RecordCount）DTO
  - `src/DotVector.Core/IDotVectorClient.cs`：新增 `ListCollectionsAsync(CancellationToken)` 返回 `IReadOnlyList<CollectionInfo>`
  - `src/DotVector.Data/DotVectorVectorStore.cs`：
    - 实现 `ListCollectionNamesAsync` —— 通过 `IDotVectorClient.ListCollectionsAsync` 枚举集合名
    - 实现 `CollectionExistsAsync(name)` —— 改为查询 `ListCollectionsAsync` 的真实结果
    - `GetCollection<TKey,TRecord>(name, definition)` 现在透传 `definition` 给 `DotVectorCollection<,>`
    - `GetDynamicCollection(name, definition)` 返回新的 `DotVectorDynamicCollection`
  - `src/DotVector.Data/Internal/DotVectorRecordMapper.cs`：新增基于 `VectorStoreCollectionDefinition` 的构造函数（不依赖 attribute 反射），并把 payload 字段名映射收敛到 `_dataStorageNames` 字典
  - `src/DotVector.Data/DotVectorCollection.cs`：
    - 新增三参构造 `(client, name, VectorStoreCollectionDefinition?)`，按定义构造映射器；原两参构造委托至此
    - `CollectionExistsAsync` 改为查询 `ListCollectionsAsync`
  - `src/DotVector.Data/Internal/DotVectorDynamicCollection.cs`（新增）：`VectorStoreCollection<object, Dictionary<string,object?>>` 实现，按定义中的属性名访问字段，支持 Upsert/Delete/Get/Search；LINQ Filter 翻译显式拒绝（动态字典缺少强类型语义，TODO M7+）
  - `tests/DotVector.Tests/InMemoryDotVectorClient.cs`：新增 `ListCollectionsAsync` 实现
  - `tests/DotVector.Tests/M7_3_DynamicAndListTests.cs`（新增 8 个测试）：覆盖 `ListCollectionNamesAsync` 全量返回、`CollectionExistsAsync` 创建前后真值变化、动态集合 Upsert/Search/Get/Delete 端到端、缺失键字段抛 `InvalidOperationException`、显式 `VectorStoreCollectionDefinition` 与基于 attribute 的反射映射在搜索结果上等价（参数化 Theory）
  - `tests/DotVector.Tests/SmokeTests.cs::DotVectorData_AssemblyDoesNotReferenceServerShell` 仍通过 —— 客户端/服务端隔离不变
  - 全部测试通过（DotVector.Tests 46 通过 0 失败）

### Changed

- M6 — 标量过滤（Payload Filter）正式标记为 ✅。M6 范围内的 payload 字段、Filter AST、`Collection<TKey>.Search(query, topK, Filter?)` post-filter 重载、`GetPayload(key)` 已交付；B-tree 索引与 payload 持久化已在 M11 完成；"100 万条带过滤搜索 < 100 ms" 验收项显式延期至 M8 BenchmarkDotNet 基准体系。
- ROADMAP — 把 M7 "已知局限（标 TODO M7+）" 拆分为三个独立子里程碑：**M7.1**（GetAsync(key/keys) + IncludeVectors）、**M7.2**（LINQ Filter Expression 翻译 + GetAsync(filter)）、**M7.3**（Dynamic Collection / ListCollectionNames / VectorStoreCollectionDefinition）。三者依赖独立，可分别 PR。

### Added

- PR #M7.2：M7.2 — LINQ Filter Expression 翻译 + `GetAsync(filter, top)` 闭环
  - `src/DotVector.Core/Protocol/ProtocolDtos.cs`：
    - `VectorSearchRequest.Filter` 由占位 `string?` 升级为强类型 `DotVector.Query.Filter?`
    - 新增 `VectorScrollRequest`（`Top` + `Filter` + `IncludeVector`），用于无向量的过滤扫描
  - `src/DotVector.Core/IDotVectorClient.cs`：新增 `ScrollAsync(string collectionName, VectorScrollRequest, CancellationToken)`
  - `src/DotVector.Data/Internal/LinqFilterTranslator.cs`（新增）：把 `Expression<Func<TRecord,bool>>` 翻译成 `DotVector.Query.Filter`
    - 支持 `==` / `!=` / `<` / `<=` / `>` / `>=`、`&&` / `||` / `!`、`bool` 成员、`== null` → `Missing`、`!= null` → `Exists`、闭包/捕获常量编译求值
    - 拒绝对主键 / 向量字段过滤；非 `[VectorStoreData]` 属性显式抛 `NotSupportedException`
  - `src/DotVector.Data/Internal/DotVectorRecordMapper.cs`：暴露 `KeyPropertyName` / `VectorPropertyName` / `TryGetPayloadFieldName`，供翻译器解析存储字段名（含 `VectorStoreDataAttribute.StorageName`）
  - `src/DotVector.Data/DotVectorCollection.cs`：
    - `SearchAsync` 把 `VectorSearchOptions<TRecord>.Filter` 翻译后通过 `VectorSearchRequest.Filter` 透传
    - 实现 `GetAsync(Expression<Func<TRecord,bool>> filter, int top, FilteredRecordRetrievalOptions<TRecord>?, CancellationToken)`：调用 `IDotVectorClient.ScrollAsync`，按 `IncludeVectors` 透传向量
  - `tests/DotVector.Tests/InMemoryDotVectorClient.cs`：`SearchAsync` 应用 `Filter.Matches`；新增 `ScrollAsync` 实现
  - `tests/DotVector.Tests/LinqFilterTests.cs`（新增 10 个测试）：覆盖等值 / 范围 / `&&`/`||`/`!` 组合 / 捕获常量 / `bool` 成员 / `GetAsync(filter)` Top 限制 / `IncludeVectors` 透传 / 主键过滤拒绝
  - 全部测试通过（DotVector.Tests 38 通过 0 失败）

### Added


  - `src/DotVector.Core/Protocol/ProtocolDtos.cs`：
    - `VectorSearchRequest` 新增 `bool IncludeVector { get; init; }`
    - `VectorSearchResult` 新增 `float[]? Vector { get; init; }`
    - 新增 `VectorRecordDto`（`Id` + `Vector?` + `Payload`）
  - `src/DotVector.Core/IDotVectorClient.cs`：新增 `GetAsync(string collectionName, IReadOnlyList<string> ids, bool includeVector, CancellationToken)`
  - `src/DotVector.Core/Index/Flat/FlatIndex.cs`：新增 `TryCopyVectorTo(TKey, Span<float>)`，读锁保护下零拷贝复制行向量
  - `src/DotVector.Core/Api/Collection.cs`：
    - 新增 `TryGet(TKey, out VectorRecord<TKey>?)` 与 `GetMany(ReadOnlySpan<TKey>, bool)`
    - `BuildRecord` 内部辅助：从 `_payloads` 物化 `Dictionary<string,object>`，跳过 null 值
  - `src/DotVector.Data/Internal/DotVectorRecordMapper.cs`：`CreateRecord` 增加 `float[]? vector` 重载，将向量回写 `[VectorStoreVector]` 属性（兼容 `float[]` 与 `ReadOnlyMemory<float>` 两种形态）
  - `src/DotVector.Data/DotVectorCollection.cs`：
    - `GetAsync(TKey)` / `GetAsync(IEnumerable<TKey>)` 通过 `IDotVectorClient.GetAsync` 实现，按 `RecordRetrievalOptions.IncludeVectors` 透传
    - `SearchAsync` 透传 `VectorSearchOptions.IncludeVectors` 至 protocol，并把 `VectorSearchResult.Vector` 写回 `TRecord`
    - 移除 M7 `IncludeVectors=true` / `GetAsync` 的 `NotSupportedException`
  - `tests/DotVector.Core.Tests/Persistence/CollectionTryGetTests.cs`：新增 4 个测试覆盖 TryGet 命中/未命中、Flush+Compact+Reopen round-trip、`GetMany`
  - `tests/DotVector.Tests/DotVectorVectorStoreTests.cs`：删除 M7 限制断言，新增 6 个正向测试覆盖 `GetAsync(key)` / `GetAsync(keys)` / `IncludeVectors` 行为
  - `tests/DotVector.Tests/InMemoryDotVectorClient.cs`：实现 `GetAsync` 与 `SearchAsync` 的 `IncludeVector` 路径
  - 全部 220 个测试通过

### Added

- PR #M7：M7 — `Microsoft.Extensions.VectorData.Abstractions` 适配层
  - `src/DotVector.Data/DotVectorVectorStore.cs`：继承 `VectorStore`，封装 `IDotVectorClient`
    - 重写 `GetCollection<TKey, TRecord>` / `EnsureCollectionDeletedAsync` / `CollectionExistsAsync` / `GetService`
    - `GetDynamicCollection` / `ListCollectionNamesAsync` 暂抛 `NotSupportedException`（TODO M7+）
  - `src/DotVector.Data/DotVectorCollection.cs`：继承 `VectorStoreCollection<TKey, TRecord>`
    - 实现 `EnsureCollectionExistsAsync`（依据 `[VectorStoreVector]` 维度 + 距离函数自动 `CreateCollectionAsync`）
    - 实现 `UpsertAsync` 单条/批量、`DeleteAsync` 单条/批量
    - 实现 `SearchAsync<TInput>`：支持 `float[]` / `ReadOnlyMemory<float>` / `Memory<float>` / `IEnumerable<float>` 查询；`IncludeVectors=true` 与非空 `Filter` 暂抛 `NotSupportedException`
    - `GetAsync`（按 key / 按 keys / 按 Expression filter）暂抛 `NotSupportedException`（TODO M7+）
  - `src/DotVector.Data/Internal/DotVectorRecordMapper.cs`：基于反射映射 `TRecord` 与 `VectorUpsertRecord`/`VectorSearchResult`
    - 解析 `[VectorStoreKey]` / `[VectorStoreVector]` / `[VectorStoreData]` 特性
    - 标注 `[RequiresUnreferencedCode]` + `[RequiresDynamicCode]`，AOT 友好向后兼容
  - `src/DotVector.Data/Internal/KeyConverter.cs`：支持 `string` / `int` / `long` / `Guid` 键类型 round-trip
  - `src/DotVector.Data/Internal/DistanceFunctionMapper.cs`：`DistanceFunction.*` 字符串常量映射到 DotVector metric（Cosine / L2 / DotProduct / InnerProduct / Hamming）
  - `src/DotVector.Data/DependencyInjection/DotVectorServiceCollectionExtensions.cs`：`AddDotVectorVectorStore(...)` 注册到 `IServiceCollection`
  - `Directory.Packages.props`：新增 `Microsoft.Extensions.DependencyInjection.Abstractions` 9.0.0
  - `src/DotVector.Data/DotVector.Data.csproj`：引入 `Microsoft.Extensions.VectorData.Abstractions` + DI Abstractions；`InternalsVisibleTo` 暴露给 `DotVector.Tests`
  - 架构约束：`DotVector.Data` 仅依赖 `DotVector.Core`，**不**直接引用 `DotVector`（服务端壳），新增程序集引用断言测试
  - `tests/DotVector.Tests/`：新增 9 个 M7 集成测试 + `InMemoryDotVectorClient` brute-force cosine 模拟实现
  - 全部 212 个测试通过

- PR #M11：M11 — Payload 持久化 + 标量 B-tree pre-filter 索引（M6 延续）
  - **Payload 持久化**
    - `src/DotVector.Core/Storage/PayloadCodec.cs`：TLV 编解码器（key=UTF-8 + 类型 tag：null/bool/long/double/string/bytes），AOT 友好、纯 BCL
    - `src/DotVector.Core/Wal/WalWriter.cs`：新增 type=3 `SetPayload` 记录类型；`WalReader` 同步支持 replay 恢复
    - `src/DotVector.Core/Storage/SegmentWriter.cs` / `SegmentReader.cs`：Segment 写入新增可选 `payload.bin`，Flush 时把当前 `_payloads` 快照编码持久化；mmap 读取后 `RestorePayload` 写入运行时 dict
    - `src/DotVector.Core/Api/Collection.cs::SetPayload` 写 WAL → 重启后通过 WAL replay + Segment payload.bin 完整恢复
    - `tests/DotVector.Core.Tests/Persistence/PayloadPersistenceTests.cs`：4 个测试（WAL 恢复 / Flush+Segment 恢复 / 清空 payload 持久化 / Compaction 合并 payload）
  - **标量 B-tree pre-filter 索引**
    - `src/DotVector.Core/Storage/ScalarIndex.cs`：进程内倒排索引；数值字段用 `SortedDictionary<double, HashSet<TKey>>` 支持 Eq+Range，字符串/布尔用 hash 桶；单 monitor lock 保证并发一致性；纯 BCL，零依赖
    - `src/DotVector.Core/Query/FilterIntrospection.cs` + `FilterIntrospectionAccessor.cs`：reflection-free 桥接，把私有 sealed Filter 节点映射到内部 record view（`EqualsView` / `RangeView` / `AndView`），AOT 友好
    - `src/DotVector.Core/Query/Filter.cs`：基类新增 `internal virtual object? GetIntrospection()`；`FieldEqualsFilter` / `FieldRangeFilter` / `AndFilter` 提供 view 实现
    - `src/DotVector.Core/Index/Flat/FlatIndex.cs`：新增 `SearchSubset(query, topK, candidateKeys, results)` —— 仅扫描候选行的 brute-force 搜索，复用现有 PriorityQueue topK 堆
    - `src/DotVector.Core/Api/Collection.cs`：
      - `_scalarIndex` 在 `SetPayload` / `StorePayload` / `RestorePayload` / `Delete` 路径上同步维护（基于 old/new payload 差量更新）
      - `Search(query, topK, filter)`：当 filter 可被 `ScalarIndex.TryResolveCandidates` 下推且底层为 `FlatIndex<TKey>` 时，走 `SearchSubset` 直接在候选键上搜索；不可下推或非 Flat 时回退到原有 8× over-fetch + post-filter 路径
      - 双保险：pre-filter 命中后仍调用 `filter.Matches(payload)` 做最终判定，对未下推的子条件保持正确性
    - `tests/DotVector.Core.Tests/Index/ScalarIndexTests.cs`：9 个单元测试（string/long/bool Eq、numeric Range inclusive/half-open、And 交集、Or 不下推回 false、Update 旧值清桶、Remove 清键）
    - `tests/DotVector.Core.Tests/Query/FilterPreFilterIntegrationTests.cs`：5 个端到端测试（Eq / Range / And / Or 回退 / Delete 后索引同步）
  - 全部 202 个测试通过（DotVector.Core.Tests 171 + DotVector.Accuracy.Tests 17 + DotVector.Tests 14）

- PR #M10：M10 — Segment Flush + mmap 零拷贝读路径 + Compaction（M5 延续）
  - `src/DotVector.Core/Catalog/CollectionManifest.cs`：每集合 manifest（`NextSegmentSequence` / `LastCoveredWalSequence`），原子写入 `manifest.bin`
  - `src/DotVector.Core/Storage/SegmentWriter.cs`：原子 Segment 写入（写 `.tmp` 目录 → `Directory.Move`）
    - 输出 `seg.hdr`（`SegmentHeader` unmanaged struct，little-endian）+ `keys.bin`（`KeyCodec` 序列化）+ `vectors.bin`（float32 行优先）
  - `src/DotVector.Core/Storage/SegmentReader.cs`：`MemoryMappedFile` + `MemoryMappedViewAccessor.ReadArray<float>` 单拷贝、safe-only 读取（AGENTS.md M0–M7 禁 `unsafe` 仍然适用）
  - `src/DotVector.Core/Storage/PersistentDirectory.cs`：
    - `FlushCollection<TKey>(Guid, FlatIndex<TKey>)`：旋转当前 WAL → `index.SnapshotSince(prevFlushedRows)` 取增量 → 写 Segment → 更新 manifest（`LastCoveredWalSequence`）→ 调用 `TryTrimWal` 删除已覆盖的 WAL 段
    - `CompactCollection<TKey>(Guid)`：合并所有现存 Segment 为单个新 Segment，原子提交 + 删除旧 Segment
    - `NotifyRestoredRowCount`：恢复完成后告知已加载行数，避免下次 Flush 重复写入
  - `src/DotVector.Core/Index/Flat/FlatIndex.cs`：新增 `SnapshotSince(int startRow, ...)` 增量快照 API（基于 `CollectionsMarshal.AsSpan`，零额外分配）
  - `src/DotVector.Core/Api/VectorDatabase.cs`：
    - 公开 `Flush()` / `Compact()` API
    - `LoadSegmentsInto` 在恢复时累加行数并调用 `NotifyRestoredRowCount`
    - 注册集合时正确调用 `AttachPersistence`（修复此前 `collections/` 目录不创建的 bug）
  - `tests/DotVector.Core.Tests/Persistence/`：5 个新增测试文件
    - `SegmentFlushTests`：Flush 后 segment + manifest 创建，重启后向量检索可用
    - `CompactionTests`：3 次 Flush 累积 3 个 Segment → Compact 合并为 1 个 → 重启后 5 条数据完整
    - `MmapSegmentReaderTests`：SegmentWriter round-trip + mmap 读取一致性
    - `WalTrimTests`：部分 Flush 保留未 flush 集合的 WAL；重复 Flush 裁剪旧 WAL
    - `CrashRecoveryTests`：遗留 `seg-*.tmp` 目录在重新打开时被忽略
  - 全部 178 个测试通过（DotVector.Core.Tests 147 + DotVector.Accuracy.Tests 17 + DotVector.Tests 14）

- PR #M6：M6 — 标量过滤（Payload Filter）
  - `src/DotVector.Core/Query/Filter.cs`：reflection-free Filter AST，AOT 友好
    - 公开静态工厂：`Eq` / `Ne` / `Range`（支持 inclusive/exclusive 上下界）/ `Exists` / `Missing` / `And` / `Or` / `Not`
    - 私有 sealed 节点：FieldEqualsFilter / FieldNotEqualsFilter / FieldRangeFilter / FieldExistsFilter / FieldMissingFilter / AndFilter / OrFilter / NotFilter
    - 范围比较使用 `IComparable.CompareTo`，类型不匹配吞掉 `ArgumentException` / `InvalidCastException` 返回 false
  - `src/DotVector.Core/Api/Collection.cs`：
    - 新增 `ConcurrentDictionary<TKey, IReadOnlyDictionary<string, object?>> _payloads` 内存 payload 存储
    - `Insert` / `InsertBatch` / `Delete` 同步维护 payload 快照
    - 新增 `GetPayload(TKey key)` 公开 API
    - 新增 `Search(query, topK, Filter?)` 重载：filter ≠ null 时按 `max(topK*8, topK+32)` 上限到 `Index.Count` 过取，再在 Collection 层 post-filter
    - 命中结果通过 `SearchResult<TKey> { Payload = ... }` 携带 payload 快照
  - `src/DotVector.Core/Api/SearchResult.cs`：新增 `IReadOnlyDictionary<string, object?>? Payload { get; init; }`
  - `tests/DotVector.Core.Tests/Query/FilterTests.cs`：12 个单测，覆盖 Eq/Ne/Range/Exists/Missing/And/Or/Not、null payload、type mismatch、工厂参数校验
  - `tests/DotVector.Core.Tests/Query/FilteredSearchTests.cs`：5 个端到端测试（无过滤 / Eq / Range / 复合 AND / 空匹配 / Delete 清理 payload / InsertBatch 存储 payload / 无 payload 时 Payload == null）
  - `tests/DotVector.Accuracy.Tests/FilteredRecallTests.cs`：FlatIndex 上验证带过滤 Recall = 1.0（远优于 ROADMAP < 5% 偏差要求），覆盖 L2 / Cosine
  - 备注：M6 范围内 payload 仅保存在内存中，**不写入 WAL**，重启后会丢失；持久化将在后续 Segment-flush milestone 与 vectors.bin / index.bin 一并实现

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

### Fixed

- 修复 Release 资产打包中 C NativeAOT connector publish 未传入 RuntimeIdentifier 导致 GitHub Release 发布失败的问题。

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
