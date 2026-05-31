"""DotVector embedded / native client backed by the C ABI dynamic library.

包装 ``connectors/c/include/dotvector.h`` 暴露的 28 个入口，提供 Pythonic API：

- :class:`NativeDotVector` — 本地嵌入式数据库句柄。
- :class:`NativeCollection` — 字符串主键的集合，支持 payload + filter。
- :class:`NativeCollectionInt64` — 兼容旧 v0.1 ABI 的 int64 主键集合。

Payload / Filter / 结果集统一通过 UTF-8 JSON 文本与 native 层交互（与 dotvector.h 的约定一致）。
"""

from __future__ import annotations

import ctypes
import json
import os
import sys
from ctypes import POINTER, byref, c_char_p, c_float, c_int32, c_int64, c_void_p
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any, Iterable, Mapping, Sequence


# --------------------------------------------------------------------------- #
# Status / Metric / IndexKind 常量（与 dotvector.h 对齐）                     #
# --------------------------------------------------------------------------- #

DOTVECTOR_OK = 0
DOTVECTOR_INVALID_ARGUMENT = -1
DOTVECTOR_NOT_FOUND = -2
DOTVECTOR_BUFFER_TOO_SMALL = -3
DOTVECTOR_FAILED = -100

DOTVECTOR_METRIC_L2 = 0
DOTVECTOR_METRIC_COSINE = 1
DOTVECTOR_METRIC_INNER_PRODUCT = 2
DOTVECTOR_METRIC_HAMMING = 3
DOTVECTOR_METRIC_DOT_PRODUCT = 4

DOTVECTOR_INDEX_FLAT = 0
DOTVECTOR_INDEX_HNSW = 1
DOTVECTOR_INDEX_IVF_FLAT = 2
DOTVECTOR_INDEX_IVF_PQ = 3
DOTVECTOR_INDEX_VAMANA = 4

_NATIVE_STRING_BUFFER_SIZE = 4096
_DEFAULT_VARIABLE_BUFFER = 4096

_LIBRARIES: dict[str, "_NativeLibrary"] = {}
_DLL_DIRECTORY_HANDLES: list[Any] = []

_METRICS: dict[str, int] = {
    "l2": DOTVECTOR_METRIC_L2,
    "cosine": DOTVECTOR_METRIC_COSINE,
    "innerproduct": DOTVECTOR_METRIC_INNER_PRODUCT,
    "inner_product": DOTVECTOR_METRIC_INNER_PRODUCT,
    "hamming": DOTVECTOR_METRIC_HAMMING,
    "dotproduct": DOTVECTOR_METRIC_DOT_PRODUCT,
    "dot_product": DOTVECTOR_METRIC_DOT_PRODUCT,
}

_INDEX_KINDS: dict[str, int] = {
    "flat": DOTVECTOR_INDEX_FLAT,
    "hnsw": DOTVECTOR_INDEX_HNSW,
    "ivfflat": DOTVECTOR_INDEX_IVF_FLAT,
    "ivf_flat": DOTVECTOR_INDEX_IVF_FLAT,
    "ivfpq": DOTVECTOR_INDEX_IVF_PQ,
    "ivf_pq": DOTVECTOR_INDEX_IVF_PQ,
    "vamana": DOTVECTOR_INDEX_VAMANA,
}


# --------------------------------------------------------------------------- #
# 异常 / 数据类                                                                #
# --------------------------------------------------------------------------- #


class DotVectorNativeError(RuntimeError):
    """Raised when the DotVector C ABI returns a non-OK status."""

    def __init__(self, message: str, status: int | None = None) -> None:
        super().__init__(message if status is None else f"[{status}] {message}")
        self.status = status


@dataclass(frozen=True)
class CollectionInfo:
    """``dotvector_collection_describe`` / ``list_collections`` 返回的元数据。"""

    name: str
    dimensions: int
    metric: str
    record_count: int


@dataclass(frozen=True)
class Point:
    """``query`` / ``get`` 返回的记录。"""

    id: str
    payload: dict[str, Any] = field(default_factory=dict)
    vector: list[float] | None = None


@dataclass(frozen=True)
class ScoredPoint:
    """``search`` 返回的命中结果。"""

    id: str
    score: float
    payload: dict[str, Any] = field(default_factory=dict)
    vector: list[float] | None = None


@dataclass(frozen=True)
class NativeSearchResult:
    """旧 v0.1 ABI int64 主键 search 的返回类型。"""

    key: int
    score: float


# --------------------------------------------------------------------------- #
# 工具函数                                                                     #
# --------------------------------------------------------------------------- #


def _normalize_option(value: str | int, mapping: dict[str, int], label: str) -> int:
    if isinstance(value, int):
        return value
    key = value.replace("-", "_").lower()
    if key not in mapping:
        raise ValueError(f"unknown {label}: {value}")
    return mapping[key]


