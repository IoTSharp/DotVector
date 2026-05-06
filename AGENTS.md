# AGENTS

本文件定义 AI 协作（如 GitHub Copilot Agent）在 DotVector 仓库工作的规范与约束。所有 AI 辅助生成的代码和文档均须遵守此规范。

---

## 项目目标

**DotVector** 是一个使用 C# / .NET 10 实现的嵌入式原生向量数据库，目标是：

> 可以通过 NuGet 直接引用，进程内嵌入式运行，**单目录持久化**（`.dvec/` 目录，每个 Segment 独立文件），零外部依赖，支持 HNSW / IVF / Flat 近似最近邻索引，与 `Microsoft.Extensions.VectorData` 天然集成，支持 Native AOT 部署。

当前 Milestone：**M0 — 工程骨架 + 文档 + 设计基线**（详见 [ROADMAP.md](ROADMAP.md)）。

> 后续路线：M1（SIMD 距离内核）→ M2（Flat 索引）→ M3（HNSW）→ M4（IVF/IVF-PQ）→ M5（持久化）→ M6（标量过滤）→ M7（VectorData 适配）→ M8（基准对比）→ M9（gRPC + AOT + Docker）。

---

## 强制约束

以下约束**不得违反**。如需例外，必须在 PR 描述中明确说明理由，并通过 reviewer 评审后方可执行。

### 1. 禁止 `unsafe`

**第一版（M0 ～ M7）禁止使用 `unsafe` 关键字。**

向量距离计算一律走 `System.Numerics.Tensors.TensorPrimitives` / `Vector<T>` / `Vector512<T>`，所有底层内存操作通过以下安全 API 完成：

| API | 用途 |
|-----|------|
| `Span<T>` / `ReadOnlySpan<T>` / `Memory<T>` | 内存切片与传递 |
| `MemoryMarshal.CreateSpan` / `AsBytes` / `Cast` / `Read` / `Write` | 类型转换与 reinterpret |
| `BinaryPrimitives` | 小端/大端整数读写 |
| `[InlineArray(N)]` | 固定大小的栈/结构体内嵌缓冲（magic bytes、保留字段） |
| `ArrayPool<T>` | 可复用堆缓冲区 |
| `stackalloc` | 小型栈缓冲 |
| `CollectionsMarshal` | `List<T>` 底层 span 访问 |
| `TensorPrimitives` | 向量距离 / 点积 / 归一化等 SIMD 加速原语 |
| `Vector<T>` / `Vector128<T>` / `Vector256<T>` / `Vector512<T>` | 硬件向量操作 |

### 2. 固定二进制结构体规范

所有固定二进制结构（`FileHeader`、`SegmentHeader`、`CollectionHeader`、`HnswNodeHeader`、`IvfListHeader` 等）必须：

```csharp
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct SegmentHeader
{
    // ...
}
```

- 类型必须为 `unmanaged struct`（不含托管引用）
- 字节序统一 **little-endian**（使用 `BinaryPrimitives` 读写多字节字段）
- 修改布局时必须同步升级 `FileHeader.Version`，并在 CHANGELOG 中记录

### 2a. 持久化格式约定（目录方案）

