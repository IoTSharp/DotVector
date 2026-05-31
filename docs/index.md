---
title: DotVector 文档
layout: default
---

<section class="hero">
  <div class="hero-inner">
    <p class="eyebrow">Embedded ANN · Native AOT · VectorData</p>
    <h1>DotVector</h1>
    <p class="hero-lede">面向 .NET 10 的嵌入式原生向量数据库，围绕 HNSW、IVF、Flat、单目录持久化和 VectorData 集成构建。</p>
    <div class="hero-actions" aria-label="主要入口">
      <a class="button primary" href="/DotVector/architecture/">查看架构</a>
      <a class="button" href="https://github.com/IoTSharp/DotVector">GitHub 仓库</a>
    </div>
    <div class="hero-metrics" aria-label="项目特性">
      <div class="metric">
        <strong>NuGet</strong>
        <span>进程内嵌入式运行</span>
      </div>
      <div class="metric">
        <strong>.dvec</strong>
        <span>单目录持久化布局</span>
      </div>
      <div class="metric">
        <strong>SIMD</strong>
        <span>TensorPrimitives 距离内核</span>
      </div>
    </div>
  </div>
</section>

<h2>文档入口</h2>

<div class="doc-grid">
  <a class="doc-card" href="/DotVector/architecture/">
    <strong>架构总览</strong>
    <span>Core / Data / CLI 分层，以及 SonnetDB 库级 adapter 边界。</span>
  </a>
  <a class="doc-card" href="/DotVector/algorithms/">
    <strong>算法参考</strong>
    <span>Flat、HNSW、IVF、PQ、DiskANN 等索引路线和参考实现。</span>
  </a>
  <a class="doc-card" href="/DotVector/dotnet10-advantages/">
    <strong>.NET 10 优势</strong>
    <span>Span、TensorPrimitives、AOT、mmap 与安全内存模型的工程取舍。</span>
  </a>
  <a class="doc-card" href="/DotVector/comparison/">
    <strong>产品对比</strong>
    <span>与 Milvus、Qdrant、pgvector、LanceDB、Chroma 的定位对照。</span>
  </a>
  <a class="doc-card" href="/DotVector/release/">
    <strong>发布说明</strong>
    <span>NuGet、GitHub Release 与 GitHub Pages 的发布流程。</span>
  </a>
  <a class="doc-card" href="/DotVector/release-news-v1.0.0">
    <strong>v1.0.0 发布</strong>
    <span>索引、量化、持久化与生态集成的版本概览。</span>
  </a>
  <a class="doc-card" href="https://github.com/IoTSharp/DotVector/blob/main/ROADMAP.md">
    <strong>路线图</strong>
    <span>M0 到 M16 的 Milestone、验收标准与后续规划。</span>
  </a>
</div>

<h2>项目门面</h2>

<div class="feature-grid">
  <div class="feature-card">
    <strong>DotVector.Core</strong>
    <span>嵌入式数据库引擎，包含 VectorDatabase、索引、持久化、过滤、量化与协议抽象。</span>
  </div>
  <div class="feature-card">
    <strong>DotVector.Data</strong>
    <span>客户端 SDK 项目，NuGet 包名为 DotVector，包含高层客户端、嵌入式工厂和 VectorData 适配。</span>
  </div>
  <div class="feature-card">
    <strong>DotVector.Cli</strong>
    <span>Native AOT 命令行工具，后续覆盖本地 `.dvec` 管理命令。</span>
  </div>
  <div class="feature-card">
    <strong>connectors/c</strong>
    <span>NativeAOT C ABI / P-Invoke 连接器，服务跨语言和本机集成场景。</span>
  </div>
  <div class="feature-card">
    <strong>connectors/python</strong>
    <span>Python ctypes Native 客户端路线，便于 RAG 原型和脚本集成。</span>
  </div>
</div>

<h2>快速运行</h2>

<div class="command-panel">
  <pre><code>dotnet restore DotVector.slnx
dotnet build DotVector.slnx -c Release
dotnet test DotVector.slnx -c Release --no-build</code></pre>
</div>

<p>文档站地址：<a href="https://iotsharp.net/DotVector/">https://iotsharp.net/DotVector/</a>。仓库内使用 <a href="https://github.com/JekyllNet/JekyllNet">JekyllNet</a> 构建静态站点，随后通过 GitHub Pages artifact 部署。</p>
