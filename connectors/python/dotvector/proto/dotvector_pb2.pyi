from google.protobuf.internal import containers as _containers
from google.protobuf import descriptor as _descriptor
from google.protobuf import message as _message
from collections.abc import Iterable as _Iterable, Mapping as _Mapping
from typing import ClassVar as _ClassVar, Optional as _Optional, Union as _Union

DESCRIPTOR: _descriptor.FileDescriptor

class ScalarValue(_message.Message):
    __slots__ = ("bool_value", "int_value", "double_value", "string_value")
    BOOL_VALUE_FIELD_NUMBER: _ClassVar[int]
    INT_VALUE_FIELD_NUMBER: _ClassVar[int]
    DOUBLE_VALUE_FIELD_NUMBER: _ClassVar[int]
    STRING_VALUE_FIELD_NUMBER: _ClassVar[int]
    bool_value: bool
    int_value: int
    double_value: float
    string_value: str
    def __init__(self, bool_value: bool = ..., int_value: _Optional[int] = ..., double_value: _Optional[float] = ..., string_value: _Optional[str] = ...) -> None: ...

class UpsertRecord(_message.Message):
    __slots__ = ("id", "vector", "payload")
    class PayloadEntry(_message.Message):
        __slots__ = ("key", "value")
        KEY_FIELD_NUMBER: _ClassVar[int]
        VALUE_FIELD_NUMBER: _ClassVar[int]
        key: str
        value: ScalarValue
        def __init__(self, key: _Optional[str] = ..., value: _Optional[_Union[ScalarValue, _Mapping]] = ...) -> None: ...
    ID_FIELD_NUMBER: _ClassVar[int]
    VECTOR_FIELD_NUMBER: _ClassVar[int]
    PAYLOAD_FIELD_NUMBER: _ClassVar[int]
    id: str
    vector: bytes
    payload: _containers.MessageMap[str, ScalarValue]
    def __init__(self, id: _Optional[str] = ..., vector: _Optional[bytes] = ..., payload: _Optional[_Mapping[str, ScalarValue]] = ...) -> None: ...

class VectorRecord(_message.Message):
    __slots__ = ("id", "vector", "payload")
    class PayloadEntry(_message.Message):
        __slots__ = ("key", "value")
        KEY_FIELD_NUMBER: _ClassVar[int]
        VALUE_FIELD_NUMBER: _ClassVar[int]
        key: str
        value: ScalarValue
        def __init__(self, key: _Optional[str] = ..., value: _Optional[_Union[ScalarValue, _Mapping]] = ...) -> None: ...
    ID_FIELD_NUMBER: _ClassVar[int]
    VECTOR_FIELD_NUMBER: _ClassVar[int]
    PAYLOAD_FIELD_NUMBER: _ClassVar[int]
    id: str
    vector: bytes
    payload: _containers.MessageMap[str, ScalarValue]
    def __init__(self, id: _Optional[str] = ..., vector: _Optional[bytes] = ..., payload: _Optional[_Mapping[str, ScalarValue]] = ...) -> None: ...

class ScoredRecord(_message.Message):
    __slots__ = ("id", "score", "vector", "payload")
    class PayloadEntry(_message.Message):
        __slots__ = ("key", "value")
        KEY_FIELD_NUMBER: _ClassVar[int]
        VALUE_FIELD_NUMBER: _ClassVar[int]
        key: str
        value: ScalarValue
        def __init__(self, key: _Optional[str] = ..., value: _Optional[_Union[ScalarValue, _Mapping]] = ...) -> None: ...
    ID_FIELD_NUMBER: _ClassVar[int]
    SCORE_FIELD_NUMBER: _ClassVar[int]
    VECTOR_FIELD_NUMBER: _ClassVar[int]
    PAYLOAD_FIELD_NUMBER: _ClassVar[int]
    id: str
    score: float
    vector: bytes
    payload: _containers.MessageMap[str, ScalarValue]
    def __init__(self, id: _Optional[str] = ..., score: _Optional[float] = ..., vector: _Optional[bytes] = ..., payload: _Optional[_Mapping[str, ScalarValue]] = ...) -> None: ...

