/*
 * DotVector C ABI — public header.
 *
 * 该 ABI 把 DotVector.Data 的本地嵌入式能力以 NativeAOT 形式暴露给 C/C++/Python/其它 FFI。
 *
 * 设计原则：
 *  - 只导出不透明句柄 + 原生缓冲区（int / float / 字节）。
 *  - 字符串一律 NUL 结尾的 UTF-8。
 *  - 变长输出（list / search / payload）一律使用 caller 提供的字节缓冲 + 必需长度返回值；
 *    如果 buffer 不足，函数返回 DOTVECTOR_BUFFER_TOO_SMALL 并把所需总长度（不含 NUL）写入 *out_required_size。
 *    caller 重新分配后重试即可。
 *  - 对外 enum 编号锚定到 C# `DotVector.Model.Metric` / `DotVector.Model.IndexKind`，
 *    并非 `DotVector.Data.DistanceMetric` 的编号（后者仅供 .NET 用户）。
 *  - 出错时函数返回非 0 状态码或 NULL 句柄；详细信息可通过 dotvector_last_error 获取（线程本地）。
 *
 * Payload / Filter / 结果集统一以 UTF-8 JSON 文本传递：
 *
 *  Payload JSON 形如：
 *      {"genre":"sci-fi","year":2021,"rating":4.5,"published":true}
 *  仅支持 string / int64 / double / bool / null。
 *
 *  Filter JSON 形如：
 *      {"and":[
 *          {"eq":{"genre":"sci-fi"}},
 *          {"range":{"year":{"min":2020,"min_inclusive":true}}},
 *          {"or":[ {"exists":"author"}, {"missing":"editor"} ]}
 *      ]}
 *  支持的算子：eq / ne / range / exists / missing / and / or / not。
 *  range 的字段子对象支持 min / max / min_inclusive / max_inclusive。
 *
 *  搜索结果 JSON 形如：
 *      [
 *        {"id":"abc","score":0.123,"payload":{...},"vector":[0.1,0.2,...]},
 *        ...
 *      ]
 *  payload / vector 字段在未启用时省略。
 */

#ifndef DOTVECTOR_H
#define DOTVECTOR_H

#include <stdint.h>

#ifdef _WIN32
#  define DOTVECTOR_API __declspec(dllimport)
#else
#  define DOTVECTOR_API
#endif

