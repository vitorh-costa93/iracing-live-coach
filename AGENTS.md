# Live Coach — mapa e regras
- `src/` e `tests/`: V2 congelado. `v3/`: iRacing V3. `ams2/`: aplicativo separado de Automobilista 2.
- Escolha o produto solicitado antes de editar. Trabalho AMS2 vai somente em `ams2/` e no script correspondente; não altere V3 incidentalmente.
- Para AMS2, leia `ams2/AGENTS.md` mesmo quando o diretório corrente for a raiz.
- Kapps é referência somente de leitura: não executar, modificar ou redistribuir fontes extraídos.
- Para código, build/test dos projetos afetados, seguido de validação final do produto. Compare telemetria com evidência real; não declare equivalência só pela compilação.
- Publicação iRacing V3: `scripts/publish-v3.ps1`; AMS2: `scripts/publish-ams2.ps1`. Entrega de executável precisa publicar o produto correto e conferir espelho do Desktop; exe aberto pode impedir a cópia.
- Não iniciar jogo/overlay real nem enviar teclas ao jogo sem autorização específica da tarefa. Prefira fake/prévia quando suficiente.
- Não commitar previews, bin/obj, arquivos de lock ou caches locais.

## Execução econômica
- Leia `docs/CODEX_CONTINUIDADE.md` somente ao retomar trabalho; atualize-o ao fechar uma etapa, sem copiar histórico ou segredos.
- Busque arquivos e seções relevantes antes de carregar documentos inteiros. Seções históricas são contexto sob demanda.
- Tarefas pequenas são diretas; delegue apenas trabalho independente extenso ou revisão de risco, com escopo e critério de aceite.
- Um responsável integra e valida o estado final. Subagentes fazem testes focados e devolvem evidência; não repetem toda a suíte/build por hábito.
- Alterações somente em instruções/documentação exigem revisão de diff e links, sem build de aplicação. Para código, cumpra as verificações abaixo; repita se o estado relevante mudar.
- Consulte `docs/CODEX_CONTEXTO.md` quando existir para localizar seções de contexto.
