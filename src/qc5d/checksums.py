"""Byte-exact SHA-256 utilities; files are streamed rather than loaded in memory."""

import hashlib
from pathlib import Path


def sha256_bytes(data: bytes) -> str:
    """Return the lowercase SHA-256 hex digest of an exact byte sequence."""
    return hashlib.sha256(data).hexdigest()


def sha256_file(path: str | Path, *, chunk_size: int = 1024 * 1024) -> str:
    """Hash a file without changing its bytes, line endings, or encoding."""
    if chunk_size <= 0:
        raise ValueError("chunk_size must be positive")
    digest = hashlib.sha256()
    with Path(path).open("rb") as stream:
        for chunk in iter(lambda: stream.read(chunk_size), b""):
            digest.update(chunk)
    return digest.hexdigest()