#ifdef __cplusplus
extern "C" {
#endif

/* ------------------------------------------------------------------------- */
/* Opaque handles                                                            */
/* ------------------------------------------------------------------------- */

typedef struct dotvector_database  dotvector_database;
typedef struct dotvector_collection dotvector_collection;

typedef dotvector_database*   dotvector_database_t;
typedef dotvector_collection* dotvector_collection_t;

/* ------------------------------------------------------------------------- */
/* Status / enums                                                            */
/* ------------------------------------------------------------------------- */

enum dotvector_status {
    DOTVECTOR_OK                  =    0,
    DOTVECTOR_INVALID_ARGUMENT    =   -1,
    DOTVECTOR_NOT_FOUND           =   -2,
    DOTVECTOR_BUFFER_TOO_SMALL    =   -3,
    DOTVECTOR_FAILED              = -100
};

/* 数值锚定到 C# DotVector.Model.Metric。 */
enum dotvector_metric {
    DOTVECTOR_METRIC_L2            = 0,
    DOTVECTOR_METRIC_COSINE        = 1,
    DOTVECTOR_METRIC_INNER_PRODUCT = 2,
    DOTVECTOR_METRIC_HAMMING       = 3,
    DOTVECTOR_METRIC_DOT_PRODUCT   = 4
};

/* 数值锚定到 C# DotVector.Model.IndexKind。仅 *_i64 兼容入口使用。 */
enum dotvector_index_kind {
    DOTVECTOR_INDEX_FLAT     = 0,
    DOTVECTOR_INDEX_HNSW     = 1,
    DOTVECTOR_INDEX_IVF_FLAT = 2,
    DOTVECTOR_INDEX_IVF_PQ   = 3,
    DOTVECTOR_INDEX_VAMANA   = 4
};

/* ------------------------------------------------------------------------- */
/* Diagnostics                                                               */
/* ------------------------------------------------------------------------- */

DOTVECTOR_API int32_t dotvector_version(char* buffer, int32_t buffer_length);
DOTVECTOR_API int32_t dotvector_last_error(char* buffer, int32_t buffer_length);

/* ------------------------------------------------------------------------- */
/* Database lifecycle                                                        */
/* ------------------------------------------------------------------------- */

/* 在临时目录里新建一个嵌入式数据库（关闭时目录会被删除）。 */
DOTVECTOR_API dotvector_database_t dotvector_database_create(void);

/* 打开/创建指定路径的本地嵌入式数据库目录。 */
DOTVECTOR_API dotvector_database_t dotvector_database_open(const char* path);

/*
 * 旧版远程连接入口。DotVector 独立 Server / gRPC 模式已删除。
 * 该函数保留 ABI 符号用于兼容旧加载器，调用时返回 NULL，并可通过
 * dotvector_last_error 读取迁移提示。请使用 dotvector_database_open 打开本地
 * 嵌入式数据库；需要服务端 endpoint 时使用 SonnetDB。
 */
DOTVECTOR_API dotvector_database_t dotvector_database_connect(
    const char* endpoint,
    const char* database_name,
    const char* api_key,
    int32_t     use_proxy);

DOTVECTOR_API void    dotvector_database_free(dotvector_database_t database);
DOTVECTOR_API int32_t dotvector_database_flush(dotvector_database_t database);   /* 嵌入式独占 */
DOTVECTOR_API int32_t dotvector_database_compact(dotvector_database_t database); /* 嵌入式独占 */

/* 检查本地数据库句柄是否可用。返回 0=失败 / 1=成功 / 负数=错误。 */
DOTVECTOR_API int32_t dotvector_database_ping(dotvector_database_t database);

/*
 * 列出所有集合，结果以 UTF-8 JSON 数组写入 out_buffer：
 *   [{"name":"...","dimensions":N,"metric":"Cosine","record_count":N}, ...]
 * 若 buffer 不足，返回 DOTVECTOR_BUFFER_TOO_SMALL，并把所需字节数（不含 NUL）写入 *out_required_size。
 */
DOTVECTOR_API int32_t dotvector_database_list_collections(
    dotvector_database_t database,
    char*    out_buffer,
    int32_t  buffer_length,
    int32_t* out_required_size);

/* 集合是否存在。返回 0=不存在 / 1=存在 / 负数=错误。 */
DOTVECTOR_API int32_t dotvector_database_collection_exists(
    dotvector_database_t database,
    const char* name);

/* 创建字符串主键集合；已存在时返回错误。 */
DOTVECTOR_API int32_t dotvector_database_create_collection(
    dotvector_database_t database,
    const char* name,
    int32_t     dimensions,
    int32_t     metric);

/* 不存在则创建，存在直接返回 OK；不校验维度/度量是否一致。 */
DOTVECTOR_API int32_t dotvector_database_ensure_collection(
    dotvector_database_t database,
    const char* name,
    int32_t     dimensions,
    int32_t     metric);

DOTVECTOR_API int32_t dotvector_database_delete_collection(
    dotvector_database_t database,
    const char* name);

/* 字符串主键的集合句柄；不发请求、不校验存在性。 */
DOTVECTOR_API dotvector_collection_t dotvector_database_get_collection(
    dotvector_database_t database,
    const char* name);

/* ------------------------------------------------------------------------- */
/* Collection lifecycle                                                      */
/* ------------------------------------------------------------------------- */

/* 创建 int64 主键的集合（向后兼容入口；底层依旧把 key.ToString() 作为字符串 ID）。 */
DOTVECTOR_API dotvector_collection_t dotvector_collection_create_i64(
    dotvector_database_t database,
    const char* name,
    int32_t     dimensions,
    int32_t     metric,
    int32_t     index_kind);

/* 取已有 int64 主键集合句柄。 */
DOTVECTOR_API dotvector_collection_t dotvector_collection_get_i64(
    dotvector_database_t database,
    const char* name);

DOTVECTOR_API void    dotvector_collection_free(dotvector_collection_t collection);
DOTVECTOR_API int64_t dotvector_collection_count(dotvector_collection_t collection);

/* 元数据 JSON：{"name":"...","dimensions":N,"metric":"Cosine","record_count":N}。 */
DOTVECTOR_API int32_t dotvector_collection_describe(
    dotvector_collection_t collection,
    char*    out_buffer,
    int32_t  buffer_length,
    int32_t* out_required_size);

/* ------------------------------------------------------------------------- */
/* int64 兼容入口（保留 v0.1 ABI）                                            */
/* ------------------------------------------------------------------------- */

DOTVECTOR_API int32_t dotvector_collection_insert_i64(
    dotvector_collection_t collection,
    int64_t      key,
    const float* vector,
    int32_t      dimensions);

DOTVECTOR_API int32_t dotvector_collection_search_i64(
    dotvector_collection_t collection,
    const float* query,
    int32_t      dimensions,
    int32_t      top_k,
    int64_t*     out_keys,
    float*       out_scores,
    int32_t*     out_count);

/* ------------------------------------------------------------------------- */
/* 字符串主键 + payload + filter 全功能入口                                   */
/* ------------------------------------------------------------------------- */

/*
 * Upsert 单条记录。
 *   id_utf8:        必填，记录 ID（UTF-8）。
 *   vector:         dim 个 float。
 *   payload_json:   可为 NULL；为 NULL 表示无 payload。
 */
DOTVECTOR_API int32_t dotvector_collection_upsert(
    dotvector_collection_t collection,
    const char*  id_utf8,
    const float* vector,
    int32_t      dimensions,
    const char*  payload_json);

/*
 * 批量 Upsert。
 *   ids_utf8:      长度 = count，每项是 NUL 结尾的 UTF-8 字符串指针。
 *   flat_vectors:  扁平 float 数组，长度 = count * dimensions（按 ids 顺序）。
 *   payloads_json: 可为 NULL；非 NULL 时长度 = count，每项可为 NULL（表示该条无 payload）。
 */
DOTVECTOR_API int32_t dotvector_collection_upsert_batch(
    dotvector_collection_t collection,
    const char* const* ids_utf8,
    int32_t            count,
    const float*       flat_vectors,
    int32_t            dimensions,
    const char* const* payloads_json);

/* 删除若干记录。 */
DOTVECTOR_API int32_t dotvector_collection_delete(
    dotvector_collection_t collection,
    const char* const* ids_utf8,
    int32_t            count);

/*
 * 按 ID 取回记录。结果 JSON 形如：
 *   [{"id":"...","payload":{...},"vector":[...]}, ...]
 *   include_vector=0 时省略 vector 字段；payload 为空时省略 payload。
 */
DOTVECTOR_API int32_t dotvector_collection_get(
    dotvector_collection_t collection,
    const char* const* ids_utf8,
    int32_t            count,
    int32_t            include_vector,
    char*    out_buffer,
    int32_t  buffer_length,
    int32_t* out_required_size);

/*
 * 近似最近邻搜索。
 *   filter_json:     可为 NULL，否则为 Filter JSON。
 *   include_vector:  0 / 1。
 * 结果 JSON 形如：
 *   [{"id":"...","score":0.12,"payload":{...},"vector":[...]}, ...]
 */
DOTVECTOR_API int32_t dotvector_collection_search(
    dotvector_collection_t collection,
    const float* query,
    int32_t      dimensions,
    int32_t      top_k,
    const char*  filter_json,
    int32_t      include_vector,
    char*    out_buffer,
    int32_t  buffer_length,
    int32_t* out_required_size);

/*
 * 仅按 payload 过滤，不参与向量相似度（对应 DotVectorCollection.QueryAsync / Scroll）。
 *   filter_json:    必填。
 *   top:            最多返回的记录数。
 *   include_vector: 0 / 1。
 * 结果 JSON 形如：
 *   [{"id":"...","payload":{...},"vector":[...]}, ...]
 */
DOTVECTOR_API int32_t dotvector_collection_query(
    dotvector_collection_t collection,
    const char* filter_json,
    int32_t     top,
    int32_t     include_vector,
    char*    out_buffer,
    int32_t  buffer_length,
    int32_t* out_required_size);

#ifdef __cplusplus
}
#endif

#endif /* DOTVECTOR_H */
