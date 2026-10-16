"""Bounded, pickle-free NPY v1/v2 inspection before allocation or NPZ decoding."""

import io
import math
import zipfile
from pathlib import Path

import numpy as np

MAX_ARCHIVE_BYTES = 512 * 1024 * 1024


def read_npz(source: Path | bytes) -> dict[str, np.ndarray]:
    if isinstance(source, Path):
        if source.stat().st_size > MAX_ARCHIVE_BYTES:
            raise ValueError("NPZ exceeds the 512 MiB file budget")
        payload = source.read_bytes()
    else:
        payload = source
    if len(payload) > MAX_ARCHIVE_BYTES:
        raise ValueError("NPZ exceeds the 512 MiB file budget")
    try:
        with zipfile.ZipFile(io.BytesIO(payload)) as archive:
            members = archive.infolist()
            names = [member.filename for member in members]
            if not members or len(members) > 128 or len(set(names)) != len(names):
                raise ValueError("NPZ has empty, excessive, or duplicate members")
            if sum(member.file_size for member in members) > MAX_ARCHIVE_BYTES:
                raise ValueError("NPZ uncompressed contents exceed the memory budget")
            result = {}
            for member in members:
                name = member.filename
                if (
                    not name.endswith(".npy")
                    or "\\" in name
                    or ":" in name
                    or any(part in ("", ".", "..") for part in name.split("/"))
                ):
                    raise ValueError("NPZ member is not a safe relative NPY path")
                with archive.open(member) as stream:
                    version = np.lib.format.read_magic(stream)
                    if version not in ((1, 0), (2, 0)):
                        raise ValueError("unsupported NPY version; expected v1/v2")
                    reader = (
                        np.lib.format.read_array_header_1_0
                        if version == (1, 0)
                        else np.lib.format.read_array_header_2_0
                    )
                    shape, fortran, dtype = reader(stream, max_header_size=10_000)
                    if dtype.hasobject or dtype.fields or dtype.kind not in "biufcU":
                        raise ValueError("NPZ object/pickle or unsupported dtype rejected")
                    if len(shape) > 3 or any(type(size) is not int or size < 0 for size in shape):
                        raise ValueError("NPZ shape is invalid")
                    size = math.prod(shape) * dtype.itemsize
                    if size > MAX_ARCHIVE_BYTES or size != member.file_size - stream.tell():
                        raise ValueError(
                            "NPZ shape/data length mismatch or allocation budget exceeded"
                        )
                    if fortran or dtype.str.startswith(">"):
                        raise ValueError("NPZ must use C-order little-endian arrays")
                raw = archive.read(member)
                result[name[:-4]] = np.lib.format.read_array(io.BytesIO(raw), allow_pickle=False)
            return result
    except (zipfile.BadZipFile, EOFError, OSError, RuntimeError) as error:
        raise ValueError("malformed or truncated NPZ archive") from error