def _encode_utf8(value: str) -> bytes:
    if "\x00" in value:
        raise DotVectorNativeError("strings passed to the native ABI must not contain NUL bytes")
    return value.encode("utf-8")


def _encode_optional_utf8(value: str | None) -> bytes | None:
    if value is None:
        return None
    return _encode_utf8(value)


def _default_library_names() -> list[str]:
    plat = sys.platform
    if plat.startswith("win"):
        return ["DotVector.Native.dll"]
    if plat.startswith("linux"):
        return ["DotVector.Native.so"]
    if plat == "darwin":
        return ["DotVector.Native.dylib"]
    return ["DotVector.Native.dll", "DotVector.Native.so", "DotVector.Native.dylib"]


def _runtime_identifier() -> str:
    plat = sys.platform
    machine = (os.environ.get("PROCESSOR_ARCHITECTURE") or "").lower()
    if not machine:
        # Fall back to a cheap source; avoid platform.machine() which can spawn subprocesses on some setups.
        machine = (os.uname().machine.lower() if hasattr(os, "uname") else "")
    if machine in ("amd64", "x86_64"):
        arch = "x64"
    elif machine in ("arm64", "aarch64"):
        arch = "arm64"
    elif machine in ("x86", "i386", "i686"):
        arch = "x86"
    else:
        arch = machine or "x64"

    if plat.startswith("win"):
        return f"win-{arch}"
    if plat.startswith("linux"):
        return f"linux-{arch}"
    if plat == "darwin":
        return f"osx-{arch}"
    return f"{plat}-{arch}"


def _candidate_library_paths() -> Iterable[Path]:
    env = os.environ.get("DOTVECTOR_NATIVE_LIBRARY")
    if env:
        yield Path(env)

    library_dir = os.environ.get("DOTVECTOR_NATIVE_LIB_DIR")
    if library_dir:
        base_dir = Path(library_dir).expanduser()
        for name in _default_library_names():
            yield base_dir / name

    package_root = Path(__file__).resolve().parent
    repo_root = package_root.parents[2]
    rid = _runtime_identifier()
    search_dirs = [
        package_root,
        package_root / "native",
        repo_root / "artifacts" / "connectors" / "c" / rid,
        repo_root / "artifacts" / "connectors" / "c" / rid / "Release",
        repo_root / "artifacts" / "connectors" / "c" / rid / "native" / rid / "publish",
        repo_root
        / "connectors" / "c" / "native" / "DotVector.Native"
        / "bin" / "Release" / "net10.0" / rid / "native",
        repo_root
        / "connectors" / "c" / "native" / "DotVector.Native"
        / "bin" / "Release" / "net10.0" / rid / "publish",
        repo_root
        / "connectors" / "c" / "native" / "DotVector.Native"
        / "bin" / "Release" / "net10.0",
    ]
    for directory in search_dirs:
        for name in _default_library_names():
            yield directory / name


def _add_dll_directory(directory: Path) -> None:
    if os.name == "nt" and hasattr(os, "add_dll_directory"):
        _DLL_DIRECTORY_HANDLES.append(os.add_dll_directory(str(directory)))


# --------------------------------------------------------------------------- #
# Native library 配置                                                          #
# --------------------------------------------------------------------------- #


