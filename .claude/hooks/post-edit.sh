#!/usr/bin/env bash
# PostToolUse: dotnet format + build, só quando um arquivo .NET foi alterado.
# Exit 2 devolve o erro de build ao agente, que corrige antes de seguir.
set -uo pipefail

file=$(python3 -c 'import json,sys; print(json.load(sys.stdin).get("tool_input",{}).get("file_path",""))')

case "$file" in
  *.cs|*.csproj|*.props|*.targets) ;;
  *) exit 0 ;;
esac

cd "${CLAUDE_PROJECT_DIR:-.}" || exit 0

dotnet format --include "$file" >/dev/null 2>&1 || true

if ! out=$(dotnet build --no-restore -v q 2>&1); then
  echo "dotnet build falhou após editar $file:" >&2
  echo "$out" | grep -E " error " | head -20 >&2
  exit 2
fi
exit 0
