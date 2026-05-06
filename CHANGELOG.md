# CHANGELOG

本文件遵循 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.0.0/) 格式，版本号遵循 [语义化版本](https://semver.org/lang/zh-CN/)。

---

## [Unreleased]

### Added

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
