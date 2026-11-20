"""Install pinned official WABT into a new isolated directory; verify SHA256.
This development/CI helper never executes downloaded files itself.
"""
import hashlib
import os
from pathlib import Path
import platform
import tarfile
import tempfile
import urllib.request

VERSION = "1.0.42"


def main():
    system = {"Windows": "windows", "Linux": "linux"}.get(platform.system())
    if system is None or platform.machine().lower() not in ("amd64", "x86_64"):
        raise SystemExit("Only Windows/Linux x64 are configured for this installer")
    root = Path(__file__).resolve().parents[1] / "artifacts" / "toolchain"
    root.mkdir(parents=True, exist_ok=True)
    directory = Path(tempfile.mkdtemp(prefix="wabt-" + VERSION + "-", dir=root))
    archive_name = f"wabt-{VERSION}-{system}-x64.tar.gz"
    base = f"https://github.com/WebAssembly/wabt/releases/download/{VERSION}/"
    archive = directory / archive_name
    urllib.request.urlretrieve(base + archive_name, archive)
    with urllib.request.urlopen(base + archive_name + ".sha256", timeout=60) as response:
        expected = response.read().decode().split()[0]
    if hashlib.sha256(archive.read_bytes()).hexdigest() != expected:
        raise SystemExit("WABT checksum mismatch")
    unpacked = directory / "unpacked"
    unpacked.mkdir()
    with tarfile.open(archive) as contents:
        contents.extractall(unpacked, filter="data")
    executable = next(unpacked.rglob("wat2wasm.exe" if system == "windows" else "wat2wasm"))
    binary_directory = executable.parent.resolve()
    if "GITHUB_PATH" in os.environ:
        with open(os.environ["GITHUB_PATH"], "a", encoding="utf-8") as output:
            output.write(str(binary_directory) + "\n")
    print(binary_directory)


if __name__ == "__main__":
    main()
