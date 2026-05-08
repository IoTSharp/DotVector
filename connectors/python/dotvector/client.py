"""
DotVector Python Client — gRPC-based client for DotVector vector database.

API modeled after the Tencent Cloud VectorDB HTTP SDK for familiarity.
"""

from __future__ import annotations

import struct
from dataclasses import dataclass, field
from typing import Any, Optional

import grpc

from .proto import (
    ScalarValue,
    UpsertRecord,
    CreateCollectionRequest,
    DeleteCollectionRequest,
    ListCollectionsRequest,
    UpsertRequest,
    DeleteRequest,
    SearchRequest,
    GetRequest,
    VectorServiceStub,
)


def _vector_to_bytes(vec: list[float]) -> bytes:
    """Pack a float list into float32 little-endian bytes."""
    return struct.pack(f"<{len(vec)}f", *vec)


def _bytes_to_vector(data: bytes) -> list[float]:
    """Unpack float32 LE bytes back to a float list."""
    n = len(data) // 4
    return list(struct.unpack(f"<{n}f", data))


def _scalar_value(value: Any) -> ScalarValue:
    """Convert a Python value to a protobuf ScalarValue."""
    if isinstance(value, bool):
        return ScalarValue(bool_value=value)
    elif isinstance(value, int):
        return ScalarValue(int_value=value)
    elif isinstance(value, float):
        return ScalarValue(double_value=value)
    elif isinstance(value, str):
        return ScalarValue(string_value=value)
    else:
        return ScalarValue(string_value=str(value))


def _scalar_to_python(sv: ScalarValue) -> Any:
    """Convert a ScalarValue back to a Python value."""
    kind = sv.WhichOneof("value")
    if kind is None:
        return None
    return getattr(sv, kind)


@dataclass
class SearchResult:
    """A single search hit returned by `search`."""

    id: str
    score: float
    vector: Optional[list[float]] = None
    payload: dict[str, Any] = field(default_factory=dict)


@dataclass
class CollectionInfo:
    """Metadata about a collection."""

    name: str
    dimensions: int
    metric: str
    record_count: int


