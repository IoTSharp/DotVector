# DotVector 架构总览

本文档描述 DotVector 的整体架构分层设计，以及**客户端/服务端分离**原则。

---

## 项目依赖关系

```mermaid
graph LR
    subgraph Client["客户端（用户应用）"]
        APP["用户应用\n(ASP.NET Core / SK / MAUI)"]
        DATA["DotVector.Data\n(IVectorStore 适配层)"]
        CLI2["DotVector.Cli\n(gRPC 客户端模式)"]
    end

    subgraph Contract["共享契约层"]
        CORE["DotVector.Core\n(IDotVectorClient\nIIndex / IStorage\nProtocol DTOs)"]
    end

    subgraph Server["服务端"]
        SRV["DotVector\n(服务器实现)"]
        CLI["DotVector.Cli\n(gRPC server / 嵌入式)"]
    end

    APP --> DATA
    APP --> CLI2
    DATA --> CORE
    CLI2 --> CORE
    SRV --> CORE
    CLI --> SRV
    CLI --> CORE

    style Contract fill:#ffe,stroke:#cc0
    style Client fill:#eff,stroke:#0cc
    style Server fill:#fef,stroke:#c0c
```

**关键约束**：
- `DotVector.Data`（客户端）**禁止**直接引用 `DotVector`（服务端）
- 二者只通过 `DotVector.Core` 中的 `IDotVectorClient` 接口通信
- 传输实现（gRPC / 进程内）在运行时注入，对 `DotVector.Data` 透明

---

## 系统分层图（服务端内部）

```mermaid
graph TD
    subgraph API["API 层（服务端）"]
        VDB["VectorDatabase"]
        COL["Collection&lt;TKey&gt;"]
        REQ["SearchRequest"]
        RES["SearchResult&lt;TKey&gt;"]
        LCL["LocalDotVectorClient\n(IDotVectorClient 进程内实现, M9)"]
    end

    subgraph Index["索引层"]
        FLAT["Flat Index\n(M2: BruteForce)"]
        HNSW["HNSW Index\n(M3: 图索引)"]
        IVF["IVF / IVF-PQ\n(M4: 倒排+量化)"]
    end

    subgraph Filter["过滤层"]
        SF["Scalar Filter\n(M6: payload index)"]
    end

    subgraph Storage["存储层"]
        WAL["WAL\n(WalWriter/WalReader)"]
        SEG["Segment\n(SegmentWriter/Reader)"]
        MMAP["Memory-Mapped File\n(目录持久化, M5)"]
        MT["MemTable\n(内存写缓冲)"]
    end

    subgraph Format["格式层"]
        FH["FileHeader\n(unmanaged struct)"]
        SH["SegmentHeader"]
        NH["HnswNodeHeader"]
        IH["IvfListHeader"]
    end

    subgraph Compute["计算层 (SIMD)"]
        L2["L2 Distance\n(TensorPrimitives)"]
        COS["Cosine Similarity\n(TensorPrimitives)"]
        IP["InnerProduct\n(TensorPrimitives.Dot)"]
        HAM["Hamming\n(BitOperations)"]
    end

    subgraph Catalog["目录层"]
        CAT["CollectionCatalog\n(集合元数据)"]
    end

    subgraph Query["查询层"]
        QE["QueryEngine\n(ANN / KNN 调度)"]
    end

    LCL --> VDB
    VDB --> COL
    COL --> QE
    COL --> Index
    QE --> SF
    QE --> FLAT
    QE --> HNSW
    QE --> IVF
    FLAT --> Compute
    HNSW --> Compute
    IVF --> Compute
    FLAT --> Storage
    HNSW --> Storage
    IVF --> Storage
    Storage --> Format
    Storage --> MMAP
    WAL --> MMAP
    SEG --> MMAP
    MT --> WAL
    CAT --> Storage
```

---

## 层次职责

### 共享契约层（`src/DotVector.Core/`）

所有跨越客户端/服务端边界的类型都在此定义。

| 类型 | 职责 |
|------|------|
| `IDotVectorClient` | 客户端协议抽象，定义所有操作契约（Create / Upsert / Delete / Search / Ping） |
| `IIndex<TKey>` | 向量索引抽象（服务端内部） |
| `IStorage` | 持久化存储抽象（服务端内部） |
| `IDistanceKernel<T>` | 距离计算内核抽象 |
| `Protocol/ProtocolDtos.cs` | 协议 DTO：`CreateCollectionRequest` / `VectorUpsertRecord` / `VectorSearchRequest` / `VectorSearchResult` |

### 客户端适配层（`src/DotVector.Data/`）

