# Riffstorm

Jogo de ritmo de guitarra **nativo para Windows** (sem emulador), com controle, guitarra USB ou teclado, e
**multiplayer online até 6 jogadores**. As músicas e as notas vêm dos **seus próprios discos** de Guitar Hero
(PS2/PS3): o jogo lê a ISO uma vez, importa as músicas para uma biblioteca local e junta todos os jogos num só.

> O Riffstorm **não traz nenhuma música, ISO ou capa de álbum**. Cada jogador usa as cópias dos próprios discos.
> Projeto de fãs, sem fins lucrativos e sem ligação com a Activision, Harmonix, Neversoft ou RedOctane.
> "Guitar Hero" é marca registrada dos respectivos donos e aparece aqui só para dizer quais discos são compatíveis.

## ⬇️ [BAIXAR O JOGO (Riffstorm-windows.zip)](https://github.com/henrnasc/Riffstorm/releases/latest/download/Riffstorm-windows.zip)

> **Atenção:** o botão verde **"Code → Download ZIP"** baixa só o código-fonte, **sem o `Riffstorm.exe`**.
> Para jogar, use o link acima (ou a página [Releases](https://github.com/henrnasc/Riffstorm/releases/latest)).

---

## Como jogar (passo a passo)

1. **Baixe** o [**`Riffstorm-windows.zip`**](https://github.com/henrnasc/Riffstorm/releases/latest/download/Riffstorm-windows.zip)
   e extraia onde quiser (ex.: `Documentos\Riffstorm`).
   Não precisa instalar nada: o .NET já vem dentro do `Riffstorm.exe`.
2. **Coloque as ISOs** na pasta **`ISOs`** que vem junto, ao lado do `Riffstorm.exe`:

   ```
   Riffstorm\
   ├── Riffstorm.exe
   ├── COMO JOGAR.txt
   ├── assets\
   └── ISOs\                          <-- coloque as ISOs aqui
       ├── Guitar Hero (USA).iso
       ├── Guitar Hero II (USA).iso
       ├── Guitar Hero Encore - Rocks the 80s (USA).iso
       └── Guitar Hero III - Legends of Rock (USA).iso
   ```
   Pode ser direto na pasta ou em subpastas (uma por disco). Não precisa ter todas: cada disco libera as músicas dele.
3. **Abra o `Riffstorm.exe`.** O jogo confere cada ISO e importa as músicas sozinho, em segundo plano (dá para jogar
   enquanto isso; acompanhe em *Configurações > Discos*). Depois de importada, a ISO pode ser apagada: as músicas
   ficam em `%LOCALAPPDATA%\Riffstorm\biblioteca`.
4. As **capas dos álbuns** são baixadas automaticamente (Cover Art Archive) na primeira vez que aparecem.
5. **Atualizações:** na tela inicial, o botão no canto inferior esquerdo verifica se tem versão nova. Se tiver, é só clicar
   nele (ou apertar F5 / Select): o jogo baixa, instala e reabre sozinho. Suas músicas, recordes e configurações continuam.

### ISOs compatíveis

O jogo só aceita **estas versões exatas** (dump do disco original, região **USA / NTSC-U**). Ele confere a
"impressão digital" (SHA-1) do arquivo; o nome do arquivo não importa. Assim todo mundo tem exatamente as mesmas
notas, o que é obrigatório para o multiplayer.

| Jogo | Arquivo (nome padrão do dump) | Console | Tamanho (bytes) | SHA-1 | Músicas |
|---|---|---|---|---|---|
| Guitar Hero | `Guitar Hero (USA).iso` | PS2 · SLUS-21224 | 2.300.313.600 | `4caacaea582c79605f8c3582dd4b90abdec699f5` | 47 |
| Guitar Hero II | `Guitar Hero II (USA).iso` | PS2 · SLUS-21447 | 4.646.305.792 | `e1a3a81827aa4264984b4f3678de58ca3d881528` | 63 |
| Guitar Hero Encore: Rocks the 80s | `Guitar Hero Encore - Rocks the 80s (USA).iso` | PS2 · SLUS-21586 | 1.901.821.952 | `184f2dc9993d9b1e6edf0125f68e1ce07742567a` | 30 |
| Guitar Hero III: Legends of Rock | `Guitar Hero III - Legends of Rock (USA).iso` | **PS3** | 6.318.391.296 | `a1fe34e1895aab21e7da0dca62bb49edd1f6dffa` | 73 |

Para conferir a sua ISO antes (PowerShell): `Get-FileHash "arquivo.iso" -Algorithm SHA1`.
Se a ISO não for compatível, o jogo avisa em *Configurações > Discos* e não importa nada.

### Precisa instalar mais alguma coisa?

| Programa | Precisa? | Para quê |
|---|---|---|
| Windows 10 ou 11 (64 bits) | sim | o jogo só existe para Windows |
| .NET | **não** | já vem dentro do `Riffstorm.exe` |
| [Radmin VPN](https://www.radmin-vpn.com) (grátis) | **só para o online** | cria uma rede entre você e seus amigos |

---

## Multiplayer online (duelo de pontos)

- Até **6 jogadores**, cada um no seu PC e na sua casa. Todos tocam a mesma música ao mesmo tempo; ganha quem fizer
  mais pontos. Cada um escolhe a própria dificuldade. **Todos precisam estar na mesma versão do jogo.**
- Na tela: a sua pista no centro, as pistas dos outros ao fundo, o placar de todos em cima (o seu em destaque) e uma
  barra na direita mostrando quanto falta da música.

1. Todos instalam o **Radmin VPN**, e um de vocês cria uma rede nele (*Rede > Criar rede*); os outros entram nela.
2. No jogo: **Multiplayer Online** → digite seu nome → **Criar sala**.
3. Os amigos abrem **Multiplayer Online** e a sala aparece sozinha em **Salas disponíveis** (ou digitam o código,
   ex.: `4F2-K9QA`).
4. Na 1ª vez, o Windows do anfitrião pergunta se o Riffstorm pode usar a rede: clique em **Permitir**
   (marque redes privadas **e** públicas — a rede do Radmin costuma aparecer como pública).
5. **Sorteio de músicas:** cada jogador põe uma música no sorteio (só aparecem as que **todos** têm), pode
   **ouvir** a dos outros (só no próprio PC, uma de cada vez) e **tirar** a sua se gostar mais de outra.
   Quando todos marcam **Pronto**, o anfitrião aperta **Sortear e começar** e uma roleta escolhe a música.
6. **Durante a música:** mande reações para os amigos (🔥 Fogo, 😂 Risada, 💀 Caveira, 👏 Palmas — teclas 1 a 4 ou
   D-pad ←/→, L3, R3) e acompanhe os avisos automáticos ("Bruno: 100 notas seguidas!", "Carla foi vaiada!",
   "Diego assumiu a liderança!").

Portas usadas pelo anfitrião: **TCP 47823** (jogo) e **UDP 47824** (para a sala aparecer na lista).
## Controles

| | Controle (padrão) | Guitarra USB | Teclado |
|---|---|---|---|
| Trastes (verde → laranja) | L2 L1 R1 R2 ✕ (Xbox: LT LB RB RT A) | A B Y X LB | A S D F G |
| Palhetada | D-pad ↑/↓ | barra | Enter / setas |
| Alavanca | analógico direito | analógico direito | segure K |
| Modo Fúria | △ / Y / Select | Select ou inclinar | Espaço |
| Trocar de jogo na lista | L1 / R1 | L1 / R1 | Q / E |
| Pausa | Start | Start | Esc |

Todos os botões podem ser trocados em **Configurações > Controles**: tem o desenho da pista com o botão de cada traste embaixo e a lista das outras ações (palhetada, Modo Fúria, alavanca, pausa...). Cada um escolhe uma tecla e um botão do controle.

Por padrão a palhetada é automática (basta apertar o traste na hora certa); dá para trocar em
*Configurações > Palhetada*. Mais detalhes em `COMO JOGAR.txt`.

---

## Para desenvolvedores

C# .NET 8 + [Raylib-cs](https://github.com/ChrisDill/Raylib-cs). Requer o .NET 8 SDK.

```
powershell -ExecutionPolicy Bypass -File build.ps1           # compila em .\jogo
powershell -ExecutionPolicy Bypass -File build-release.ps1   # gera dist\Riffstorm-windows.zip
```

| Pasta / arquivo | O que é |
|---|---|
| `src/` | Código do jogo |
| `src/Importer.cs`, `Disc.cs`, `Dtb.cs`, `Vgs.cs` | Leitura dos discos Harmonix (PS2): ARK, DTB, VGS, MIDI |
| `src/Neversoft.cs` | Leitura do disco do GH3 (PS3): ISO Joliet, PAK/QB, áudio FSB, notas → MIDI |
| `src/Net.cs`, `src/OnlineScreens.cs` | Multiplayer online (servidor no PC do anfitrião, JSON por TCP) |
| `assets/covers.json` | Música → release group do MusicBrainz (as capas são baixadas pelo jogo) |
| `assets/ui/` | Arte da tela inicial |
| `pacote/` | Arquivos que vão junto no zip (guia, pasta `ISOs`) |
| `tools/covers/` | Scripts que montam e conferem o `covers.json` |

Biblioteca local de cada jogador (`%LOCALAPPDATA%\Riffstorm\biblioteca`):

```
biblioteca/
  gh1/
    game.json            nome, código do disco, carreira (níveis na ordem original) e bônus
    ironman/
      song.json          título, artista, mixagem dos canais, prévia, BPM, chartHash
      audio.vgs          áudio multitrilha (ou stems .mp3 no GH3)
      notes.mid          notas de todas as dificuldades
```

Testes:

```
jogo\Riffstorm.exe --selftest
jogo\Riffstorm.exe --no-iso --loopback --host --name A --autopick gh1/ironman --autostart 2 --bot
jogo\Riffstorm.exe --no-iso --join 127.0.0.1 --name B --autoready --bot
```
O primeiro faz um bot tocar todas as músicas em todas as dificuldades; os outros dois simulam uma partida online
no mesmo PC.