class _NativeLibrary:
    def __init__(self, path: str | os.PathLike[str] | None = None) -> None:
        self.path = Path(path) if path is not None else _resolve_library_path(None)
        _add_dll_directory(self.path.parent)
        self.lib = ctypes.CDLL(str(self.path))
        self._configure()

    def _configure(self) -> None:
        lib = self.lib

        # Diagnostics
        lib.dotvector_version.argtypes = [c_char_p, c_int32]
        lib.dotvector_version.restype = c_int32
        lib.dotvector_last_error.argtypes = [c_char_p, c_int32]
        lib.dotvector_last_error.restype = c_int32

        # Database lifecycle
        lib.dotvector_database_create.argtypes = []
        lib.dotvector_database_create.restype = c_void_p
        lib.dotvector_database_open.argtypes = [c_char_p]
        lib.dotvector_database_open.restype = c_void_p
        lib.dotvector_database_connect.argtypes = [c_char_p, c_char_p, c_char_p, c_int32]
        lib.dotvector_database_connect.restype = c_void_p
        lib.dotvector_database_free.argtypes = [c_void_p]
        lib.dotvector_database_free.restype = None
        lib.dotvector_database_flush.argtypes = [c_void_p]
        lib.dotvector_database_flush.restype = c_int32
        lib.dotvector_database_compact.argtypes = [c_void_p]
        lib.dotvector_database_compact.restype = c_int32
        lib.dotvector_database_ping.argtypes = [c_void_p]
        lib.dotvector_database_ping.restype = c_int32

        lib.dotvector_database_list_collections.argtypes = [
            c_void_p, c_char_p, c_int32, POINTER(c_int32),
        ]
        lib.dotvector_database_list_collections.restype = c_int32
        lib.dotvector_database_collection_exists.argtypes = [c_void_p, c_char_p]
        lib.dotvector_database_collection_exists.restype = c_int32
        lib.dotvector_database_create_collection.argtypes = [c_void_p, c_char_p, c_int32, c_int32]
        lib.dotvector_database_create_collection.restype = c_int32
        lib.dotvector_database_ensure_collection.argtypes = [c_void_p, c_char_p, c_int32, c_int32]
        lib.dotvector_database_ensure_collection.restype = c_int32
        lib.dotvector_database_delete_collection.argtypes = [c_void_p, c_char_p]
        lib.dotvector_database_delete_collection.restype = c_int32
        lib.dotvector_database_get_collection.argtypes = [c_void_p, c_char_p]
        lib.dotvector_database_get_collection.restype = c_void_p

        # Collection lifecycle
        lib.dotvector_collection_create_i64.argtypes = [
            c_void_p, c_char_p, c_int32, c_int32, c_int32,
        ]
        lib.dotvector_collection_create_i64.restype = c_void_p
        lib.dotvector_collection_get_i64.argtypes = [c_void_p, c_char_p]
        lib.dotvector_collection_get_i64.restype = c_void_p
        lib.dotvector_collection_free.argtypes = [c_void_p]
        lib.dotvector_collection_free.restype = None
        lib.dotvector_collection_count.argtypes = [c_void_p]
        lib.dotvector_collection_count.restype = c_int64
        lib.dotvector_collection_describe.argtypes = [c_void_p, c_char_p, c_int32, POINTER(c_int32)]
        lib.dotvector_collection_describe.restype = c_int32

        # Legacy int64 entries
        lib.dotvector_collection_insert_i64.argtypes = [
            c_void_p, c_int64, POINTER(c_float), c_int32,
        ]
        lib.dotvector_collection_insert_i64.restype = c_int32
        lib.dotvector_collection_search_i64.argtypes = [
            c_void_p, POINTER(c_float), c_int32, c_int32,
            POINTER(c_int64), POINTER(c_float), POINTER(c_int32),
        ]
        lib.dotvector_collection_search_i64.restype = c_int32

        # Full ABI: string-keyed
        lib.dotvector_collection_upsert.argtypes = [
            c_void_p, c_char_p, POINTER(c_float), c_int32, c_char_p,
        ]
        lib.dotvector_collection_upsert.restype = c_int32
        lib.dotvector_collection_upsert_batch.argtypes = [
            c_void_p, POINTER(c_char_p), c_int32, POINTER(c_float), c_int32, POINTER(c_char_p),
        ]
        lib.dotvector_collection_upsert_batch.restype = c_int32
        lib.dotvector_collection_delete.argtypes = [c_void_p, POINTER(c_char_p), c_int32]
        lib.dotvector_collection_delete.restype = c_int32
        lib.dotvector_collection_get.argtypes = [
            c_void_p, POINTER(c_char_p), c_int32, c_int32,
            c_char_p, c_int32, POINTER(c_int32),
        ]
        lib.dotvector_collection_get.restype = c_int32
        lib.dotvector_collection_search.argtypes = [
            c_void_p, POINTER(c_float), c_int32, c_int32, c_char_p, c_int32,
            c_char_p, c_int32, POINTER(c_int32),
        ]
        lib.dotvector_collection_search.restype = c_int32
        lib.dotvector_collection_query.argtypes = [
            c_void_p, c_char_p, c_int32, c_int32,
            c_char_p, c_int32, POINTER(c_int32),
        ]
        lib.dotvector_collection_query.restype = c_int32

    # -- diagnostics -------------------------------------------------------

    def _copy_native_string(self, func: Any) -> str:
        buffer = ctypes.create_string_buffer(_NATIVE_STRING_BUFFER_SIZE)
        required = int(func(buffer, len(buffer)))
        if required < 0:
            raise DotVectorNativeError(
                self.last_error() or "DotVector native string copy failed.", required
            )
        if required >= len(buffer):
            buffer = ctypes.create_string_buffer(required + 1)
            required = int(func(buffer, len(buffer)))
            if required < 0:
                raise DotVectorNativeError(
                    self.last_error() or "DotVector native string copy failed.", required
                )
        return buffer.value.decode("utf-8")

    def version(self) -> str:
        return self._copy_native_string(self.lib.dotvector_version)

    def last_error(self) -> str:
        try:
            return self._copy_native_string(self.lib.dotvector_last_error)
        except DotVectorNativeError:
            return ""

    def check(self, status: int) -> None:
        status = int(status)
        if status == DOTVECTOR_OK:
            return
        raise DotVectorNativeError(
            self.last_error() or f"DotVector native call failed: {status}", status,
        )

    # -- variable output protocol ------------------------------------------

    def read_variable(self, call: Any) -> str:
        """对 ``BUFFER_TOO_SMALL`` 的入口做 caller-buffer 重试。

        ``call(buffer, length, byref(required))`` 必须遵守 dotvector.h 约定。
        """

        required = c_int32(0)
        buffer = ctypes.create_string_buffer(_DEFAULT_VARIABLE_BUFFER)
        status = int(call(buffer, c_int32(len(buffer)), byref(required)))
        if status == DOTVECTOR_BUFFER_TOO_SMALL:
            buffer = ctypes.create_string_buffer(required.value + 1)
            status = int(call(buffer, c_int32(len(buffer)), byref(required)))
        self.check(status)
        return buffer.value.decode("utf-8")


