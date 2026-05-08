/*
 * DotVector C Connector — Full QuickStart 示例
 *
 * 演示新 ABI 的字符串主键 + payload + filter 完整能力：
 *   1. 打开嵌入式数据库
 *   2. ensure 一个 Cosine 集合
 *   3. dotvector_collection_upsert_batch 一次写入多条带 payload 的记录
 *   4. dotvector_collection_search 带 Filter JSON 的相似度搜索
 *   5. dotvector_collection_query 仅按 payload 过滤
 *   6. dotvector_collection_get 按 ID 取回
 *   7. dotvector_database_list_collections / dotvector_collection_describe
 *   8. 删除集合并释放资源
 *
 * 变长输出统一使用 "BUFFER_TOO_SMALL 重试" 协议；本示例提供 read_variable() 工具函数。
 */

#include "dotvector.h"

#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#define DIM 4

static void print_last_error(const char *prefix)
{
    char buffer[1024];
    int32_t written = dotvector_last_error(buffer, (int32_t)sizeof(buffer));
    if (written > 0)
    {
        fprintf(stderr, "%s: %s\n", prefix, buffer);
    }
    else
    {
        fprintf(stderr, "%s: (no detailed error)\n", prefix);
    }
}

/*
 * read_variable — 通用 BUFFER_TOO_SMALL 重试帮手。
 *   call(buffer, length, &required) 必须实现 ABI 的变长输出协议。
 *   返回 malloc 出来的 NUL 结尾 UTF-8 字符串；调用方负责 free。
 *   失败返回 NULL。
 */
typedef int32_t (*variable_call_t)(void *ctx, char *buffer, int32_t length, int32_t *out_required);

static char *read_variable(variable_call_t call, void *ctx, const char *what)
{
    int32_t required = 0;
    int32_t status = call(ctx, NULL, 0, &required);
    if (status != DOTVECTOR_OK && status != DOTVECTOR_BUFFER_TOO_SMALL)
    {
        print_last_error(what);
        return NULL;
    }
    if (required <= 0)
    {
        char *empty = (char *)malloc(1);
        if (empty) empty[0] = '\0';
        return empty;
    }
    int32_t cap = required + 1;
    char *buf = (char *)malloc((size_t)cap);
    if (!buf) return NULL;
    status = call(ctx, buf, cap, &required);
    if (status != DOTVECTOR_OK)
    {
        print_last_error(what);
        free(buf);
        return NULL;
    }
    return buf;
}

/* ---- variable_call_t 适配器 ---- */
typedef struct { dotvector_database_t db; } list_ctx_t;
static int32_t call_list(void *ctx, char *buf, int32_t len, int32_t *req)
{
    list_ctx_t *c = (list_ctx_t *)ctx;
    return dotvector_database_list_collections(c->db, buf, len, req);
}

typedef struct { dotvector_collection_t col; } describe_ctx_t;
static int32_t call_describe(void *ctx, char *buf, int32_t len, int32_t *req)
{
    describe_ctx_t *c = (describe_ctx_t *)ctx;
    return dotvector_collection_describe(c->col, buf, len, req);
}

typedef struct {
    dotvector_collection_t col;
    const float *query;
    int32_t dim;
    int32_t topk;
    const char *filter_json;
    int32_t include_vector;
} search_ctx_t;
static int32_t call_search(void *ctx, char *buf, int32_t len, int32_t *req)
{
    search_ctx_t *c = (search_ctx_t *)ctx;
    return dotvector_collection_search(
        c->col, c->query, c->dim, c->topk, c->filter_json, c->include_vector, buf, len, req);
}

typedef struct {
    dotvector_collection_t col;
    const char *filter_json;
    int32_t top;
    int32_t include_vector;
} query_ctx_t;
static int32_t call_query(void *ctx, char *buf, int32_t len, int32_t *req)
{
    query_ctx_t *c = (query_ctx_t *)ctx;
    return dotvector_collection_query(
        c->col, c->filter_json, c->top, c->include_vector, buf, len, req);
}

typedef struct {
    dotvector_collection_t col;
    const char *const *ids;
    int32_t count;
    int32_t include_vector;
} get_ctx_t;
static int32_t call_get(void *ctx, char *buf, int32_t len, int32_t *req)
{
    get_ctx_t *c = (get_ctx_t *)ctx;
    return dotvector_collection_get(
        c->col, c->ids, c->count, c->include_vector, buf, len, req);
}

