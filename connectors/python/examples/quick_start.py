"""Quick start mirror of connectors/c/examples/quick_start.c (legacy int64 ABI)."""

from __future__ import annotations

from dotvector import NativeDotVector, version


def main() -> None:
    print("DotVector native version:", version())
    with NativeDotVector() as db:
        coll = db.create_collection_i64(
            "demo_i64", dimensions=3, metric="Cosine", index_kind="Flat"
        )
        coll.insert(1, [0.10, 0.20, 0.30])
        coll.insert(2, [0.10, 0.21, 0.29])
        coll.insert(3, [0.90, 0.10, 0.05])

        hits = coll.search([0.10, 0.20, 0.30], top_k=2)
        for hit in hits:
            print(f"  key={hit.key} score={hit.score:.4f}")

        print(f"count={coll.count}")


if __name__ == "__main__":
    main()
