# iracing-live-coach

Overlay + Control Center para iRacing (WPF/.NET). `src/` + `tests/` = V2 (congelado, só baseline);
`v3/` = rearquitetura ativa (OverlayHost, ControlCenter, Core). Mudanças novas vão em `v3/`.

## Build e publicação
- Publicar V3: `powershell -ExecutionPolicy Bypass -File scripts\publish-v3.ps1 [-Shortcut]`
  - Saída em `%LOCALAPPDATA%\IracingLiveCoachV3` e espelho em `Desktop\iRacing Live Coach V3` (robocopy /MIR).
  - A cópia do Desktop nunca pode ficar velha: toda mudança entregue = rodar o publish.
  - O espelho é pulado (com aviso) se o exe estiver aberto; avise o usuário para fechar e repetir.
- V2: `scripts\publish.ps1` (não alterar o comportamento de V2).
- Testes: `dotnet test` em `v3/tests/*` (e `tests/*` só se tocar V2).

## Regras
- **Kapps = somente leitura.** Os fontes extraídos ficam no scratchpad (`kapps_src`); só ler, comparar e
  replicar a lógica. Nunca rodar, modificar nem redistribuir código do Kapps.
- Não substituir "parece certo" por validação: comparar com o comportamento exato do Kapps antes de concluir.
- Não commitar `preview-*.png`, `.claude/scheduled_tasks.lock` nem `.superpowers/`.
- Capturas de tela para validação: recortes pequenos, não releia as já analisadas.

## Subagentes
- Use os agentes globais (`~/.claude/agents`). Telemetria/cálculo de widget → `code-implementer` (ou `hard-logic` se for lógica difícil), um widget por chamada, passando só caminhos de arquivo. Layout/cores/dimensões → `code-implementer`. Logs/prints/comparações → `monitor-reader`.

## Validação
1. `dotnet build` + `dotnet test` sem falhas.
2. Publicar com `publish-v3.ps1`.
3. Conferir visualmente o widget alterado (um recorte por widget) e, se for dado do SDK, contra o Kapps.
