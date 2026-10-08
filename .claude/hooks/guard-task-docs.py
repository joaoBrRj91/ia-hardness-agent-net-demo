#!/usr/bin/env python3
"""PreToolUse (planner e retro): bloqueia escrita fora de docs/tasks/.

Exit 2 bloqueia a ferramenta e devolve a mensagem ao agente.
"""
import json
import os
import sys

data = json.load(sys.stdin)
path = data.get("tool_input", {}).get("file_path", "")
root = os.environ.get("CLAUDE_PROJECT_DIR") or data.get("cwd") or os.getcwd()

allowed = os.path.realpath(os.path.join(root, "docs", "tasks"))
target = os.path.realpath(path if os.path.isabs(path) else os.path.join(root, path))

if target == allowed or target.startswith(allowed + os.sep):
    sys.exit(0)

print(
    f"Escrita bloqueada: {path} está fora de docs/tasks/. "
    "Este agente só escreve na pasta da tarefa.",
    file=sys.stderr,
)
sys.exit(2)