def _resolve_library_path(library_path: str | os.PathLike[str] | None) -> Path:
    explicit = library_path or os.environ.get("DOTVECTOR_NATIVE_LIBRARY")
    if explicit:
        path = Path(explicit).expanduser()
        if path.is_dir():
            path = path / _default_library_names()[0]
        if path.exists():
            return path.resolve()
        raise FileNotFoundError(f"DotVector native library not found: {path}")

    for candidate in _candidate_library_paths():
        resolved = candidate.expanduser()
        if resolved.exists():
            return resolved.resolve()

    searched = "\n".join(f"  - {candidate}" for candidate in _candidate_library_paths())
    raise FileNotFoundError(
        "Could not find DotVector native library. "
        "Build connectors/c first or set DOTVECTOR_NATIVE_LIBRARY / DOTVECTOR_NATIVE_LIB_DIR.\n"
        "Searched:\n" + searched
    )


def _load_native_library(library_path: str | os.PathLike[str] | None = None) -> _NativeLibrary:
    path = _resolve_library_path(library_path)
    existing = _LIBRARIES.get(str(path))
    if existing is not None:
        return existing
    library = _NativeLibrary(path)
    _LIBRARIES[str(path)] = library
    return library


# --------------------------------------------------------------------------- #
# C 字符串 / 浮点缓冲区辅助                                                    #
# --------------------------------------------------------------------------- #


def _make_cstr_array(items: Sequence[str | None]) -> tuple[ctypes.Array, list[bytes | None]]:
    """构造 ``const char* const*`` 数组；返回数组 + 必须保活的 bytes 列表。"""

    encoded: list[bytes | None] = [_encode_optional_utf8(s) for s in items]
    array = (c_char_p * len(items))(*[b if b is not None else None for b in encoded])
    return array, encoded  # encoded keeps the bytes alive across the call


def _flatten_vectors(vectors: Sequence[Sequence[float]], dimensions: int) -> ctypes.Array:
    flat = (c_float * (len(vectors) * dimensions))()
    offset = 0
    for vector in vectors:
        if len(vector) != dimensions:
            raise ValueError(f"expected {dimensions} dimensions, got {len(vector)}")
        for value in vector:
            flat[offset] = float(value)
            offset += 1
    return flat


def _vector_array(vector: Sequence[float], dimensions: int) -> ctypes.Array:
    if len(vector) != dimensions:
        raise ValueError(f"expected {dimensions} dimensions, got {len(vector)}")
    return (c_float * dimensions)(*[float(v) for v in vector])


def _normalize_ids(ids: str | Sequence[str]) -> list[str]:
    if isinstance(ids, str):
        return [ids]
    return list(ids)


# --------------------------------------------------------------------------- #
# Filter 构建（与 dotvector.h DSL 对齐）                                       #
# --------------------------------------------------------------------------- #


def _filter_to_json(value: Mapping[str, Any] | str | None) -> str | None:
    if value is None:
        return None
    if isinstance(value, str):
        return value
    return json.dumps(value, ensure_ascii=False)


class Filter:
    """便捷构造 Filter JSON 的工厂方法集合。"""

    @staticmethod
    def eq(field: str, value: Any) -> dict[str, Any]:
        return {"eq": {field: value}}

    @staticmethod
    def ne(field: str, value: Any) -> dict[str, Any]:
        return {"ne": {field: value}}

    @staticmethod
    def range(
        field: str,
        *,
        min: Any = None,
        max: Any = None,
        min_inclusive: bool = True,
        max_inclusive: bool = True,
    ) -> dict[str, Any]:
        body: dict[str, Any] = {}
        if min is not None:
            body["min"] = min
            body["min_inclusive"] = min_inclusive
        if max is not None:
            body["max"] = max
            body["max_inclusive"] = max_inclusive
        if not body:
            raise ValueError("Filter.range requires at least one of min/max")
        return {"range": {field: body}}

    @staticmethod
    def exists(field: str) -> dict[str, Any]:
        return {"exists": field}

    @staticmethod
    def missing(field: str) -> dict[str, Any]:
        return {"missing": field}

    @staticmethod
    def and_(*clauses: Mapping[str, Any]) -> dict[str, Any]:
        return {"and": list(clauses)}

    @staticmethod
    def or_(*clauses: Mapping[str, Any]) -> dict[str, Any]:
        return {"or": list(clauses)}

    @staticmethod
    def not_(clause: Mapping[str, Any]) -> dict[str, Any]:
        return {"not": clause}


