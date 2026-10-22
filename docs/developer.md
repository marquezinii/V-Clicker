# V-Clicker para desenvolvedores

Linha de comando, logs estruturados, variáveis e os formatos de arquivo `.vcmacro` e `.vcprofile`. Design:
[`superpowers/specs/2026-10-21-developer-mode-design.md`](superpowers/specs/2026-10-21-developer-mode-design.md).

## Linha de comando

```
V-Clicker.exe --macro <arquivo.vcmacro | nome> [--var nome=valor]... [--log arquivo.jsonl] [--exit-when-done]
V-Clicker.exe --profile <app.exe | nome> [--log arquivo.jsonl] [--exit-when-done]
V-Clicker.exe --help
```

| Argumento | O que faz |
| --- | --- |
| `--macro` | Abre a macro no Macro Studio e aperta **Executar**. Aceita um caminho ou o nome de uma macro salva na pasta `Macros`. |
| `--profile` | Escolhe o aplicativo do perfil no Window Lock (carregando a configuração dele) e aperta **Iniciar**. Aceita o executável (`RobloxPlayerBeta.exe`) ou o nome do perfil (`Roblox`). |
| `--var nome=valor` | Valor de uma variável `{{nome}}` da macro. Pode repetir. O valor pode conter `=`. |
| `--log arquivo` | Grava os eventos desta execução nesse arquivo (JSON Lines, acrescentando). |
| `--exit-when-done` | Fecha o V-Clicker quando a execução termina e devolve um código de saída. |

A linha de comando usa a **mesma janela e as mesmas proteções** dos botões: F8 para, o indicador aparece, o Window
Lock pausa no Alt+Tab, bloquear o Windows para, a revisão de segurança do SafeClick aparece para uma macro com ações
impactantes ainda não aprovada (não há como pulá-la pela linha de comando) e o limite de duração vale.

Com o V-Clicker **já aberto**, a linha de comando não age e sai com o código 3: feche-o antes.

O V-Clicker é um aplicativo de janela: para esperar o fim num script, use `start /wait` (cmd) ou
`Start-Process -Wait -PassThru` (PowerShell). Mensagens de erro vão para a saída de erro de quem chamou.

```powershell
$p = Start-Process V-Clicker.exe -ArgumentList '--macro','Relatório','--var','mes=10','--log','run.jsonl','--exit-when-done' -Wait -PassThru
$p.ExitCode
```

### Códigos de saída (com `--exit-when-done`)

| Código | Significado |
| --- | --- |
| 0 | Terminou (macro completa, ou todos os cliques pedidos) |
| 1 | Parado pelo usuário (F8, Parar, fechar a janela) |
| 2 | Argumentos inválidos; macro ou perfil não encontrado; macro ilegível; variável sem valor |
| 3 | O V-Clicker já estava aberto |
| 4 | Não começou (aplicativo fechado, revisão de segurança cancelada, janela não veio para a frente…; a janela diz o motivo) |
| 10 + n | Outra parada: `n` é o motivo (`MacroStopReason` para macros, `StopReason` para o clicker); o log traz o nome |

Valores de `n` (código = 10 + n). Macro: 2 janela fechada, 3 pausa longa demais, 4 campo de senha, 5 entrada recusada pelo
Windows, 6 computador bloqueado ou suspenso, 7 passo desconhecido, 8 interrompida durante um arraste, 9 condição sem
resposta no tempo, 10 alvo não encontrado no tempo, 11 limite de duração. Clicker: 2 atalho solto (modo
segurar), 3 entrada recusada, 4 perdeu o foco, 5 janela fechada, 6 pausa longa demais, 7 computador bloqueado ou
suspenso.

## Logs estruturados

**Modo desenvolvedor** (Opções, no modo avançado) grava toda execução em `Logs\v-clicker-AAAA-MM-DD.jsonl`, ao lado
das configurações. `--log` grava a execução da linha de comando no arquivo indicado.

Um objeto JSON por linha, UTF-8:

```json
{"time":"2026-10-21T14:03:12.481-03:00","event":"run.start","kind":"macro","name":"Relatório","file":"C:\\...\\Relatório.vcmacro","app":"msedge.exe","repeat":60,"steps":7,"limitMinutes":0}
{"time":"2026-10-21T14:03:15.502-03:00","event":"step","index":1,"iteration":1,"kind":"Key"}
{"time":"2026-10-21T14:05:40.117-03:00","event":"run.stop","kind":"macro","reason":"Completed","iteration":60,"step":7,"elapsedMs":147636}
{"time":"2026-10-21T14:10:02.004-03:00","event":"run.start","kind":"clicker","cps":15,"button":"Left","double":false,"repeat":0,"app":"RobloxPlayerBeta.exe"}
{"time":"2026-10-21T14:12:30.950-03:00","event":"run.stop","kind":"clicker","reason":"Manual","clicks":2215,"elapsedMs":148946}
```

