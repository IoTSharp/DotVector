"""DotVector Python Connector."""

from .native import (
    CollectionInfo,
    DotVectorNativeError,
    Filter,
    NativeCollection,
    NativeCollectionInt64,
    NativeDotVector,
    NativeSearchResult,
    Point,
    ScoredPoint,
    connect,
    connect_remote,
    last_error,
    open,
    version,
)

__version__ = "0.2.0"

__all__ = [
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