class CollectionInfo(_message.Message):
    __slots__ = ("name", "dimensions", "metric", "record_count")
    NAME_FIELD_NUMBER: _ClassVar[int]
    DIMENSIONS_FIELD_NUMBER: _ClassVar[int]
    METRIC_FIELD_NUMBER: _ClassVar[int]
    RECORD_COUNT_FIELD_NUMBER: _ClassVar[int]
    name: str
    dimensions: int
    metric: str
    record_count: int
    def __init__(self, name: _Optional[str] = ..., dimensions: _Optional[int] = ..., metric: _Optional[str] = ..., record_count: _Optional[int] = ...) -> None: ...

class DatabaseSelector(_message.Message):
    __slots__ = ("database",)
    DATABASE_FIELD_NUMBER: _ClassVar[int]
    database: str
    def __init__(self, database: _Optional[str] = ...) -> None: ...

class PingRequest(_message.Message):
    __slots__ = ()
    def __init__(self) -> None: ...

class PingResponse(_message.Message):
    __slots__ = ("ok",)
    OK_FIELD_NUMBER: _ClassVar[int]
    ok: bool
    def __init__(self, ok: bool = ...) -> None: ...

class CreateCollectionRequest(_message.Message):
    __slots__ = ("name", "dimensions", "metric", "selector")
    NAME_FIELD_NUMBER: _ClassVar[int]
    DIMENSIONS_FIELD_NUMBER: _ClassVar[int]
    METRIC_FIELD_NUMBER: _ClassVar[int]
    SELECTOR_FIELD_NUMBER: _ClassVar[int]
    name: str
    dimensions: int
    metric: str
    selector: DatabaseSelector
    def __init__(self, name: _Optional[str] = ..., dimensions: _Optional[int] = ..., metric: _Optional[str] = ..., selector: _Optional[_Union[DatabaseSelector, _Mapping]] = ...) -> None: ...

class CreateCollectionResponse(_message.Message):
    __slots__ = ()
    def __init__(self) -> None: ...

class DeleteCollectionRequest(_message.Message):
    __slots__ = ("name", "selector")
    NAME_FIELD_NUMBER: _ClassVar[int]
    SELECTOR_FIELD_NUMBER: _ClassVar[int]
    name: str
    selector: DatabaseSelector
    def __init__(self, name: _Optional[str] = ..., selector: _Optional[_Union[DatabaseSelector, _Mapping]] = ...) -> None: ...

class DeleteCollectionResponse(_message.Message):
    __slots__ = ()
    def __init__(self) -> None: ...

class ListCollectionsRequest(_message.Message):
    __slots__ = ("selector",)
    SELECTOR_FIELD_NUMBER: _ClassVar[int]
    selector: DatabaseSelector
    def __init__(self, selector: _Optional[_Union[DatabaseSelector, _Mapping]] = ...) -> None: ...

class ListCollectionsResponse(_message.Message):
    __slots__ = ("collections",)
    COLLECTIONS_FIELD_NUMBER: _ClassVar[int]
    collections: _containers.RepeatedCompositeFieldContainer[CollectionInfo]
    def __init__(self, collections: _Optional[_Iterable[_Union[CollectionInfo, _Mapping]]] = ...) -> None: ...

class UpsertRequest(_message.Message):
    __slots__ = ("collection", "records", "selector")
    COLLECTION_FIELD_NUMBER: _ClassVar[int]
    RECORDS_FIELD_NUMBER: _ClassVar[int]
    SELECTOR_FIELD_NUMBER: _ClassVar[int]
    collection: str
    records: _containers.RepeatedCompositeFieldContainer[UpsertRecord]
    selector: DatabaseSelector
    def __init__(self, collection: _Optional[str] = ..., records: _Optional[_Iterable[_Union[UpsertRecord, _Mapping]]] = ..., selector: _Optional[_Union[DatabaseSelector, _Mapping]] = ...) -> None: ...

class UpsertResponse(_message.Message):
    __slots__ = ("count",)
    COUNT_FIELD_NUMBER: _ClassVar[int]
    count: int
    def __init__(self, count: _Optional[int] = ...) -> None: ...

