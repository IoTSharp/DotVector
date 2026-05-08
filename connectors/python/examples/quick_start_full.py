"""Full ABI example — mirror of connectors/c/examples/quick_start_full.c.

演示字符串主键、payload、Filter DSL（AND / OR / range）、describe 与 list_collections。
"""

from __future__ import annotations

from dotvector import Filter, NativeDotVector, version


COLLECTION = "books_full"


def main() -> None:
    print("DotVector native version:", version())

    with NativeDotVector() as db:
        print("ping:", db.ping())

        coll = db.ensure_collection(COLLECTION, dimensions=4, metric="Cosine")

        coll.upsert_batch(
            ids=["b1", "b2", "b3", "b4"],
            vectors=[
                [0.10, 0.20, 0.30, 0.40],
                [0.11, 0.19, 0.31, 0.39],
                [0.90, 0.10, 0.05, 0.02],
                [0.50, 0.50, 0.50, 0.50],
            ],
            payloads=[
                {"title": "三国演义", "author": "罗贯中", "year": 1400, "rating": 4.8},
                {"title": "水浒传", "author": "施耐庵", "year": 1380, "rating": 4.6},
                {"title": "西游记", "author": "吴承恩", "year": 1592, "rating": 4.9},
                {"title": "红楼梦", "author": "曹雪芹", "year": 1791, "rating": 5.0},
            ],
        )

        info = coll.describe()
        print(f"describe: name={info.name} dim={info.dimensions} "
              f"metric={info.metric} count={info.record_count}")

        print("collections in database:")
        for entry in db.list_collections():
            print(f"  - {entry.name} (dim={entry.dimensions}, "
                  f"metric={entry.metric}, records={entry.record_count})")

        # AND/OR 复合 filter
        complex_filter = Filter.and_(
            Filter.range("year", min=1300, max=1600),
            Filter.or_(
                Filter.eq("author", "罗贯中"),
                Filter.eq("author", "吴承恩"),
            ),
        )
        print("search top 3 with AND/OR filter:")
        for hit in coll.search(
            [0.10, 0.20, 0.30, 0.40], top_k=3, filter=complex_filter,
        ):
            print(f"  id={hit.id} score={hit.score:.4f} payload={hit.payload}")

        # query by range filter
        print("query rating >= 4.8:")
        for point in coll.query(Filter.range("rating", min=4.8), top=10):
            print(f"  id={point.id} payload={point.payload}")

        # get with vectors
        print("get b1, b4 with vector:")
        for point in coll.get(["b1", "b4"], include_vector=True):
            print(f"  id={point.id} vector={point.vector} payload={point.payload}")

        coll.delete("b3")
        print(f"after delete b3 count={coll.count}")

        db.flush()
        db.delete_collection(COLLECTION)
        print("collection dropped.")


if __name__ == "__main__":
    main()