持久化格式采用**单目录**（`.dvec/`），不使用单文件，原因见 [ROADMAP.md M5](ROADMAP.md#m5)。

**禁止**实现单文件数据库容器（如 SQLite page manager 风格），原因：
- 内部页分配器实现复杂，容易引入 bug
- 单文件 mmap 无法按 Segment 粒度独立管理 OS 页面生命周期
- Compaction 时需 copy-on-write 整个文件，IO 放大严重
- 并行 IO 受单 fd 限制

目录结构规范（实现时须遵守）：

```
{name}.dvec/
├── catalog.bin               # CollectionHeader[] — unmanaged struct，little-endian
├── wal/
│   └── wal-{seq}.log         # WAL 段文件，顺序追加
└── collections/
    └── {collection-id}/
        └── segments/
            └── seg-{seq}/
                ├── seg.hdr   # SegmentHeader — unmanaged struct
                ├── vectors.bin  # float32[] 行优先，直接 mmap
                └── index.bin    # 索引数据（HNSW / IVF）
```

### 3. 编译器选项

所有项目必须启用：

```xml
<Nullable>enable</Nullable>
<ImplicitUsings>enable</ImplicitUsings>
<TreatWarningsAsErrors>true</TreatWarningsAsErrors>
<IsAotCompatible>true</IsAotCompatible>
```

测试与基准项目可在本地 csproj 中显式关闭 `IsAotCompatible`（因为 xUnit 大量反射不适用 AOT 分析）。

不得通过 `#pragma warning disable` 压制与本项目逻辑相关的警告，除非有充分注释说明。

### 4. 依赖约束

- 核心类库 `src/DotVector` **不得**引入任何第三方 NuGet 运行时依赖
  - 允许 `System.Numerics.Tensors`，因为属于 BCL 体系
- 测试项目可引用 `xunit`、`xunit.runner.visualstudio`、`Microsoft.NET.Test.Sdk`、`coverlet.collector`
- 基准项目可引用 `BenchmarkDotNet`，以及对照基准用的 `Qdrant.Client`、`Milvus.Client`、`Pgvector`、`Npgsql`
- `src/DotVector.Data` 可引用 `Microsoft.Extensions.VectorData.Abstractions`
- **不得**引入 `Newtonsoft.Json`、`Dapper`、`EntityFramework` 等大型依赖
- 若确有必要引入新依赖，须在 PR 描述中说明理由并通过评审

### 5. 格式版本变更

不得修改已发布的文件二进制格式（`FileHeader`、`SegmentHeader`、`HnswNodeHeader` 等结构体布局），除非同步：
1. 升级 `FileHeader.Version` 字段值
2. 在 PR 描述和 `CHANGELOG.md` 中明确标注格式变更
3. 添加格式迁移或拒绝旧格式的处理逻辑

---

## 代码规范

### 命名规范

遵循 [.NET 官方命名规范](https://learn.microsoft.com/zh-cn/dotnet/standard/design-guidelines/naming-guidelines)：

| 元素 | 规范 |
|------|------|
| 类型、方法、属性 | `PascalCase` |
| 私有字段 | `_camelCase` |
| 局部变量、参数 | `camelCase` |
| 常量 | `PascalCase`（不用全大写） |
| 接口 | `IXxx` |

### XML 文档注释

**所有 public API**（类型、方法、属性、构造函数）必须有 XML 文档注释，使用中文撰写：

```csharp
/// <summary>
/// 在集合中执行近似最近邻搜索。
/// </summary>
/// <param name="query">查询向量（维度须与集合一致）。</param>
/// <param name="topK">返回最相似的 K 个结果。</param>
/// <param name="cancellationToken">取消令牌。</param>
/// <returns>按相似度降序排列的搜索结果列表。</returns>
public IReadOnlyList<SearchResult<TKey>> Search(
    ReadOnlySpan<float> query,
    int topK,
    CancellationToken cancellationToken = default) { ... }
```

### TODO 标签规范

所有占位类型和未实现方法必须包含指向 ROADMAP Milestone 的 TODO 标签：

```csharp
// TODO(M3): 实现 HNSW 图索引 — 参见 ROADMAP.md M3
```

### 异常处理

- 参数校验使用 `ArgumentNullException.ThrowIfNull`、`ArgumentOutOfRangeException.ThrowIfNegative` 等现代 API
- 不吞掉 `IOException`、`InvalidDataException` 等存储层异常
- 自定义异常继承 `Exception` 并放置在 `DotVector.Exceptions` 命名空间

---

## 测试要求

### 覆盖率目标

单元测试覆盖率目标 **≥ 80%**（以行覆盖率计）。

### 必测场景

| 场景 | 要求 |
|------|------|
| 二进制 round-trip | 所有 `unmanaged struct` 必须有 `AsBytes` 写入后 `MemoryMarshal.Read` 读取的 round-trip 测试 |
| HNSW 召回率 | 在标准数据集上 Recall@10 ≥ 0.95（M3 后开始测） |
| IVF 召回率 | 在标准数据集上 Recall@10 ≥ 0.90（M4 后开始测） |
| 距离函数 SIMD vs scalar 一致性 | `TensorPrimitives` 与手写 scalar 结果差 < 1e-5（M1 后开始测） |
| 并发读 | 多线程并发只读索引，无数据竞争（M2 后开始测） |
| AOT 启动 | `DotVector.Cli` AOT 编译后能正常启动输出版本号（M9 后开始测） |
| 边界条件 | 空集合、单向量、维度不匹配抛出正确异常 |
| 持久化恢复 | WAL replay、Segment 重载（M5 后开始测） |

### 测试命名

遵循 `方法名_场景描述_预期结果` 格式：

```csharp
[Fact]
public void Search_WithEmptyCollection_ReturnsEmptyResults() { ... }

[Fact]
public void FileHeader_RoundTrip_PreservesAllFields() { ... }
```

---

## PR 规范

### 标题格式

```
<type>: <简述>
```

`type` 取值范围：

| type | 用途 |
|------|------|
| `feat` | 新功能 |
| `fix` | Bug 修复 |
| `docs` | 文档变更 |
| `refactor` | 重构（不改变行为） |
| `perf` | 性能优化 |
| `test` | 测试相关 |
| `build` | 构建系统 |
| `ci` | CI 配置 |
| `chore` | 杂项（依赖升级、格式等） |

示例：
- `feat(m1): 实现 L2 / Cosine / InnerProduct 距离函数`
- `test(m3): 补充 HNSW 召回率测试`
- `docs: 更新 ROADMAP 中 M3 验收标准`

### PR 内容要求

每个 PR 描述必须包含以下部分：

```markdown
## 变更点
- 简述本 PR 新增/修改了什么

## 对应 ROADMAP
- PR #N：<标题>

## 测试说明
- 新增 X 个测试，覆盖以下场景：...

## 是否破坏兼容
- [ ] 是（说明原因及迁移方案）
- [x] 否

## CHANGELOG 更新
- [ ] 已在 CHANGELOG.md 的 [Unreleased] 段落中记录
```

### 单一职责

**一个 PR 只做一件事**，对应 ROADMAP 中的一个 Milestone 编号。

若发现范围外的 bug，单独创建 PR 修复，不混入当前 PR。

---

## Commit 规范

遵循 [Conventional Commits](https://www.conventionalcommits.org/zh-hans/)：

```
<type>(<scope>): <简述>

[可选正文]

[可选 footer，例如 BREAKING CHANGE: ...]
```

示例：

```
feat(m1): 实现 L2 / Cosine / InnerProduct / Hamming 距离函数

基于 TensorPrimitives + Vector512<float> 实现四种距离计算。
提供 scalar 回退路径，确保跨平台一致性。
包含 SIMD vs scalar 精度一致性测试（差 < 1e-5）。
```

---

## CHANGELOG 更新要求

**每个 PR 必须更新 `CHANGELOG.md` 的 `[Unreleased]` 段落**，在对应分类（`Added / Changed / Fixed / Removed`）下添加条目：

```markdown
## [Unreleased]
### Added
- 实现 L2 / Cosine / InnerProduct / Hamming 距离函数（PR #2）
```

---

## 目录约定

```
DotVector/
├── src/
│   ├── DotVector/                   # 核心库（零第三方运行时依赖）
│   │   ├── Api/                   # 公共 API：VectorDatabase / Collection / SearchRequest / SearchResult
│   │   ├── Buffers/               # InlineArray 工具：Magic8 等
│   │   ├── Catalog/               # 集合元数据目录
│   │   ├── Compute/               # 距离函数（Distance.cs，TensorPrimitives 实现）
│   │   ├── Compression/           # PQ / SQ / OPQ 量化
│   │   ├── Exceptions/            # DotVectorException 及子类
│   │   ├── Format/                # unmanaged struct：FileHeader / SegmentHeader / HnswNodeHeader / IvfListHeader
│   │   ├── Index/
│   │   │   ├── Flat/              # Brute Force 索引（M2）
│   │   │   ├── Hnsw/              # HNSW 图索引（M3）
│   │   │   └── Ivf/               # IVF / IVF-PQ（M4）
│   │   ├── IO/                    # SpanReader / SpanWriter
│   │   ├── Model/                 # VectorRecord<TKey> / Metric 枚举
│   │   ├── PageStore/             # 页面管理（M5）
│   │   ├── Query/                 # 查询引擎
│   │   ├── Storage/               # MemTable / SegmentWriter / Reader
│   │   └── Wal/                   # WalWriter / WalReader
│   ├── DotVector.Core/              # 抽象与接口（IIndex / IStorage / IDistanceKernel<T>）
│   ├── DotVector.Data/              # Microsoft.Extensions.VectorData 适配（M7）
│   └── DotVector.Cli/               # 命令行工具
├── tests/
│   ├── DotVector.Tests/             # 集成测试
│   ├── DotVector.Core.Tests/        # 单元测试（镜像 src/DotVector 目录结构）
│   ├── DotVector.Accuracy.Tests/    # 召回率 / 精度测试
│   └── DotVector.Benchmarks/        # BenchmarkDotNet 基准测试
├── connectors/
│   └── c/native/DotVector.Native/  # C ABI / P-Invoke 连接器
├── eng/
│   └── benchmarks/
│       ├── run-benchmarks/          # 基准运行脚手架
│       └── start-benchmark-env/     # 对照环境启动（Testcontainers）
├── docs/
│   ├── architecture.md              # 架构总览（Mermaid）
│   ├── dotnet10-advantages.md       # .NET 10 向量数据库优势
│   ├── algorithms.md                # 算法参考清单
│   └── comparison.md                # 产品对比表
├── .github/
│   └── workflows/
│       ├── ci.yml                   # build + test（3 平台）
│       └── publish.yml              # NuGet 发布
├── .editorconfig
├── .config/dotnet-tools.json
├── Directory.Build.props
├── Directory.Packages.props
├── DotVector.slnx
├── global.json
├── AGENTS.md                        # 本文件
├── ROADMAP.md
└── CHANGELOG.md
```

---

## 禁止事项清单

| 禁止 | 原因 |
|------|------|
| 使用 `unsafe` | 第一版 Safe-only 原则（M0～M7） |
| 在 `src/DotVector` 中引入运行时第三方依赖 | 保持零依赖特性 |
| 引入 `Newtonsoft.Json`、`Dapper` 等大型库 | 最小化依赖 |
| 修改二进制格式不升级 `FileHeader.Version` | 破坏向后兼容 |
| 压制编译警告（无注释说明） | 维护代码质量 |
| 一个 PR 混入多个 ROADMAP 条目 | 保持 PR 可审查性 |
| 提交 build artifacts（`bin/`、`obj/`、`.nupkg`） | 保持仓库整洁 |
| 向量距离计算使用手写 P/Invoke C++ 内核 | 绕过 safe 约束 |