# --------------------------------------------------------------------------- #
# NativeDotVector — 数据库句柄                                                 #
# --------------------------------------------------------------------------- #


class NativeDotVector:
    """本地嵌入式 DotVector 数据库的 ctypes 包装。

    构造方式：

    - ``NativeDotVector()`` — 临时目录里的嵌入式数据库（``dotvector_database_create``）
    - ``NativeDotVector(path)`` / ``NativeDotVector.embedded(path)`` — 打开本地 ``.dvec`` 目录
    """

    def __init__(
        self,
        path: str | os.PathLike[str] | None = None,
        *,
        library_path: str | os.PathLike[str] | None = None,
        _handle: int | None = None,
    ) -> None:
        self._native = _load_native_library(library_path)
        self._collections: list["_BaseNativeCollection"] = []
        if _handle is not None:
            self._database = int(_handle)
            return
        self._database = self._open_database(path)

    # -- factories ---------------------------------------------------------

    @classmethod
    def embedded(
        cls,
        path: str | os.PathLike[str] | None = None,
        *,
        library_path: str | os.PathLike[str] | None = None,
    ) -> "NativeDotVector":
        """嵌入式数据库；``path=None`` 表示临时目录。"""

        return cls(path, library_path=library_path)

    @classmethod
    def connect_remote(
        cls,
        endpoint: str,
        *,
        database: str | None = None,
        api_key: str | None = None,
        use_proxy: bool = False,
        library_path: str | os.PathLike[str] | None = None,
    ) -> "NativeDotVector":
        """旧版远程连接入口。DotVector 独立服务端模式已删除。"""

        _ = endpoint, database, api_key, use_proxy, library_path
        raise DotVectorNativeError(
            "DotVector remote server mode has been removed. Use NativeDotVector.embedded(path) for local databases, or use SonnetDB when a service endpoint is required."
        )

    # -- properties --------------------------------------------------------

    @property
    def version(self) -> str:
        return self._native.version()

    @property
    def library_path(self) -> Path:
        return self._native.path

    @property
    def handle(self) -> int:
        return self._database

    # -- internal ----------------------------------------------------------

    def _open_database(self, path: str | os.PathLike[str] | None) -> int:
        if path is None:
            handle = self._native.lib.dotvector_database_create()
        else:
            handle = self._native.lib.dotvector_database_open(_encode_utf8(os.fspath(path)))
        if not handle:
            raise DotVectorNativeError(
                self._native.last_error() or "dotvector_database open failed."
            )
        return int(handle)

    def _require_handle(self) -> int:
        if not self._database:
            raise DotVectorNativeError("NativeDotVector instance is closed.")
        return self._database

    # -- maintenance -------------------------------------------------------

    def ping(self) -> bool:
        result = int(self._native.lib.dotvector_database_ping(self._require_handle()))
        if result < 0:
            self._native.check(result)
        return result == 1

    def flush(self) -> None:
        self._native.check(self._native.lib.dotvector_database_flush(self._require_handle()))

    def compact(self) -> None:
        self._native.check(self._native.lib.dotvector_database_compact(self._require_handle()))

    # -- collection management --------------------------------------------

    def list_collections(self) -> list[CollectionInfo]:
        handle = self._require_handle()
        json_text = self._native.read_variable(
            lambda buf, length, req: self._native.lib.dotvector_database_list_collections(
                handle, buf, length, req
            )
        )
        items = json.loads(json_text) or []
        return [self._collection_info(item) for item in items]

    def collection_exists(self, name: str) -> bool:
        result = int(self._native.lib.dotvector_database_collection_exists(
            self._require_handle(), _encode_utf8(name)
        ))
        if result < 0:
            self._native.check(result)
        return result == 1

    def create_collection(
        self,
        name: str,
        *,
        dimensions: int,
        metric: str | int = "Cosine",
    ) -> "NativeCollection":
        """``dotvector_database_create_collection`` + ``get_collection``。"""

        self._native.check(self._native.lib.dotvector_database_create_collection(
            self._require_handle(),
            _encode_utf8(name),
            c_int32(int(dimensions)),
            c_int32(_normalize_option(metric, _METRICS, "metric")),
        ))
        return self.get_collection(name, dimensions=dimensions)

    def ensure_collection(
        self,
        name: str,
        *,
        dimensions: int,
        metric: str | int = "Cosine",
    ) -> "NativeCollection":
        """``dotvector_database_ensure_collection`` —— 不存在即创建，存在直接返回。"""

        self._native.check(self._native.lib.dotvector_database_ensure_collection(
            self._require_handle(),
            _encode_utf8(name),
            c_int32(int(dimensions)),
            c_int32(_normalize_option(metric, _METRICS, "metric")),
        ))
        return self.get_collection(name, dimensions=dimensions)

    def delete_collection(self, name: str) -> None:
        self._native.check(self._native.lib.dotvector_database_delete_collection(
            self._require_handle(), _encode_utf8(name)
        ))

    def get_collection(
        self,
        name: str,
        *,
        dimensions: int | None = None,
    ) -> "NativeCollection":
        """获取字符串主键集合句柄；不发请求、不校验存在性。"""

        handle = self._native.lib.dotvector_database_get_collection(
            self._require_handle(), _encode_utf8(name)
        )
        if not handle:
            raise DotVectorNativeError(
                self._native.last_error() or "dotvector_database_get_collection failed."
            )
        collection = NativeCollection(self._native, int(handle), name, dimensions or 0)
        self._collections.append(collection)
        return collection

    # -- legacy int64 ABI --------------------------------------------------

    def create_collection_i64(
        self,
        name: str,
        *,
        dimensions: int,
        metric: str | int = "Cosine",
        index_kind: str | int = "Flat",
    ) -> "NativeCollectionInt64":
        """旧 v0.1 ABI：int64 主键集合（``dotvector_collection_create_i64``）。"""

        handle = self._native.lib.dotvector_collection_create_i64(
            self._require_handle(),
            _encode_utf8(name),
            c_int32(int(dimensions)),
            c_int32(_normalize_option(metric, _METRICS, "metric")),
            c_int32(_normalize_option(index_kind, _INDEX_KINDS, "index kind")),
        )
        if not handle:
            raise DotVectorNativeError(
                self._native.last_error() or "dotvector_collection_create_i64 failed."
            )
        collection = NativeCollectionInt64(self._native, int(handle), name, dimensions)
        self._collections.append(collection)
        return collection

    def get_collection_i64(
        self,
        name: str,
        *,
        dimensions: int | None = None,
    ) -> "NativeCollectionInt64":
        handle = self._native.lib.dotvector_collection_get_i64(
            self._require_handle(), _encode_utf8(name)
        )
        if not handle:
            raise DotVectorNativeError(
                self._native.last_error() or "dotvector_collection_get_i64 failed."
            )
        collection = NativeCollectionInt64(self._native, int(handle), name, dimensions or 0)
        self._collections.append(collection)
        return collection

    # -- lifecycle ---------------------------------------------------------

    def close(self) -> None:
        if not self._database:
            return
        for collection in self._collections:
            try:
                collection.close()
            except Exception:
                pass
        self._collections.clear()
        self._native.lib.dotvector_database_free(self._database)
        self._database = 0

    def __enter__(self) -> "NativeDotVector":
        return self

    def __exit__(self, *args: Any) -> None:
        self.close()

    def __del__(self) -> None:
        try:
            self.close()
        except Exception:
            pass

    # -- helpers -----------------------------------------------------------

    @staticmethod
    def _collection_info(payload: Mapping[str, Any]) -> CollectionInfo:
        return CollectionInfo(
            name=str(payload.get("name", "")),
            dimensions=int(payload.get("dimensions", 0)),
            metric=str(payload.get("metric", "")),
            record_count=int(payload.get("record_count", 0)),
        )


