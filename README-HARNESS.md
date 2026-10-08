# Harness Claude — do ticket à retrospectiva (.NET)

Pacote do fluxo iterativo descrito no doc *Harness Engineering com Claude — do ticket à PR*.
Você digita `/task <id>`; o orquestrador conduz o ticket por todas as fases e para nos
pontos em que a decisão é sua.

## Estrutura

```text
CLAUDE.md                              # memória do projeto (adapte ao seu serviço)
.claude/
├── settings.json                      # permissões + hook de format/build
├── hooks/
│   ├── post-edit.sh                   # PostToolUse: dotnet format + build em arquivos .NET
│   └── guard-task-docs.py             # PreToolUse: planner e retro só escrevem em docs/tasks/
├── agents/
│   ├── explorer.md                    # lê a codebase (somente leitura)
│   ├── planner.md                     # análise + plano, cabeçalho fixo
│   ├── implementer.md                 # TDD, um único agente por ticket
│   ├── reviewer.md                    # revisão do diff (somente leitura)
│   └── retro.md                       # propõe skill, regra ou hook
└── skills/
    ├── task/                          # orquestrador /task
    │   ├── SKILL.md
    │   ├── state.template.json
    │   ├── scripts/check-header.py    # valida o cabeçalho de 02-analise.md
    │   └── templates/                 # 01-ticket, 02-analise, 03-plano, progress-update, 04-retro
    ├── ticket/SKILL.md                # lê o ticket e classifica perguntas
    └── pr/SKILL.md                    # abre a PR com travas de entrada
docs/tasks/                            # uma pasta por ticket, criada pelo /task
```

## Instalação

1. Copie o conteúdo deste pacote para a raiz do repositório (mesclando com `.claude/` e
   `CLAUDE.md`, se já existirem).
2. Adapte o `CLAUDE.md`: nome do serviço, stack e regras reais.
3. Garanta que os scripts são executáveis: `chmod +x .claude/hooks/* .claude/skills/task/scripts/*`.
4. Pré-requisitos na máquina: `python3`, `dotnet`, `git` e `gh` autenticado (`gh auth login`).
5. Conecte o gerenciador de tarefas (opcional; sem ele, a skill `ticket` usa `gh issue view`):
   ```bash
   claude mcp add --transport http github https://api.githubcopilot.com/mcp/
   claude mcp add --transport http linear https://mcp.linear.app/mcp
   ```
6. Se a branch base não for `main`, ajuste `.claude/agents/reviewer.md`.
7. Faça commit de `.claude/` e `CLAUDE.md`: o harness é código e evolui por PR.

## Uso

```text
/task ENG-1234
```

O mesmo comando inicia e retoma. O orquestrador lê `docs/tasks/<id>/state.json` e segue
da fase em que a tarefa parou.

| Fase (`phase`) | O que acontece | Você decide? |
| --- | --- | --- |
| `ticket` | Skill `ticket` lê e classifica as perguntas | Se houver bloqueante: responder ou estacionar |
| `awaiting-answers` | Aguarda respostas no ticket | Sim (você ou o PO) |
| `explore` | Subagent `explorer` mapeia a codebase | Não |
| `plan` | Subagent `planner` + `check-header.py` | Não |
| `awaiting-decision` / `split` | Decisão estrutural ou ticket G | Sim |
| `plan-review` | Resumo do plano | Sim: aprovar, ajustar ou estacionar |
| `implementing` | Subagent `implementer`, TDD, `progress-update.md` | Só se `blocked` |
| `validating` | `dotnet test` + `reviewer`, até 2 rodadas `[REVISÃO]` | Só na 3ª rodada bloqueada |
| `pr` | Skill `pr` abre a PR | Não |
| `pr-review` | Triagem dos comentários novos da PR | Sim, item a item, inclusive Mudança |
| `done` | Subagent `retro` propõe melhorias | Sim, proposta a proposta |
| `closed` | Tarefa encerrada | — |

## Arquivos de cada tarefa (`docs/tasks/<id>/`)

| Arquivo | Escrito por | Conteúdo |
| --- | --- | --- |
| `state.json` | Orquestrador | Fase, perguntas, premissas, `prUrl`, `processedComments` |
| `01-ticket.md` | Skill `ticket` | Resumo do ticket e perguntas classificadas |
| `01b-codebase.md` | Orquestrador (resposta do `explorer`) | Mapa da codebase |
| `02-analise.md` | `planner` | Cabeçalho fixo + análise |
| `03-plano.md` | `planner` | Decisões, premissas, passos, critérios de pronto |
| `progress-update.md` | `implementer` | Subtasks, status, hashes; seções `[REVISÃO]` e `[REVIEW-PR]` |
| `review-<N>.md` | Orquestrador (resposta do `reviewer`) | Verdict e achados de cada rodada |
| `04-retro.md` | `retro` | Propostas e observações |

## Roteiro de adoção (4 semanas)

- [ ] Semana 1: `CLAUDE.md` + hook + skill `ticket` + `state.json`; `/task` só até `explore`.
- [ ] Semana 2: `planner` + `check-header.py` + checkpoint do plano; implementar à mão.
- [ ] Semana 3: `implementer` + `progress-update.md` + `reviewer` com rodadas `[REVISÃO]`.
- [ ] Semana 4: skill `pr` + triagem do review da PR + `retro`.

## Pontos a verificar na sua versão do Claude Code

- **Hooks no frontmatter dos subagents** (`planner.md`, `retro.md`): se a sua versão não
  suportar, mova o `PreToolUse` para o `.claude/settings.json`. Nesse caso ele vale para
  todos os agentes, então restrinja o `matcher` ou ajuste o script.
- **Skills como slash command:** confirme que `/task` aparece no menu. Se não aparecer,
  peça "rode a skill task para ENG-1234".
- **Hook de build:** o `post-edit.sh` roda `dotnet build` a cada edição de arquivo .NET.
  Em soluções grandes, troque por um build do projeto afetado.
