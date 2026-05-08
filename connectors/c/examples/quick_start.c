/*
 * DotVector C Connector — QuickStart 示例
 *
 * 该示例演示如何使用 DotVector C ABI（NativeAOT 发布的 DotVector.Native）：
 *   1. 打开（或创建）一个本地嵌入式数据库目录
 *   2. 创建 / 获取一个 int64 主键的 Cosine 向量集合
 *   3. 插入若干 4 维向量
 *   4. 执行 Top-K 相似度搜索
 *   5. 刷盘并释放资源
 *
 * 编译： 由同目录下的 CMakeLists.txt 自动构建为 dotvector_quickstart 可执行文件。
 */

#include "dotvector.h"

#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#define DIMENSIONS 4
#define TOP_K      3

static void print_last_error(const char *prefix)
{
    char buffer[512];
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

int main(void)
{
    /* 打印 Native 库版本 */
    char version[128];
    if (dotvector_version(version, (int32_t)sizeof(version)) > 0)
    {
        printf("DotVector native: %s\n", version);
    }

    /* 1. 打开本地嵌入式数据库目录（不存在则创建） */
    const char *db_path = "./quickstart_c.dvec";
    dotvector_database_t database = dotvector_database_open(db_path);
    if (database == NULL)
    {
        print_last_error("dotvector_database_open failed");
        return EXIT_FAILURE;
    }
    printf("Opened database at %s\n", db_path);

    /* 2. 获取已有集合，否则创建一个新的 */
    const char *collection_name = "books";
    dotvector_collection_t collection = dotvector_collection_get_i64(database, collection_name);
    if (collection == NULL)
    {
        collection = dotvector_collection_create_i64(
            database,
            collection_name,
            DIMENSIONS,
            DOTVECTOR_METRIC_COSINE,
            DOTVECTOR_INDEX_FLAT);
        if (collection == NULL)
        {
            print_last_error("dotvector_collection_create_i64 failed");
            dotvector_database_free(database);
            return EXIT_FAILURE;
        }
        printf("Created collection '%s' (dim=%d, metric=cosine)\n", collection_name, DIMENSIONS);
    }
    else
    {
        printf("Reusing collection '%s'\n", collection_name);
    }

    /* 3. 插入向量 */
    typedef struct
    {
        int64_t key;
        float   vector[DIMENSIONS];
        const char *title;
    } book_t;

    book_t books[] = {
        { 1, { 0.10f, 0.20f, 0.30f, 0.40f }, "深入理解计算机系统" },
        { 2, { 0.20f, 0.10f, 0.40f, 0.30f }, "重构：改善既有代码的设计" },
        { 3, { 0.90f, 0.10f, 0.05f, 0.05f }, "C 程序设计语言" },
        { 4, { 0.05f, 0.05f, 0.10f, 0.90f }, "算法导论" },
    };
    const int book_count = (int)(sizeof(books) / sizeof(books[0]));

    for (int i = 0; i < book_count; i++)
    {
        int32_t status = dotvector_collection_insert_i64(
            collection,
            books[i].key,
            books[i].vector,
            DIMENSIONS);
        if (status != DOTVECTOR_OK)
        {
            print_last_error("dotvector_collection_insert_i64 failed");
            dotvector_collection_free(collection);
            dotvector_database_free(database);
            return EXIT_FAILURE;
        }
    }

    int64_t total = dotvector_collection_count(collection);
    printf("Inserted %d vectors. Collection now contains %lld points.\n",
           book_count, (long long)total);

    /* 4. Top-K 搜索：与 books[0] 的向量相近 */
    float   query[DIMENSIONS] = { 0.10f, 0.20f, 0.30f, 0.45f };
    int64_t out_keys[TOP_K]   = { 0 };
    float   out_scores[TOP_K] = { 0.0f };
    int32_t out_count         = 0;

    int32_t status = dotvector_collection_search_i64(
        collection,
        query,
        DIMENSIONS,
        TOP_K,
        out_keys,
        out_scores,
        &out_count);
    if (status != DOTVECTOR_OK)
    {
        print_last_error("dotvector_collection_search_i64 failed");
        dotvector_collection_free(collection);
        dotvector_database_free(database);
        return EXIT_FAILURE;
    }

    printf("Top-%d results for query [%.2f, %.2f, %.2f, %.2f]:\n",
           out_count, query[0], query[1], query[2], query[3]);
    for (int32_t i = 0; i < out_count; i++)
    {
        const char *title = "(unknown)";
        for (int j = 0; j < book_count; j++)
        {
            if (books[j].key == out_keys[i])
            {
                title = books[j].title;
                break;
            }
        }
        printf("  #%d  key=%lld  score=%.6f  -> %s\n",
               i + 1,
               (long long)out_keys[i],
               (double)out_scores[i],
               title);
    }

    /* 5. 刷盘 + 释放资源 */
    if (dotvector_database_flush(database) != DOTVECTOR_OK)
    {
        print_last_error("dotvector_database_flush failed");
    }

    dotvector_collection_free(collection);
    dotvector_database_free(database);

    printf("Done.\n");
    return EXIT_SUCCESS;
}