# --------------------------------------------------------------------------- #
# Collection 公共基类                                                          #
# --------------------------------------------------------------------------- #


class _BaseNativeCollection:
    def __init__(
        self,
        native: _NativeLibrary,
        handle: int,
        name: str,
        dimensions: int,
    ) -> None:
        self._native = native
        self._handle = int(handle)
        self.name = name
        self.dimensions = int(dimensions)

    @property
    def handle(self) -> int:
        return self._handle

    @property
    def count(self) -> int:
        value = int(self._native.lib.dotvector_collection_count(self._require_handle()))
        if value < 0:
            self._native.check(value)
        return value

    def _require_handle(self) -> int:
        if not self._handle:
            raise DotVectorNativeError("NativeCollection handle is closed.")
        return self._handle

    def close(self) -> None:
        if not self._handle:
            return
        self._native.lib.dotvector_collection_free(self._handle)
        self._handle = 0

    def __enter__(self):  # type: ignore[no-untyped-def]
        return self

    def __exit__(self, *args: Any) -> None:
        self.close()

    def __del__(self) -> None:
        try:
            self.close()
        except Exception:
            pass


# --------------------------------------------------------------------------- #
# NativeCollection — 字符串主键 + payload + filter                             #
# --------------------------------------------------------------------------- #


