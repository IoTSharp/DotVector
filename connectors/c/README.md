# DotVector C Connector

`connectors/c/native/DotVector.Native` publishes `DotVector.Core` as a NativeAOT shared library and exposes a small C ABI.

## ABI Scope

The ABI keeps opaque handles + primitive/UTF-8 buffers; payload / filter / 结果集统一用 UTF-8 JSON 文本承载。

- **Database lifecycle**：`dotvector_database_create` / `dotvector_database_open` / `dotvector_database_connect`（gRPC）/ `dotvector_database_free` / `dotvector_database_flush` / `dotvector_database_compact` / `dotvector_database_ping`
- **Collection 管理**：`dotvector_database_list_collections` / `dotvector_database_collection_exists` / `dotvector_database_create_collection` / `dotvector_database_ensure_collection` / `dotvector_database_delete_collection` / `dotvector_database_get_collection` / `dotvector_collection_describe`
- **写入**：`dotvector_collection_upsert` / `dotvector_collection_upsert_batch`
- **查询**：`dotvector_collection_search`（向量 + 可选 Filter）/ `dotvector_collection_query`（仅 Filter）/ `dotvector_collection_get`（按 ID）
- **删除**：`dotvector_collection_delete`
- **诊断**：`dotvector_version` / `dotvector_last_error`
- **遗留 int64**：`dotvector_collection_create_i64` / `dotvector_collection_get_i64` / `dotvector_collection_insert_i64` / `dotvector_collection_search_i64` / `dotvector_collection_count`

它**不**暴露 C# 对象、文件格式 struct 或引擎内部指针。

### 状态码

| 值 | 含义 |
|----|------|
| `0` `DOTVECTOR_OK` | 成功 |
| `-1` `DOTVECTOR_INVALID_ARGUMENT` | 参数非法 |
| `-2` `DOTVECTOR_NOT_FOUND` | 句柄/集合不存在 |
| `-3` `DOTVECTOR_BUFFER_TOO_SMALL` | 输出 buffer 不足，看 `*out_required_size` 重试 |
| `-100` `DOTVECTOR_FAILED` | 其它运行时错误，调用 `dotvector_last_error` 读详情 |

### 变长输出协议（BUFFER_TOO_SMALL 重试）

所有返回 JSON 的入口都遵循同一约定：

```c
int32_t required = 0;
int32_t status = dotvector_collection_search(col, query, dim, topk, filter, 0, NULL, 0, &required);
/* status==DOTVECTOR_BUFFER_TOO_SMALL，required = 实际所需字节数（不含末尾 NUL） */

char *buf = (char *)malloc(required + 1);
status = dotvector_collection_search(col, query, dim, topk, filter, 0, buf, required + 1, &required);
/* status==DOTVECTOR_OK，buf 是 NUL 结尾的 UTF-8 JSON */
free(buf);
```

`examples/quick_start_full.c` 中的 `read_variable()` 工具函数即此协议的通用包装。

### Payload JSON

仅支持 `string` / `int64` / `double` / `bool` / `null` —— 与 `DotVector.Data.Point.Payload` 的运行时约束一致。

```json
{"genre":"sci-fi","year":2021,"rating":4.5,"published":true}
```

### Filter JSON DSL

每个节点都是单键对象 `{"<op>": <args>}`，支持：

| 算子 | 形式 | 说明 |
|------|------|------|
| `eq` | `{"eq":{"field":value}}` | 精确等值 |
| `ne` | `{"ne":{"field":value}}` | 不等 |
| `range` | `{"range":{"field":{"min":?,"max":?,"min_inclusive":true,"max_inclusive":true}}}` | 至少 `min`/`max` 一者非 null |
| `exists` | `{"exists":"field"}` | payload 中存在该字段 |
| `missing` | `{"missing":"field"}` | payload 中不存在该字段 |
| `and` | `{"and":[<filter>, ...]}` | 全部满足 |
| `or` | `{"or":[<filter>, ...]}` | 至少一个满足 |
| `not` | `{"not":<filter>}` | 取反 |

示例：

```json
{"and":[
  {"or":[{"eq":{"genre":"sys"}},{"eq":{"genre":"algo"}}]},
  {"range":{"year":{"min":2000,"min_inclusive":true}}}
]}
```

### 结果 JSON 形状

`search`：`[{"id":"...","score":0.12,"payload":{...},"vector":[...]}, ...]`
`query` / `get`：`[{"id":"...","payload":{...},"vector":[...]}, ...]`
`describe` / `list_collections`：`{"name":"...","dimensions":N,"metric":"Cosine","record_count":N}` 单对象 / 数组。

`payload` / `vector` 字段在为空或未启用 `include_vector` 时省略。

## Build With CMake

The CMake build publishes the .NET NativeAOT library and links the C quickstart against it.

```powershell
cmake --preset windows-x64
cmake --build --preset windows-x64
```

Supported presets:

- `windows-x64`
- `windows-x86`
- `windows-arm64`
- `windows-xarm`
- `linux-x64`

For generators other than Visual Studio, configure the RID explicitly:

```powershell
cmake -S connectors/c -B artifacts/connectors/c/win-x64 -DDOTVECTOR_C_RID=win-x64
cmake --build artifacts/connectors/c/win-x64 --config Release
```

On Linux x64:

```bash
cmake -S connectors/c -B artifacts/connectors/c/linux-x64 -DDOTVECTOR_C_RID=linux-x64 -DCMAKE_BUILD_TYPE=Release
cmake --build artifacts/connectors/c/linux-x64
./artifacts/connectors/c/linux-x64/dotvector_quickstart
```

The build output contains:

- `dotvector_quickstart` / `dotvector_quickstart.exe` — 遗留 int64 ABI 演示
- `dotvector_quickstart_full` / `dotvector_quickstart_full.exe` — 完整 ABI 演示（payload + filter + batch）
- `DotVector.Native.dll` on Windows, or `DotVector.Native.so` on Linux
- `DotVector.Native.lib` for Windows linkers

## C Examples

两个示例都受 `DOTVECTOR_C_BUILD_EXAMPLES` 控制，禁用：

```powershell
cmake -S connectors/c -B artifacts/connectors/c/win-x64 -DDOTVECTOR_C_RID=win-x64 -DDOTVECTOR_C_BUILD_EXAMPLES=OFF
```

- `examples/quick_start.c`：嵌入式打开 `./quickstart_c.dvec`，使用旧 int64 ABI 创建 `books` 集合、插入三条向量、Top-3 搜索后落盘。
- `examples/quick_start_full.c`：完整 ABI 演示。打开 `./quickstart_c_full.dvec`，`ensure_collection` 4 维 Cosine `books_full`，`upsert_batch` 4 条带 payload 的向量，`describe` / `list_collections` / 带 Filter `search` / 仅 Filter `query` / 多 ID `get`（含向量）/ `delete` 单条 / `flush` / `delete_collection`。脚手架函数 `read_variable()` 演示 `DOTVECTOR_BUFFER_TOO_SMALL` 重试。