实现 `Microsoft.Extensions.VectorData.Abstractions` 接口，通过 `IDotVectorClient` 与服务端通信（M7）。

**不引用** `DotVector`（服务端）程序集。

```
DotVector.Data
    ↓ 依赖
DotVector.Core（IDotVectorClient + Protocol DTOs）
    ↑ 实现（运行时注入）
GrpcDotVectorClient（M9，位于 DotVector.Data）
LocalDotVectorClient（M9，位于 DotVector，供进程内嵌入式使用）
```

### API 层（`src/DotVector/Api/`）

服务端对外暴露的 API，是嵌入式使用的入口点。

| 类型 | 职责 |
|------|------|
| `VectorDatabase` | 数据库实例，管理多个 Collection |
| `Collection<TKey>` | 单个向量集合，封装索引 + 存储 |
| `SearchResult<TKey>` | 单条搜索结果（Key、Score、Payload） |
| `LocalDotVectorClient`（M9） | 实现 `IDotVectorClient`，进程内直接调用 VectorDatabase，零序列化开销 |

### 索引层（`src/DotVector/Index/`）

| 索引 | Milestone | 算法 |
|------|-----------|------|
| `FlatIndex<TKey>` | M2 | 线性扫描（精确） |
| `HnswIndex<TKey>` | M3 | HNSW 图（近似） |
| `IvfFlatIndex<TKey>` | M4 | IVF 倒排文件（近似） |
| `IvfPqIndex<TKey>` | M4 | IVF + 乘积量化（压缩） |

### 计算层（`src/DotVector/Compute/`）

所有距离函数的 SIMD 加速实现，基于 `TensorPrimitives` 与 `Vector512<T>`。纯函数设计，无 IO 和状态。

### 存储层（`src/DotVector/Storage/` + `Wal/`）

负责数据持久化（M5 后启用）：
- `MemTable` — 内存写缓冲，写满后 flush 到 Segment
- `WalWriter/WalReader` — 崩溃安全的预写日志
- `SegmentWriter/Reader` — 不可变数据段，基于 mmap

### 格式层（`src/DotVector/Format/`）

所有 `unmanaged struct`，字节序 little-endian，`[StructLayout(Sequential, Pack=1)]`。

**持久化采用单目录方案**（`.dvec/`），每个 Segment 是独立文件，可以独立 mmap。与 LanceDB Fragment、RocksDB SST 的做法一致，性能优于单文件。

```
目录布局（M5）：
my-database.dvec/
├── catalog.bin                 # FileHeader + CollectionHeader[]（unmanaged struct）
├── wal/
│   ├── wal-000001.log          # WAL 段：顺序追加，每条 entry 含 CRC
│   └── wal-000002.log          # 旧 WAL 在 flush 后截断删除
└── collections/
    └── {collection-id}/
        └── segments/
            ├── seg-000001/
            │   ├── seg.hdr     # SegmentHeader：向量数、维度、创建时间戳
            │   ├── vectors.bin # float32[N][dim]，行优先，直接 mmap
            │   └── index.bin   # 索引序列化（HNSW 邻居表 / IVF 倒排列表）
            └── seg-000002/
                └── ...
```

---

## 数据流示意

### 远程访问（gRPC，M9）

```
用户应用
  → DotVector.Data（IVectorStore）
    → GrpcDotVectorClient（IDotVectorClient）
      → [gRPC 传输]
        → DotVector.Cli（gRPC server）
          → VectorDatabase.Search(...)
            → QueryEngine → HnswIndex → Compute.Distance
```

### 进程内嵌入式访问（M9）

```
用户应用（直接引用 DotVector）
  → VectorDatabase.CreateCollection(...)
  → Collection.Search(queryVec, topK=10)
    → QueryEngine → HnswIndex → TensorPrimitives.CosineSimilarity
```

### 通过 VectorData 接口的进程内访问（M9）

```
用户应用（SK / Semantic Kernel）
  → IVectorStore（DotVectorVectorStore）
    → IDotVectorClient（LocalDotVectorClient）
      → VectorDatabase [进程内，零序列化]
        → QueryEngine → HnswIndex → Compute
```

---

## 并发模型

- 写操作：单写者模型（`ReaderWriterLockSlim`）
- 读操作：多读者并发（无锁读 mmap 数据）
- 索引构建（M3 HNSW）：后台线程，读写分离

---

## AOT 兼容性

所有生产代码启用 `IsAotCompatible=true`。关键约束：
- 不使用 `Activator.CreateInstance` 或反射
- 泛型约束明确（`where T : unmanaged`）
- Protocol DTOs 不依赖运行时反射序列化（M9 gRPC 用 source-generated marshalling）

