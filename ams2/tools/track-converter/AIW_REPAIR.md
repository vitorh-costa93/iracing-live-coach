# Reparo de IA sem a fonte AC

`repair_aiw.py` repara uma conversão existente quando a linha principal cruza
zebras/escape ou sai do GCL. Não substitui uma nova conversão quando há erros
na geometria/colisão ou quando o centro dos waypoints está fora do asfalto.

```powershell
python -I repair_aiw.py '<origem>/Automobilista 2' bahrain_ac '<novo destino>/Automobilista 2'
python -I check_out.py '<novo destino>/Automobilista 2' bahrain_ac --require-pits --json '<relatorio>.json'
python -I tests_aiw_repair.py
```

O destino precisa ser novo e externo à origem. A origem permanece intacta.
Somente a entrada AIW do Physics.bff muda; os demais recursos conservam seus
bytes. Grid, pits, ponteiros, setores e os blocos dos ramos de boxes permanecem
intactos. O script reduz as larguras à interseção entre asfalto (CSM id 0), GCL
flag 1 e corredor anterior, suaviza a correção lateral e atualiza tempo estimado,
marcadores de curva e groove. Não aumenta o corredor original.

A margem lateral padrão é 1,5 m nos waypoints. A linha serializada também é
verificada entre eles, em intervalos inferiores a 1 m, exigindo pelo menos 1 m
até a borda do asfalto e inclusão no GCL. Corredores desconectados ambíguos,
camadas de piso distantes e soluções inválidas são recusados. Não há instalação
automática; só empacote após aprovação do verificador independente.

Caso Bahrain/Sakhir de 21/10/2026: o pacote anterior tinha 3 waypoints no escape,
15 na zebra e 1 fora do GCL. A fonte AC não estava disponível; o reparo usa a
colisão e os limites do próprio pacote. Esta evidência não comprova desempenho
da IA por carro nem entrada/parada/saída dos boxes no jogo; isso exige teste ao
vivo autorizado. Tempo de volta calculado é estimativa física, não telemetria.
