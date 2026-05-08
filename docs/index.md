---
title: DotVector 文档
---

# DotVector 文档

DotVector 是面向 .NET 10 的嵌入式原生向量数据库，同时提供 gRPC 服务端、客户端 SDK、VectorData 适配、C / Python 连接器、Docker 部署和后续管理台路线。

文档站地址：<https://iotsharp.net/DotVector/>

## 项目门面

- `DotVector.Core`：嵌入式数据库引擎，包含 `VectorDatabase`、索引、持久化、过滤、量化与协议抽象。
- `DotVector`：gRPC 服务端宿主和 Docker 镜像入口。
- `DotVector.Data`：发布用客户端 SDK，包含高层客户端、gRPC 客户端、嵌入式工厂和 VectorData 适配。
- `DotVector.Cli`：连接远端 gRPC 服务的命令行工具。
- `connectors/c` / `connectors/python`：跨语言接入，覆盖 NativeAOT C ABI、Python gRPC 与 ctypes Native 客户端。

## 入门

- [README](https://github.com/IoTSharp/DotVector/blob/main/README.md)：项目定位、快速开始与包说明
- [架构总览](/DotVector/architecture/)：Core / Server / Data / CLI 分层
- [发布说明](/DotVector/release/)：NuGet、Docker、GitHub Release 与 Pages 发布
- [路线图](https://github.com/IoTSharp/DotVector/blob/main/ROADMAP.md)：Milestone、验收标准与后续规划

## 设计文档

- [算法参考](/DotVector/algorithms/)：Flat、HNSW、IVF、PQ、DiskANN 等算法基线
- [.NET 10 优势](/DotVector/dotnet10-advantages/)：运行时、SIMD、AOT 与 Span 生态
- [产品对比](/DotVector/comparison/)：与主流向量数据库的定位对比

## 运行与发布

```bash
dotnet restore DotVector.slnx
dotnet build DotVector.slnx -c Release
dotnet test DotVector.slnx -c Release --no-build
docker compose up --build
```

文档站由 GitHub Actions 构建并发布到 GitHub Pages。仓库内使用 [`JekyllNet`](https://github.com/JekyllNet/JekyllNet) 构建静态站点，随后通过 GitHub Pages artifact 部署。
