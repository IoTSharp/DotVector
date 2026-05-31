# DotVector Python Connector

This package contains two clients:

- `NativeDotVector`: embedded client that loads the C ABI dynamic library with `ctypes`.

The native client follows the same connector pattern as SonnetDB: `DotVector.Native` is published by the C connector, Python discovers that dynamic library, then calls the stable C ABI with opaque handles and primitive buffers.

## Native Usage (C ABI v0.2)

The native client targets the full C ABI exported from `DotVector.Native` (28 entry
points). Build / publish the native library first:

```powershell
# AOT publish for Windows x64
dotnet publish ../c/native/DotVector.Native -c Release -r win-x64 `
    /p:PublishAot=true /p:NativeLib=Shared
```

Linux:

```bash
dotnet publish ../c/native/DotVector.Native -c Release -r linux-x64 \
    /p:PublishAot=true /p:NativeLib=Shared
```

Run the examples:

```powershell
python examples/quick_start.py        # legacy int64 ABI
python examples/quick_start_full.py   # full ABI: payload + filter + describe
```

If the library is outside the default search path, set one of:

```powershell
set DOTVECTOR_NATIVE_LIBRARY=C:\path\to\DotVector.Native.dll
set DOTVECTOR_NATIVE_LIB_DIR=C:\path\to\native
```

Linux uses `DotVector.Native.so`; macOS uses `DotVector.Native.dylib`.

### High-level API

```python
from dotvector import NativeDotVector, Filter

with NativeDotVector() as db:                    # 临时目录的嵌入式数据库
    coll = db.ensure_collection("books", dimensions=4, metric="Cosine")

    coll.upsert_batch(
        ids=["b1", "b2"],
        vectors=[[0.1, 0.2, 0.3, 0.4], [0.5, 0.5, 0.5, 0.5]],
        payloads=[{"title": "三国演义", "year": 1400}, {"title": "红楼梦", "year": 1791}],
    )

    hits = coll.search(
        [0.1, 0.2, 0.3, 0.4], top_k=5,
        filter=Filter.range("year", min=1300, max=1600),
        include_vector=False,
    )
    for h in hits:
        print(h.id, h.score, h.payload)

    points = coll.query(Filter.eq("title", "红楼梦"), top=10)
    coll.delete("b1")
    db.flush()
```

Remote server mode is no longer provided by DotVector. Use SonnetDB when a service endpoint is needed.

### Filter DSL

All operators map 1:1 to the JSON DSL described in `connectors/c/include/dotvector.h`:

| Helper | JSON |
|--------|------|
| `Filter.eq("a", 1)` | `{"eq": {"a": 1}}` |
| `Filter.ne("a", 1)` | `{"ne": {"a": 1}}` |
| `Filter.range("a", min=1, max=5, max_inclusive=False)` | `{"range": {"a": {"min": 1, "min_inclusive": true, "max": 5, "max_inclusive": false}}}` |
| `Filter.exists("a")` | `{"exists": "a"}` |
| `Filter.missing("a")` | `{"missing": "a"}` |
| `Filter.and_(x, y)` / `Filter.or_(x, y)` / `Filter.not_(x)` | composite |

Pass either a `Filter.*` dict or a raw JSON string to `search`/`query`.

### Legacy int64 ABI

`db.create_collection_i64(...)` / `db.get_collection_i64(...)` returns a
`NativeCollectionInt64` that exposes the v0.1 entry points (`insert(key, vector)`
and `search(vector, top_k)` returning `NativeSearchResult(key, score)`).