class DotVectorClient:
    """gRPC client for DotVector vector database.

    Connects to a running DotVector gRPC server (default port 5180).

    Example::

        client = DotVectorClient("localhost:5180")
        # Or select a named server database:
        client = DotVectorClient("localhost:5180", database="tenant_a")
        client.create_collection("books", dimension=3, metric="Cosine")
        client.upsert("books", [
            {"id": "0001", "vector": [0.21, 0.23, 0.21], "bookName": "三国演义"},
        ])
        results = client.search("books", vectors=[[0.31, 0.43, 0.21]], top_k=3)
    """

    def __init__(
        self,
        address: str = "localhost:5180",
        *,
        database: str | None = None,
        timeout: float = 30.0,
        secure: bool = False,
    ):
        """Connect to a DotVector gRPC server.

        Args:
            address: ``host:port`` of the DotVector server.
            database: Server database name. If omitted, the server uses ``default``.
            timeout: RPC timeout in seconds.
            secure: If True, use TLS.
        """
        if secure:
            self._channel = grpc.secure_channel(address, grpc.ssl_channel_credentials())
        else:
            self._channel = grpc.insecure_channel(address)
        self._stub = VectorServiceStub(self._channel)
        self._database = database.strip() if database and database.strip() else ""
        self._timeout = timeout

    def close(self):
        """Close the underlying gRPC channel."""
        self._channel.close()

    def __enter__(self):
        return self

    def __exit__(self, *args):
        self.close()

    # ------------------------------------------------------------------
    # Ping
    # ------------------------------------------------------------------

    def ping(self) -> bool:
        """Check server connectivity."""
        from .proto import PingRequest

        rsp = self._stub.Ping(PingRequest(), timeout=self._timeout)
        return rsp.ok

    def _selector(self):
        """Return the optional database selector protobuf."""
        from .proto import DatabaseSelector

        return DatabaseSelector(database=self._database)

    # ------------------------------------------------------------------
    # Collection management
    # ------------------------------------------------------------------

    def create_collection(
        self,
        name: str,
        *,
        dimension: int,
        metric: str = "Cosine",
    ) -> None:
        """Create a vector collection.

        Args:
            name: Collection name.
            dimension: Vector dimension.
            metric: Distance metric — ``"L2"``, ``"Cosine"``, ``"InnerProduct"``,
                    ``"DotProduct"``, or ``"Hamming"``.
        """
        req = CreateCollectionRequest(
            name=name,
            dimensions=dimension,
            metric=metric,
            selector=self._selector(),
        )
        self._stub.CreateCollection(req, timeout=self._timeout)

    def delete_collection(self, name: str) -> None:
        """Delete a collection and all its data."""
        req = DeleteCollectionRequest(name=name, selector=self._selector())
        self._stub.DeleteCollection(req, timeout=self._timeout)

    def list_collections(self) -> list[CollectionInfo]:
        """List all collections."""
        req = ListCollectionsRequest(selector=self._selector())
        rsp = self._stub.ListCollections(req, timeout=self._timeout)
        return [
            CollectionInfo(
                name=c.name,
                dimensions=c.dimensions,
                metric=c.metric,
                record_count=c.record_count,
            )
            for c in rsp.collections
        ]

    # ------------------------------------------------------------------
    # Document operations
    # ------------------------------------------------------------------

    def upsert(
        self,
        collection: str,
        documents: list[dict[str, Any]],
    ) -> int:
        """Insert or update documents in a collection.

        Each document must contain:
        - ``"id"`` (str): Unique document identifier.
        - ``"vector"`` (list[float]): Vector embedding.

        Any additional fields are stored as scalar payload.

        Returns the number of documents upserted.
        """
        records = []
        for doc in documents:
            doc_id = doc["id"]
            vec = doc["vector"]
            payload = {}
            for key, value in doc.items():
                if key in ("id", "vector"):
                    continue
                payload[key] = _scalar_value(value)
            records.append(
                UpsertRecord(
                    id=str(doc_id),
                    vector=_vector_to_bytes(vec),
                    payload=payload,
                )
            )
        req = UpsertRequest(collection=collection, records=records, selector=self._selector())
        rsp = self._stub.Upsert(req, timeout=self._timeout)
        return rsp.count

    def delete(self, collection: str, ids: list[str]) -> int:
        """Delete documents by ID.

        Returns the number of documents deleted.
        """
        req = DeleteRequest(collection=collection, ids=[str(i) for i in ids], selector=self._selector())
        rsp = self._stub.Delete(req, timeout=self._timeout)
        return rsp.count

    # ------------------------------------------------------------------
    # Search
    # ------------------------------------------------------------------

    def search(
        self,
        collection: str,
        *,
        vectors: list[list[float]],
        top_k: int = 10,
        include_vector: bool = False,
    ) -> list[list[SearchResult]]:
        """Search by one or more query vectors (batch).

        Returns a list of result lists, one per query vector, each containing
        up to ``top_k`` scored hits.

        **Filter note**: The gRPC wire format uses DotVector's internal
        ``FilterCodec`` for binary-encoded filters.  String filter expressions
        following the Tencent Cloud / MongoDB-style syntax are **not** currently
        supported by this Python client.  Use `get()` + client-side filtering,
        or pass ``filter=b""`` (no filter) which is the default.
        """
        all_results: list[list[SearchResult]] = []
        for qv in vectors:
            req = SearchRequest(
                collection=collection,
                query_vector=_vector_to_bytes(qv),
                top_k=top_k,
                include_vector=include_vector,
                selector=self._selector(),
            )
            rsp = self._stub.Search(req, timeout=self._timeout)
            results = []
            for hit in rsp.hits:
                payload = {k: _scalar_to_python(v) for k, v in hit.payload.items()}
                vec = None
                if include_vector and hit.vector:
                    vec = _bytes_to_vector(hit.vector)
                results.append(
                    SearchResult(id=hit.id, score=hit.score, vector=vec, payload=payload)
                )
            all_results.append(results)
        return all_results

    def search_by_id(
        self,
        collection: str,
        *,
        document_ids: list[str],
        top_k: int = 10,
        include_vector: bool = False,
    ) -> list[list[SearchResult]]:
        """Search using existing document vectors as query anchors.

        Retrieves each document's vector first, then uses it to search.
        Returns one result list per document ID.
        """
        records = self.get(collection, ids=document_ids, include_vector=True)
        vectors = {}
        for r in records:
            if r.vector is not None:
                vectors[r.id] = r.vector
            else:
                vectors[r.id] = []  # fallback (should not happen with include_vector=True)

        all_results: list[list[SearchResult]] = []
        for doc_id in document_ids:
            qv = vectors.get(doc_id)
            if qv is None:
                all_results.append([])
                continue
            req = SearchRequest(
                collection=collection,
                query_vector=_vector_to_bytes(qv),
                top_k=top_k,
                include_vector=include_vector,
                selector=self._selector(),
            )
            rsp = self._stub.Search(req, timeout=self._timeout)
            results = []
            for hit in rsp.hits:
                payload = {k: _scalar_to_python(v) for k, v in hit.payload.items()}
                vec = None
                if include_vector and hit.vector:
                    vec = _bytes_to_vector(hit.vector)
                results.append(
                    SearchResult(id=hit.id, score=hit.score, vector=vec, payload=payload)
                )
            all_results.append(results)
        return all_results

    def get(
        self,
        collection: str,
        *,
        ids: list[str],
        include_vector: bool = False,
    ) -> list[SearchResult]:
        """Retrieve documents by ID."""
        req = GetRequest(
            collection=collection,
            ids=[str(i) for i in ids],
            include_vector=include_vector,
            selector=self._selector(),
        )
        rsp = self._stub.Get(req, timeout=self._timeout)
        results = []
        for rec in rsp.records:
            payload = {k: _scalar_to_python(v) for k, v in rec.payload.items()}
            vec = None
            if include_vector and rec.vector:
                vec = _bytes_to_vector(rec.vector)
            results.append(
                SearchResult(id=rec.id, score=0.0, vector=vec, payload=payload)
            )
        return results
