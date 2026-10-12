# Tema "2012" — GP da Malásia 2012 (gráfico de TV 2011–2013)

Fonte: capturas enviadas pelo usuário (`f1-2012-*.jpg`, vídeo 16:9 dentro de tela 2000x900; ×1,2 para 1920x1080).
Corrida usa a UI própria do 2012; classificação = mesma lógica/cores do 2004 na pele 2012.

## Linguagem visual
- Células escuras **inclinadas "/"** com brilho no topo (paralelogramo), texto branco condensado (DIN na TV; aqui Barlow Semi Condensed).
- Nome/valor: preto; equipe: azul-ardósia com brilho; posição: cinza (vermelho no líder); ganho de posições: verde com ▲; melhor volta: roxo; contador sob amarela: amarelo.
- Velocidade em **amarelo**; DRS verde quando aberto, cinza apagado; barra de **KERS** vermelha vertical com "+"/"-" e raio.

## Mapeamento (o que o AMS2 permite)
| Gráfico 2012 | Widget f1-2012 | Observação |
|---|---|---|
| Torre de siglas à esquerda (líder vermelho) | `standings` (lógica 2004, pele 2012) | sem "BATTLE FOR 5th"/▲ ganhos (pendente) |
| Pílula "Lap 41 / 56" topo-centro (amarela sob bandeira) | `lapcounter` | amarela só com FlagColour 6/7 |
| Faixa inferior de 5 células (1–5, 6–10…; "Interval" no líder; OUT) | `ticker` (novo) | páginas de 5, 6 s; modo interval/gap |
| Legenda "N ROSBERG / Mercedes" + bandeira + pneu | `drivercaption` | bandeira: país da pintura (jogador) / catálogo (IA) |
| Duelo: velocidade amarela + DRS + KERS sobre as placas | `battle` (2012) | DRS/KERS só do jogador (mBoostActive/mBoostAmount, NÃO conferidos no jogo) |
| "Pit Lane 11.6 [4]" | `pittimer` (2004 na pele) | layout não igual ao da TV |
| Dial de bordo (km/h verde, marcha, THROTTLE/BRAKE, RPM, KERS, DRS) | — | NÃO feito (Inputs segue o velocímetro 2004) |
| "Fastest Lap 1:49.800" (roxo) | — | NÃO feito |
