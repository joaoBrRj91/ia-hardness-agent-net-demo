---
name: pr
description: "Abre a PR da tarefa atual. Chamada pelo orquestrador ao fim da fase validating, ou pelo usuário com /pr."
---
1. Travas de entrada. Se qualquer uma falhar, pare e devolva o motivo:
   - dotnet test passa;
   - progress-update.md não tem subtask pendente (escopo original, [REVISÃO] ou [REVIEW-PR]);
   - o último review-<N>.md da tarefa tem verdict: Aprovado.
2. Confira que os commits seguem Conventional Commits citando o ticket
   (feat(<id>): ..., fix(<id>): ...). Não reescreva o histórico.
3. git push -u origin <branch>
4. gh pr create com corpo contendo:
   - Resumo (2-3 linhas) e link do ticket.
   - Decisões de arquitetura e premissas (de 03-plano.md).
   - Como testar.
   - Riscos conhecidos.
   - Correções da revisão interna (seções [REVISÃO] de progress-update.md)
     e sugestões do reviewer não aplicadas.
5. Comente no ticket o link da PR.
6. Devolva o número e o link da PR.

Nunca faça merge.