class NativeCollection(_BaseNativeCollection):
    """字符串主键集合，对应 dotvector.h 的全功能入口。"""

    # -- describe ----------------------------------------------------------

    def describe(self) -> CollectionInfo:
        handle = self._require_handle()
        json_text = self._native.read_variable(
            lambda buf, length, req: self._native.lib.dotvector_collection_describe(
                handle, buf, length, req
            )
        )
        info = NativeDotVector._collection_info(json.loads(json_text))
        if self.dimensions <= 0 and info.dimensions > 0:
            self.dimensions = info.dimensions
        return info

    # -- write -------------------------------------------------------------

    def upsert(
        self,
        id: str,
        vector: Sequence[float],
        payload: Mapping[str, Any] | None = None,
    ) -> None:
        if self.dimensions <= 0:
            self.dimensions = len(vector)
        array = _vector_array(vector, self.dimensions)
        payload_json = json.dumps(dict(payload), ensure_ascii=False) if payload else None
        self._native.check(self._native.lib.dotvector_collection_upsert(
            self._require_handle(),
            _encode_utf8(id),
            array,
            c_int32(self.dimensions),
            _encode_optional_utf8(payload_json),
        ))

    def upsert_batch(
        self,
        ids: Sequence[str],
        vectors: Sequence[Sequence[float]],
        payloads: Sequence[Mapping[str, Any] | None] | None = None,
    ) -> None:
        if len(ids) != len(vectors):
            raise ValueError(
                f"ids length ({len(ids)}) must match vectors length ({len(vectors)})"
            )
        if payloads is not None and len(payloads) != len(ids):
            raise ValueError(
                f"payloads length ({len(payloads)}) must match ids length ({len(ids)})"
            )
        if not ids:
            return

        if self.dimensions <= 0:
            self.dimensions = len(vectors[0])

        ids_array, _ids_keep = _make_cstr_array(list(ids))
        flat = _flatten_vectors(vectors, self.dimensions)

        payloads_array: ctypes.Array | None = None
        _payload_keep: list[bytes | None] | None = None
        if payloads is not None:
            payload_strs = [
                json.dumps(dict(p), ensure_ascii=False) if p else None for p in payloads
            ]
            payloads_array, _payload_keep = _make_cstr_array(payload_strs)

        self._native.check(self._native.lib.dotvector_collection_upsert_batch(
            self._require_handle(),
            ids_array,
            c_int32(len(ids)),
            flat,
            c_int32(self.dimensions),
            payloads_array if payloads_array is not None else ctypes.cast(None, POINTER(c_char_p)),
        ))

    def delete(self, ids: str | Sequence[str]) -> None:
        id_list = _normalize_ids(ids)
        if not id_list:
            return
        ids_array, _keep = _make_cstr_array(id_list)
        self._native.check(self._native.lib.dotvector_collection_delete(
            self._require_handle(), ids_array, c_int32(len(id_list)),
        ))

    # -- read --------------------------------------------------------------

    def get(
        self,
        ids: str | Sequence[str],
        *,
        include_vector: bool = False,
    ) -> list[Point]:
        id_list = _normalize_ids(ids)
        if not id_list:
            return []
        ids_array, _keep = _make_cstr_array(id_list)
        handle = self._require_handle()
        json_text = self._native.read_variable(
            lambda buf, length, req: self._native.lib.dotvector_collection_get(
                handle, ids_array, c_int32(len(id_list)),
                c_int32(1 if include_vector else 0),
                buf, length, req,
            )
        )
        return [_parse_point(item) for item in json.loads(json_text) or []]

    def search(
        self,
        query: Sequence[float],
        *,
        top_k: int = 10,
        filter: Mapping[str, Any] | str | None = None,
        include_vector: bool = False,
    ) -> list[ScoredPoint]:
        if top_k <= 0:
            raise ValueError("top_k must be positive")
        if self.dimensions <= 0:
            self.dimensions = len(query)
        array = _vector_array(query, self.dimensions)
        filter_json = _filter_to_json(filter)
        handle = self._require_handle()
        json_text = self._native.read_variable(
            lambda buf, length, req: self._native.lib.dotvector_collection_search(
                handle, array, c_int32(self.dimensions), c_int32(top_k),
                _encode_optional_utf8(filter_json),
                c_int32(1 if include_vector else 0),
                buf, length, req,
            )
        )
        return [_parse_scored_point(item) for item in json.loads(json_text) or []]

    def query(
        self,
        filter: Mapping[str, Any] | str,
        *,
        top: int = 100,
        include_vector: bool = False,
    ) -> list[Point]:
        if top <= 0:
            raise ValueError("top must be positive")
        filter_json = _filter_to_json(filter)
        if filter_json is None:
            raise ValueError("query() requires a non-empty filter")
        handle = self._require_handle()
        json_text = self._native.read_variable(
            lambda buf, length, req: self._native.lib.dotvector_collection_query(
                handle, _encode_utf8(filter_json), c_int32(top),
                c_int32(1 if include_vector else 0),
                buf, length, req,
            )
        )
        return [_parse_point(item) for item in json.loads(json_text) or []]