| Evento | Campos |
| --- | --- |
| `run.start` | `kind` (`macro` ou `clicker`); macro: `name`, `file`, `app`, `repeat` (0 = até parar), `steps`, `limitMinutes`; clicker: `cps`, `button`, `double`, `repeat`, `app` (null sem Window Lock) |
| `step` | `index` (1, 2…), `iteration` (repetição), `kind` (tipo do passo, como em `StepKind`) |
| `run.stop` | `kind`, `reason` (nome do motivo); macro: `iteration`, `step`, `elapsedMs`; clicker: `clicks`, `elapsedMs` |
| `command.end` | `code`, `message`: a linha de comando terminou sem executar |

**Nunca** entram no log: o texto digitado, valores de variáveis, coordenadas ou títulos de janelas.

## Variáveis

Um passo **Digitar** pode conter `{{nome}}` (letras, números, `_` e `-`, até 40). O valor vem, nesta ordem, de
`--var nome=valor`, de uma linha `var.nome=valor` no cabeçalho do arquivo ou, no Macro Studio, de uma pergunta antes de
executar (lembrada até fechar o V-Clicker). Pela linha de comando, uma variável sem valor é o código 2: nada é
digitado com `{{nome}}` literal.

Embutidas, preenchidas no momento em que o passo digita: `{{iteration}}` (repetição: 1, 2…), `{{date}}`
(`AAAA-MM-DD`) e `{{time}}` (`hh:mm:ss`, 24 h).

## Formato `.vcmacro`

Texto UTF-8. A primeira linha é `V-Clicker macro 1`; depois, linhas `chave=valor` do cabeçalho até `[steps]`; depois,
um passo por linha. Linhas desconhecidas (de uma versão mais nova) são guardadas e escritas de volta sem mudança; um
passo desconhecido impede a execução.

### Cabeçalho

| Chave | Valor |
| --- | --- |
| `name` | Nome mostrado |
| `exe` | Executável do aplicativo (`msedge.exe`): a macro só age em janelas dele |
| `title` | Título da janela na gravação (só para avisos) |
| `client` | Tamanho da área da janela na gravação, `LxA` (só para avisos) |
| `repeat` | Repetições; `0` = até parar |
| `limit` | Limite de duração em minutos (opcional; 1 a 5999) |
| `var.<nome>` | Valor padrão de uma variável |

### Passos

Coordenadas são pixels da área interna da janela (sem bordas nem título), a partir do canto superior esquerdo.
Texto entre aspas aceita `\\`, `\"`, `\n` e `\t`. `in "<classe>" "<título>"` no fim de um passo de mouse indica uma
janela do mesmo aplicativo que não é a principal (um diálogo, ou um menu, de título vazio).

```
click left 812 440                         clicar (left | middle | right)
double left 812 440                        clique duplo
click left 812 440 find element "Button" "Baixar PDF" timeout=10000
click left 812 440 find element "Button" "OK" id "btnOk" timeout=10000
click left 812 440 find image <PNG em base64> timeout=10000
click left pending                         clique proposto pela IA, ainda sem ponto: nunca executa até ser capturado
drag left 100 200 300 200                  arrastar de (100, 200) até (300, 200)
hover 50 60                                passar o mouse
scroll -3 400 300                          rolar 3 entalhes para baixo (positivo = para cima)
type "Relatório {{mes}}"                   digitar
key Enter x3                               tecla (x3 = três vezes)
shortcut Ctrl+Shift+S                      atalho
wait 1500 recorded=1500                    esperar 1,5 s (recorded = tempo gravado)
password                                   campo de senha não gravado: a execução para aqui
waitfor element 120 80 "Button" "Baixar PDF" timeout=30000
waitfor color 120 80 3A7BD5 timeout=30000
waitfor window "#32770" "Salvar como" timeout=30000
```

- Nomes de teclas: os de `System.Windows.Forms.Keys` (`F5`, `Delete`, `PageDown`, `A`…), `0`–`9`, `Enter`,
  `Backspace`, `Esc`, ou `VK` + código hexadecimal para teclas sem nome; modificadores `Ctrl+`, `Alt+`, `Shift+`.
- `timeout` em ms: 1000 a 3600000. `waitfor` aceita `was=` e `recorded=` (a espera que a condição substituiu).
- Alvos por imagem: PNG quadrado de 32, 64 ou 96 px.
- Valores fora da faixa são limitados ao ler.

## Formato `.vcprofile`

```
V-Clicker profile 1
exe=RobloxPlayerBeta.exe
name=Roblox
used=2026-10-21T17:00:00.0000000Z
IntervalMs=66.666666666666671
SpeedInCps=True
JitterPercent=0
Button=Left
DoubleClick=False
RepeatCount=0
UseFixedPosition=False
X=0
Y=0
StartDelaySeconds=1
HoldMode=False
LockStopOnFocusLoss=False
[macro]
V-Clicker macro 1
name=Coletar
...
```

As chaves de configuração são as mesmas do `settings.ini` (validadas igual); outras chaves são ignoradas. Na
exportação, cada macro do aplicativo vem depois de uma linha `[macro]`, no formato `.vcmacro`.