int main(void)
{
    char version[128];
    if (dotvector_version(version, (int32_t)sizeof(version)) > 0)
    {
        printf("DotVector native: %s\n", version);
    }

    const char *db_path = "./quickstart_full_c.dvec";
    dotvector_database_t db = dotvector_database_open(db_path);
    if (!db) { print_last_error("database_open"); return EXIT_FAILURE; }
    printf("Opened database at %s\n", db_path);

    const char *collection_name = "books_full";
    int32_t status = dotvector_database_ensure_collection(
        db, collection_name, DIM, DOTVECTOR_METRIC_COSINE);
    if (status != DOTVECTOR_OK) { print_last_error("ensure_collection"); goto fail_db; }

    dotvector_collection_t col = dotvector_database_get_collection(db, collection_name);
    if (!col) { print_last_error("get_collection"); goto fail_db; }

    /* 批量 upsert：4 条记录，带 payload。 */
    const char *ids[] = { "b1", "b2", "b3", "b4" };
    const float vectors[] = {
        0.10f, 0.20f, 0.30f, 0.40f,
        0.20f, 0.10f, 0.40f, 0.30f,
        0.90f, 0.10f, 0.05f, 0.05f,
        0.05f, 0.05f, 0.10f, 0.90f
    };
    const char *payloads[] = {
        "{\"genre\":\"sys\",\"year\":2015,\"rating\":4.7}",
        "{\"genre\":\"eng\",\"year\":2018,\"rating\":4.5}",
        "{\"genre\":\"lang\",\"year\":1988,\"rating\":4.9}",
        "{\"genre\":\"algo\",\"year\":2009,\"rating\":4.8}"
    };
    status = dotvector_collection_upsert_batch(col, ids, 4, vectors, DIM, payloads);
    if (status != DOTVECTOR_OK) { print_last_error("upsert_batch"); goto fail_col; }
    printf("Upserted %lld points.\n", (long long)dotvector_collection_count(col));

    /* describe */
    {
        describe_ctx_t ctx = { col };
        char *json = read_variable(call_describe, &ctx, "describe");
        if (json) { printf("Describe: %s\n", json); free(json); }
    }

    /* list_collections */
    {
        list_ctx_t ctx = { db };
        char *json = read_variable(call_list, &ctx, "list_collections");
        if (json) { printf("Collections: %s\n", json); free(json); }
    }

    /* search + filter: genre=sys 或 genre=algo，year >= 2000 */
    {
        const float query[DIM] = { 0.12f, 0.18f, 0.30f, 0.40f };
        const char *filter =
            "{\"and\":["
              "{\"or\":[{\"eq\":{\"genre\":\"sys\"}},{\"eq\":{\"genre\":\"algo\"}}]},"
              "{\"range\":{\"year\":{\"min\":2000,\"min_inclusive\":true}}}"
            "]}";
        search_ctx_t ctx = { col, query, DIM, 5, filter, 0 };
        char *json = read_variable(call_search, &ctx, "search");
        if (json) { printf("Search: %s\n", json); free(json); }
    }

    /* query: rating >= 4.8 */
    {
        const char *filter = "{\"range\":{\"rating\":{\"min\":4.8,\"min_inclusive\":true}}}";
        query_ctx_t ctx = { col, filter, 10, 0 };
        char *json = read_variable(call_query, &ctx, "query");
        if (json) { printf("Query: %s\n", json); free(json); }
    }

    /* get: 取回 b1,b3 含 vector */
    {
        const char *get_ids[] = { "b1", "b3" };
        get_ctx_t ctx = { col, get_ids, 2, 1 };
        char *json = read_variable(call_get, &ctx, "get");
        if (json) { printf("Get: %s\n", json); free(json); }
    }

    /* delete b2 */
    {
        const char *del_ids[] = { "b2" };
        status = dotvector_collection_delete(col, del_ids, 1);
        if (status != DOTVECTOR_OK) { print_last_error("delete"); goto fail_col; }
        printf("After delete: count=%lld\n", (long long)dotvector_collection_count(col));
    }

    /* flush + cleanup */
    if (dotvector_database_flush(db) != DOTVECTOR_OK) { print_last_error("flush"); }

    dotvector_collection_free(col);

    /* 删除整个集合 */
    if (dotvector_database_delete_collection(db, collection_name) != DOTVECTOR_OK)
    {
        print_last_error("delete_collection");
    }
    else
    {
        printf("Deleted collection '%s'.\n", collection_name);
    }

    dotvector_database_free(db);
    printf("Done.\n");
    return EXIT_SUCCESS;

fail_col:
    dotvector_collection_free(col);
fail_db:
    dotvector_database_free(db);
    return EXIT_FAILURE;
}
