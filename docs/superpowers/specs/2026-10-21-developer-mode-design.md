# Developer Mode

Item 9 do pedido: execução por linha de comando, formato de macros documentado, variáveis e logs estruturados. As
decisões foram tomadas por mim, sem perguntas, como o usuário pediu; as que fecham uma escolha estão marcadas como
**decisão**.

## Intenção

Tornar o V-Clicker útil em testes de software e automações repetitivas: um script consegue iniciar um perfil ou uma
macro, passar valores, esperar o fim e saber como terminou, e uma pessoa consegue ler e escrever macros à mão.

## Linha de comando

```
V-Clicker.exe --macro <arquivo.vcmacro | nome> [--var nome=valor]... [--log arquivo.jsonl] [--exit-when-done]
V-Clicker.exe --profile <app.exe | nome> [--log arquivo.jsonl] [--exit-when-done]
V-Clicker.exe --help
```

- **Mesma janela, mesmas proteções (decisão).** A linha de comando abre o V-Clicker normalmente e faz o que o botão
  faria: `--profile` escolhe o aplicativo no Window Lock (carregando o perfil) e aperta Iniciar; `--macro` abre a macro
  no Macro Studio e aperta Executar. F8, o indicador, a pausa no Alt+Tab, a parada ao bloquear o Windows, a revisão de
  segurança do SafeClick e o limite de duração valem igual. Não existe modo invisível.
- **Revisão de segurança:** uma macro com ações impactantes ainda não aprovada abre a revisão na tela; a linha de
  comando não tem como aprovar sozinha (**decisão**: uma automação não pode pular a única confirmação que existe).
- **Já aberto:** com o V-Clicker já aberto, a linha de comando não age (sai com o código 3) em vez de mandar a outra
  instância clicar (**decisão**: evita dois processos disputando o mouse e uma porta de comando entre processos).
- **`--exit-when-done`:** fecha o V-Clicker quando a execução termina, com um código de saída:

| Código | Significado |
| --- | --- |
| 0 | Terminou (macro completa ou repetições do clicker concluídas) |
| 1 | Parado pelo usuário (F8, Parar) |
| 2 | Argumentos inválidos, macro ou perfil não encontrado, variável sem valor |
| 3 | O V-Clicker já estava aberto |
| 4 | Não começou (aplicativo fechado, revisão de segurança cancelada, janela não veio para a frente…) |
| 10+ | Outro motivo de parada; o log diz qual (ex.: janela fechada, condição sem resposta, limite de duração) |

- Mensagens de erro de argumentos vão para o console de quem chamou (quando há um) e para o log.

## Variáveis

- Um passo **Digitar** pode conter `{{nome}}`. Nomes: letras, números, `_` e `-`.
- Valores: `--var nome=valor` na linha de comando; no Macro Studio, o valor padrão da macro (cabeçalho
  `var.nome=valor` no arquivo) ou, se não houver padrão, uma pergunta antes de executar.
- Embutidas: `{{iteration}}` (repetição atual, 1, 2…), `{{date}}` (aaaa-mm-dd) e `{{time}}` (hh:mm:ss), na hora em
  que o passo digita.
- Uma variável sem valor impede a execução (código 2 na linha de comando); nunca é digitado `{{nome}}` literalmente.
- A assinatura do SafeClick é a do arquivo: mudar o valor de uma variável na linha de comando não pede nova revisão
  (o texto digitado nunca foi o critério da revisão; os atalhos impactantes, sim).

## Logs estruturados

- Formato **JSON Lines** (um objeto por linha, UTF-8), eventos `run.start`, `step` (macros) e `run.stop`, com hora
  ISO 8601, tipo (clicker ou macro), aplicativo, repetição, índice e tipo do passo, motivo da parada, cliques e
  duração.
- **Nunca** contém o texto digitado nem valores de variáveis (**decisão**: logs costumam ser copiados para tickets).
- Onde: `--log arquivo` para aquela execução; com o **Modo desenvolvedor** ligado, toda execução também vai para
  `Logs\v-clicker-AAAA-MM-DD.jsonl` ao lado das configurações.

## Modo desenvolvedor (opção)

Uma opção nova no avançado, em Opções: **Modo desenvolvedor**. Ligado, grava os logs diários e mostra no Macro Studio o
botão **Copiar comando**, que copia a linha de comando que executa a macro aberta.

## Formato documentado

`docs/developer.md` documenta a linha de comando, os códigos de saída, os eventos do log e o formato `.vcmacro`
(cabeçalho, cada tipo de passo, coordenadas, janelas, alvos, condições, variáveis) e `.vcprofile`.