# --------------------------------------------------------------------------- #
# NativeCollectionInt64 — v0.1 ABI 兼容                                        #
# --------------------------------------------------------------------------- #


class NativeCollectionInt64(_BaseNativeCollection):
    """v0.1 ABI 的 int64 主键集合（``dotvector_collection_*_i64``）。"""

    def insert(self, key: int, vector: Sequence[float]) -> None:
        if self.dimensions <= 0:
            self.dimensions = len(vector)
        array = _vector_array(vector, self.dimensions)
        self._native.check(self._native.lib.dotvector_collection_insert_i64(
            self._require_handle(), c_int64(int(key)), array, c_int32(self.dimensions),
        ))

    def search(self, query: Sequence[float], top_k: int = 10) -> list[NativeSearchResult]:
        if top_k <= 0:
            raise ValueError("top_k must be positive")
        if self.dimensions <= 0:
            self.dimensions = len(query)
        array = _vector_array(query, self.dimensions)
        keys = (c_int64 * top_k)()
        scores = (c_float * top_k)()
        count = c_int32()
        self._native.check(self._native.lib.dotvector_collection_search_i64(
            self._require_handle(), array, c_int32(self.dimensions), c_int32(top_k),
            keys, scores, byref(count),
        ))
        return [
            NativeSearchResult(key=int(keys[i]), score=float(scores[i]))
            for i in range(count.value)
        ]


# --------------------------------------------------------------------------- #
# JSON 解析辅助                                                                #
# --------------------------------------------------------------------------- #


def _parse_point(item: Mapping[str, Any]) -> Point:
    vector = item.get("vector")
    return Point(
        id=str(item.get("id", "")),
        payload=dict(item.get("payload") or {}),
        vector=[float(v) for v in vector] if vector is not None else None,
    )


def _parse_scored_point(item: Mapping[str, Any]) -> ScoredPoint:
    vector = item.get("vector")
    return ScoredPoint(
        id=str(item.get("id", "")),
        score=float(item.get("score", 0.0)),
        payload=dict(item.get("payload") or {}),
        vector=[float(v) for v in vector] if vector is not None else None,
    )


# --------------------------------------------------------------------------- #
# 模块级便捷入口                                                                #
# --------------------------------------------------------------------------- #


def connect(
    path: str | os.PathLike[str] | None = None,
    *,
    library_path: str | os.PathLike[str] | None = None,
) -> NativeDotVector:
    """打开嵌入式数据库；``path=None`` 表示临时目录。"""

    return NativeDotVector(path, library_path=library_path)


open = connect


def connect_remote(
    endpoint: str,
    *,
    database: str | None = None,
    api_key: str | None = None,
    use_proxy: bool = False,
    library_path: str | os.PathLike[str] | None = None,
) -> NativeDotVector:
    """旧版远程连接入口。DotVector 独立服务端模式已删除。"""

    return NativeDotVector.connect_remote(
        endpoint,
        database=database,
        api_key=api_key,
        use_proxy=use_proxy,
        library_path=library_path,
    )


def version(*, library_path: str | os.PathLike[str] | None = None) -> str:
    return _load_native_library(library_path).version()


def last_error(*, library_path: str | os.PathLike[str] | None = None) -> str:
    return _load_native_library(library_path).last_error()


__all__ = [
    "DOTVECTOR_OK",
    "DOTVECTOR_INVALID_ARGUMENT",
    "DOTVECTOR_NOT_FOUND",
    "DOTVECTOR_BUFFER_TOO_SMALL",
    "DOTVECTOR_FAILED",
    "DOTVECTOR_INDEX_FLAT",
    "DOTVECTOR_INDEX_HNSW",
    "DOTVECTOR_INDEX_IVF_FLAT",
    "DOTVECTOR_INDEX_IVF_PQ",
    "DOTVECTOR_INDEX_VAMANA",
    "DOTVECTOR_METRIC_COSINE",
    "DOTVECTOR_METRIC_DOT_PRODUCT",
    "DOTVECTOR_METRIC_HAMMING",
    "DOTVECTOR_METRIC_INNER_PRODUCT",
    "DOTVECTOR_METRIC_L2",
    "CollectionInfo",
    "DotVectorNativeError",
    "Filter",
    "NativeCollection",
    "NativeCollectionInt64",
    "NativeDotVector",
    "NativeSearchResult",
    "Point",
    "ScoredPoint",
    "connect",
    "connect_remote",
    "last_error",
    "open",
    "version",
]
