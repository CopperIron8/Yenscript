#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"
case "$(uname -s)" in
  Linux*) host=linux-x64 ;;
  Darwin*) host=osx-arm64 ;;
  MINGW*|MSYS*|CYGWIN*) host=win-x64 ;;
  *) printf 'Unsupported build host\n' >&2; exit 1 ;;
esac
rid="${1:-$host}"
case "$rid" in linux-x64|win-x64|osx-arm64) ;; *) printf 'Unsupported RID: %s\n' "$rid" >&2; exit 1 ;; esac
if [[ "$rid" != "$host" ]]; then
  printf 'Native AOT requires a matching build host. Run %s on its native runner.\n' "$rid" >&2
  exit 1
fi
if [[ "$host" == win-x64 ]] && ! command -v vswhere.exe >/dev/null 2>&1; then
  installer="$(cygpath -u "${ProgramFiles_x86:-C:\\Program Files (x86)}")/Microsoft Visual Studio/Installer"
  if [[ -f "$installer/vswhere.exe" ]]; then export PATH="$installer:$PATH"; fi
fi
out="$root/artifacts/publish/$rid"
dotnet publish src/Yenscript.Cli/Yenscript.Cli.csproj -c Release -r "$rid" \
  --self-contained true -p:PublishAot=true -p:PublishTrimmed=true \
  -p:StripSymbols=true -p:TrimmerSingleWarn=false -warnaserror -o "$out"
binary="$out/yenscriptc"
if [[ "$rid" == win-x64 ]]; then binary="$binary.exe"; fi
bash scripts/smoke-test.sh "$binary"
