# 算法参考清单

本文列出 DotVector 在各 Milestone 计划学习和参考的向量索引算法，逐条注明出处、论文链接与源码链接。

---

## 1. HNSW（Hierarchical Navigable Small World）

**计划 Milestone**：M3

**核心思想**：在多层级的小世界图中进行贪心搜索，每一层图是下一层图的稀疏子图，查询时从顶层开始逐层向下精化。

**学术论文**：
- Malkov & Yashunin (2016)："Efficient and robust approximate nearest neighbor search using Hierarchical Navigable Small World graphs"
  - https://arxiv.org/abs/1603.09320
  - 后续期刊版（2020）：https://arxiv.org/abs/1603.09320v3

**参考实现与差异分析**：

| 实现 | 语言 | 差异点 |
|------|------|--------|
| [hnswlib](https://github.com/nmslib/hnswlib) | C++ | 论文原始实现，算法标准参考 |
| [Qdrant HNSW](https://github.com/qdrant/qdrant/tree/master/lib/segment/src/index/hnsw_index) | Rust | 增加 payload 过滤、动态删除支持 |
| [Milvus HNSW](https://github.com/milvus-io/knowhere/tree/main/src/index/hnsw) | C++ (knowhere) | 基于 hnswlib，增加 GPU 支持 |
| [pgvector HNSW](https://github.com/pgvector/pgvector/blob/master/src/hnsw.c) | C | 集成到 PG AM（访问方法）框架，支持 VACUUM |

**DotVector 借鉴点**：
- 参数设计：`M`（每层最大连接数）、`EfConstruction`（构建时搜索宽度）、`EfSearch`（查询时搜索宽度）
- 增量插入：与 hnswlib 保持兼容的参数语义
- 内存布局：`HnswNodeHeader`（unmanaged struct）存储节点层级和邻居列表
- **不借鉴**：hnswlib 的 C++ allocator，DotVector 用 `ArrayPool<T>` 替代

---

## 2. IVF（Inverted File Index）

**计划 Milestone**：M4（IVF-Flat）

**核心思想**：用 K-Means 将向量空间划分为 `nlist` 个聚类，查询时只搜索最近的 `nprobe` 个聚类（倒排列表），大幅减少计算量。

**学术论文**：
- Jégou, Douze, Schmid (2011)："Product Quantization for Nearest Neighbor Search"
  - https://inria.hal.science/inria-00514462/document（包含 IVF 框架介绍）

**参考实现**：
- [FAISS IndexIVFFlat](https://github.com/facebookresearch/faiss/blob/main/faiss/IndexIVFFlat.h)
- [Milvus IVF_FLAT](https://github.com/milvus-io/milvus/blob/master/docs/design_docs/milvus-index-design.md)

**DotVector 借鉴点**：
- `nlist`、`nprobe` 参数设计
- K-Means 训练（随机初始化 + Lloyd 迭代）
- `IvfListHeader`（unmanaged struct）：倒排列表头

---

## 3. IVF-PQ（IVF + Product Quantization）

**计划 Milestone**：M4（IVF-PQ）

**核心思想**：在 IVF 基础上，对每个聚类中的残差向量做乘积量化（PQ）压缩，大幅降低内存占用（典型压缩比 8x～64x）。

**学术论文**：
- Jégou, Douze, Schmid (2011)："Product Quantization for Nearest Neighbor Search"
  - https://inria.hal.science/inria-00514462/document

**参考实现**：
- [FAISS IndexIVFPQ](https://github.com/facebookresearch/faiss/blob/main/faiss/IndexIVFPQ.h)
- [Milvus IVF_PQ](https://milvus.io/docs/index.md#IVF_PQ)

**DotVector 借鉴点**：
- PQ 参数：`M`（子空间数）、`nbits`（每子空间比特数）
- 码本训练：子空间 K-Means
- ADC（Asymmetric Distance Computation）：查询时用浮点向量对比 PQ 编码

---

## 4. PQ / OPQ / SQ 量化

**计划 Milestone**：M11

### 乘积量化（Product Quantization, PQ）
- 论文：Jégou et al.（同上）
- 将 d 维向量分成 m 个子空间，每个子空间独立量化为 `k*` 个码字
- 空间复杂度：`N × m × log2(k*)` bits（典型 8 bits/子空间）

### 优化乘积量化（Optimized PQ, OPQ）
- 论文：Ge et al. (2013)："Optimized Product Quantization"
  - https://arxiv.org/abs/1303.4014
- 在 PQ 基础上学习一个旋转矩阵，使量化误差最小

### 标量量化（Scalar Quantization, SQ）
- Milvus SQ8：每个 float32 量化为 int8，压缩 4x，误差较小
- 参考：https://milvus.io/docs/index.md#IVF_SQ8

**DotVector 借鉴点**：
- SQ8 作为入门量化（M11 早期），实现简单，效果好
- PQ 作为高压缩方案（M11 后期）

---

## 5. DiskANN（Vamana 图索引）

**计划 Milestone**：M10

**核心思想**：专为 SSD 优化的图索引算法，通过 Vamana 图构建 + 压缩向量（PQ）的两阶段搜索，实现内存放不下的大规模数据集（十亿级）的高性能 ANN 搜索。

**学术论文**：
- Subramanya et al. (2019)："DiskANN: Fast Accurate Billion-point Nearest Neighbor Search on a Single Node"
  - https://proceedings.neurips.cc/paper/2019/hash/09853c7fb1d3f8ee67a61b6bf4a7f8e6-Abstract.html

**参考实现**：
- [Microsoft DiskANN](https://github.com/microsoft/DiskANN)

**DotVector 借鉴点**：
- Vamana 图构建算法（比 HNSW 更适合磁盘存储）
- SSD 访问模式优化（顺序预读 vs 随机读）
- 压缩图 + 全精度向量两阶段查询

---

## 6. SPTAG（Space Partition Tree and Graph）

**计划 Milestone**：M10（参考）

**核心思想**：结合 KD-Tree 和 BKT（Balanced K-Means Tree）快速定位候选，再用图搜索精化，支持十亿级向量。

**学术论文**：
- Wang et al. (2012)："Query-driven Iterated Neighborhood Graph Search for Large Scale Indexing"

**参考实现**：
- [Microsoft SPTAG](https://github.com/microsoft/SPTAG)

**DotVector 参考价值**：BKT 构建算法可作为 IVF K-Means 的替代选项。

---

## 7. ScaNN（Scalable Nearest Neighbors）

**计划 Milestone**：M11（参考量化思路）

**核心思想**：各向异性向量量化（Anisotropic VQ），针对最大内积搜索（MIPS）优化量化误差，Google 内部大规模应用。

**学术论文**：
- Guo et al. (2020)："Accelerating Large-Scale Inference with Anisotropic Vector Quantization"
  - https://arxiv.org/abs/1908.10396

**参考实现**：
- [Google ScaNN](https://github.com/google-research/google-research/tree/master/scann)

**DotVector 参考价值**：量化方向的参考，特别是 MIPS（内积搜索）场景的量化策略。

---

## 8. LSH（Locality Sensitive Hashing）系列

**计划 Milestone**：参考价值，暂不实现

**核心思想**：通过哈希函数将相似向量映射到相同桶，O(1) 近似检索，但召回率不如 HNSW/IVF。

**代表论文**：
- Andoni & Indyk (2006)："Near-Optimal Hashing Algorithms for Approximate Nearest Neighbor in High Dimensions"

**DotVector 参考价值**：LSH 适合极高维稀疏向量（如 NLP 词袋），在 embedding 场景（密集 384-1536 维）性能不如 HNSW/IVF，暂不列入实现计划。

---

## 9. pgvector 的 `ivfflat` / `hnsw` 操作符

**参考 Milestone**：M3（HNSW）、M4（IVF）、M6（过滤）

**核心设计**：pgvector 将向量索引作为 PostgreSQL AM（访问方法）实现，支持标准 SQL `WHERE` 过滤。

**参考点**：
- [`hnsw.c`](https://github.com/pgvector/pgvector/blob/master/src/hnsw.c)：HNSW 的 PG AM 实现，参考其 `INSERT` / `SCAN` 接口设计
- [`ivfflat.c`](https://github.com/pgvector/pgvector/blob/master/src/ivfflat.c)：IVF-Flat 的 PG AM 实现
- 距离操作符：`<->` L2、`<=>` 余弦、`<#>` 内积
- 过滤下推（pushdown）：`SELECT ... ORDER BY vec <-> $1 WHERE category = 'news' LIMIT 10`

**DotVector 借鉴点**：
- M6 标量过滤的 pre-filtering vs post-filtering 策略参考 pgvector 的 WHERE 子句处理
- 距离 API 设计语义参考 pgvector 操作符定义

---

## 10. Milvus 的 Segment / Growing-Sealed 模型

**参考 Milestone**：M5（持久化层）

**核心设计**：
- `Growing Segment`：可写的内存段（类似 LSM MemTable）
- `Sealed Segment`：不可变的磁盘段（类似 SSTable），含向量数据 + 索引
- Flush 触发：Growing Segment 达到阈值时 flush 为 Sealed Segment
- 查询合并：搜索时合并 Growing + Sealed 的结果

**参考链接**：
- [Milvus Segment 设计](https://github.com/milvus-io/milvus/blob/master/docs/design_docs/storage/data_organization.md)

**DotVector 借鉴点**：
- `MemTable`（Growing）→ `SegmentWriter` flush → `SegmentReader`（Sealed）的生命周期模型
- 搜索时合并内存与磁盘结果

---

## 11. Lance 的列式 + 二级索引模型

**参考 Milestone**：M5（持久化格式）、M4（量化）

**核心设计**：
- Lance 格式：类似 Parquet 的列式存储，支持向量列 + 标量列混合
- 二级索引：IVF-PQ 构建在列式数据之上
- Delta 更新：追加写入，不修改历史数据（COW 风格）
- Fragment + Dataset 模型：数据分片管理

**参考链接**：
- [LanceDB / lance](https://github.com/lancedb/lance)
- [Lance 格式规范](https://github.com/lancedb/lance/blob/main/docs/format.rst)

**DotVector 借鉴点**：
- 向量列与标量列分离存储（M5 格式设计参考）
- Delta/append-only 写入模式，避免复杂原地更新
- Fragment 概念 → DotVector 的 Segment 模型

---

## 参考数据集

| 数据集 | 维度 | 向量数 | 用途 |
|--------|------|--------|------|
| SIFT-1M | 128 | 100 万 | M3/M4 召回率基准 |
| GloVe-1.2M | 100 | 120 万 | NLP embedding 场景 |
| Deep-1B | 96 | 10 亿 | M10 DiskANN 大规模测试 |
| MNIST | 784 | 60 万 | 低维测试 |

---

## 参考工具

- [ANN-Benchmarks](https://github.com/erikbern/ann-benchmarks)：标准化 ANN 算法评测框架，DotVector M8 基准参考
- [FAISS wiki](https://github.com/facebookresearch/faiss/wiki)：FAISS 算法选择指南