class DeleteRequest(_message.Message):
    __slots__ = ("collection", "ids", "selector")
    COLLECTION_FIELD_NUMBER: _ClassVar[int]
    IDS_FIELD_NUMBER: _ClassVar[int]
    SELECTOR_FIELD_NUMBER: _ClassVar[int]
    collection: str
    ids: _containers.RepeatedScalarFieldContainer[str]
    selector: DatabaseSelector
    def __init__(self, collection: _Optional[str] = ..., ids: _Optional[_Iterable[str]] = ..., selector: _Optional[_Union[DatabaseSelector, _Mapping]] = ...) -> None: ...

class DeleteResponse(_message.Message):
    __slots__ = ("count",)
    COUNT_FIELD_NUMBER: _ClassVar[int]
    count: int
    def __init__(self, count: _Optional[int] = ...) -> None: ...

class SearchRequest(_message.Message):
    __slots__ = ("collection", "query_vector", "top_k", "include_vector", "filter", "selector")
    COLLECTION_FIELD_NUMBER: _ClassVar[int]
    QUERY_VECTOR_FIELD_NUMBER: _ClassVar[int]
    TOP_K_FIELD_NUMBER: _ClassVar[int]
    INCLUDE_VECTOR_FIELD_NUMBER: _ClassVar[int]
    FILTER_FIELD_NUMBER: _ClassVar[int]
    SELECTOR_FIELD_NUMBER: _ClassVar[int]
    collection: str
    query_vector: bytes
    top_k: int
    include_vector: bool
    filter: bytes
    selector: DatabaseSelector
    def __init__(self, collection: _Optional[str] = ..., query_vector: _Optional[bytes] = ..., top_k: _Optional[int] = ..., include_vector: bool = ..., filter: _Optional[bytes] = ..., selector: _Optional[_Union[DatabaseSelector, _Mapping]] = ...) -> None: ...

class SearchResponse(_message.Message):
    __slots__ = ("hits",)
    HITS_FIELD_NUMBER: _ClassVar[int]
    hits: _containers.RepeatedCompositeFieldContainer[ScoredRecord]
    def __init__(self, hits: _Optional[_Iterable[_Union[ScoredRecord, _Mapping]]] = ...) -> None: ...

class GetRequest(_message.Message):
    __slots__ = ("collection", "ids", "include_vector", "selector")
    COLLECTION_FIELD_NUMBER: _ClassVar[int]
    IDS_FIELD_NUMBER: _ClassVar[int]
    INCLUDE_VECTOR_FIELD_NUMBER: _ClassVar[int]
    SELECTOR_FIELD_NUMBER: _ClassVar[int]
    collection: str
    ids: _containers.RepeatedScalarFieldContainer[str]
    include_vector: bool
    selector: DatabaseSelector
    def __init__(self, collection: _Optional[str] = ..., ids: _Optional[_Iterable[str]] = ..., include_vector: bool = ..., selector: _Optional[_Union[DatabaseSelector, _Mapping]] = ...) -> None: ...

class GetResponse(_message.Message):
    __slots__ = ("records",)
    RECORDS_FIELD_NUMBER: _ClassVar[int]
    records: _containers.RepeatedCompositeFieldContainer[VectorRecord]
    def __init__(self, records: _Optional[_Iterable[_Union[VectorRecord, _Mapping]]] = ...) -> None: ...

class ScrollRequest(_message.Message):
    __slots__ = ("collection", "top", "include_vector", "filter", "selector")
    COLLECTION_FIELD_NUMBER: _ClassVar[int]
    TOP_FIELD_NUMBER: _ClassVar[int]
    INCLUDE_VECTOR_FIELD_NUMBER: _ClassVar[int]
    FILTER_FIELD_NUMBER: _ClassVar[int]
    SELECTOR_FIELD_NUMBER: _ClassVar[int]
    collection: str
    top: int
    include_vector: bool
    filter: bytes
    selector: DatabaseSelector
    def __init__(self, collection: _Optional[str] = ..., top: _Optional[int] = ..., include_vector: bool = ..., filter: _Optional[bytes] = ..., selector: _Optional[_Union[DatabaseSelector, _Mapping]] = ...) -> None: ...

class ScrollResponse(_message.Message):
    __slots__ = ("records",)
    RECORDS_FIELD_NUMBER: _ClassVar[int]
    records: _containers.RepeatedCompositeFieldContainer[VectorRecord]
    def __init__(self, records: _Optional[_Iterable[_Union[VectorRecord, _Mapping]]] = ...) -> None: ...
