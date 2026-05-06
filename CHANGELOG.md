# CHANGELOG

本文件遵循 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.0.0/) 格式，版本号遵循 [语义化版本](https://semver.org/lang/zh-CN/)。

---

## [Unreleased]

### Added

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
