"""Download pinned official wasm-tools with release-asset SHA256 verification.
Does not execute downloaded files. Installs only into a fresh artifacts directory.
"""
import hashlib
import os
from pathlib import Path
import platform
import tarfile
import tempfile
import urllib.request
import zipfile

VERSION = "1.263.0"
HASHES = {
    "Windows": "0803df158b4dbea6b029d11f4206c414f432f0447af91542817061e4f0bd912e",
    "Linux": "21c036c1740a0552124c79c6e2170cb61c5b016005671a6d5bc06cf642165431",
}


def main():
    system = platform.system()
    if system not in HASHES or platform.machine().lower() not in ("amd64", "x86_64"):
        raise SystemExit("Only Windows/Linux x64 are configured")
    root = Path(__file__).resolve().parents[1] / "artifacts" / "toolchain"
    root.mkdir(parents=True, exist_ok=True)
    directory = Path(tempfile.mkdtemp(prefix="wasm-tools-", dir=root))
    suffix = "windows.zip" if system == "Windows" else "linux.tar.gz"
    name = f"wasm-tools-{VERSION}-x86_64-{suffix}"
    archive = directory / name
    urllib.request.urlretrieve(f"https://github.com/bytecodealliance/wasm-tools/releases/download/v{VERSION}/{name}", archive)
    if hashlib.sha256(archive.read_bytes()).hexdigest() != HASHES[system]:
        raise SystemExit("wasm-tools checksum mismatch")
    unpacked = directory / "unpacked"
    unpacked.mkdir()
    if system == "Windows":
        with zipfile.ZipFile(archive) as contents:
            for item in contents.infolist():
                if not (unpacked / item.filename).resolve().is_relative_to(unpacked.resolve()):
                    raise ValueError("Unsafe archive path")
            contents.extractall(unpacked)
    else:
        with tarfile.open(archive) as contents:
            contents.extractall(unpacked, filter="data")
    executable = next(unpacked.rglob("wasm-tools.exe" if system == "Windows" else "wasm-tools"))
    if "GITHUB_PATH" in os.environ:
        with open(os.environ["GITHUB_PATH"], "a", encoding="utf-8") as output:
            output.write(str(executable.parent.resolve()) + "\n")
    print(executable.parent.resolve())


if __name__ == "__main__":
    main()
