"""XR Superspace reference package; scientific algorithms arrive in later stages."""

from importlib.metadata import PackageNotFoundError, version

try:
    __version__ = version("qc5d")
except PackageNotFoundError:
    __version__ = "0.1.0"

__all__ = ["__version__"]
