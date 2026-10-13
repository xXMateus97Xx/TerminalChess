# TerminalChess

Xadrez jogado no terminal contra uma engine UCI (Stockfish, Dragon, Lc0 e outras).

```
 r  n  b  q  k  b  n  r  8
 p  p  p  p     p  p  p  7
                         6
             p           5
             P           4
                         3
 P  P  P  P     P  P  P  2
 R  N  B  Q  K  B  N  R  1
 A  B  C  D  E  F  G  H

You are playing with White

Last white move: e2e4
Last black move: e7e5
```

## Requisitos

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Uma engine de xadrez compatível com o protocolo UCI, por exemplo o [Stockfish](https://stockfishchess.org/download/)

## Como executar

```
dotnet run --project TerminalChess -- -e C:\caminho\para\stockfish.exe -l hard -c white
```

Ou, depois de compilado:

```
TerminalChess -e C:\caminho\para\stockfish.exe -l hard -c white
```

### Opções

| Opção | Descrição | Padrão |
|---|---|---|
| `-e`, `--engine` | Caminho do executável da engine UCI | `stockfish.exe` |
| `-l`, `--level` | Nível: `1` a `6` ou o nome (`beginner`, `novice`, `medium`, `hard`, `expert`, `worldchampion`) | `novice` |
| `-c`, `--color` | Sua cor: `white`/`w` ou `black`/`b` | sorteada |
| `-t`, `--threads` | Threads que a engine pode usar (de 1 até o número de processadores) | metade dos processadores |
| `-h`, `--help` | Mostra a ajuda | |

## Como jogar

Os lances são digitados em notação UCI: casa de origem seguida da casa de destino.

| Lance | Exemplo |
|---|---|
| Lance comum | `e2e4`, `g1f3` |
| Captura | igual ao lance comum: `e4d5` |
| Roque | movimento do rei: `e1g1` (pequeno), `e1c1` (grande) |
| En passant | movimento do peão para a casa vazia: `e5d6` |
| Promoção | letra da peça no final: `e7e8q` (dama), `r` (torre), `b` (bispo), `n` (cavalo) |

Letras maiúsculas também são aceitas. Na promoção, a letra é obrigatória.

Comandos durante a partida:

- `resign`: abandona a partida.
- `draw`: reivindica o empate, quando há repetição tripla ou 50 lances sem captura nem movimento de peão.

## Publicação nativa (AOT)

O projeto está configurado para Native AOT, com instruções `x86-64-v3` (exige CPU com AVX2):

```
dotnet publish TerminalChess -c Release -r win-x64
```

No Windows, o AOT exige o workload **Desenvolvimento para desktop com C++** do Visual Studio.
