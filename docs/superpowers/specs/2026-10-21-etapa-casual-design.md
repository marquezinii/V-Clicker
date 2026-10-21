# V-Clicker: etapa casual — design

Data: 2026-10-21 · Estado: aprovado

## Intenção

O V-Clicker atende dois públicos: o **casual/gamer**, que quer abrir o programa, escolher 15 CPS, apertar F6 e
continuar jogando, e o usuário de **produtividade**, que quer macros, condições e reconhecimento visual. A decisão do
dono do produto: primeiro tornar o V-Clicker um autoclicker casual excepcional, depois expandir para automação
(veja [ROADMAP](../../ROADMAP.md)).

Princípio que manda em todas as decisões abaixo: **cinco coisas extremamente bem feitas valem mais que vinte medianas.**
Nada de complexidade artificial, nada de prometer o que o programa não consegue medir, nada que comprometa a
confiabilidade do motor de cliques.

Esta etapa tem quatro entregas, nesta ordem:

1. **Window Lock** — restringir os cliques a um aplicativo e pausar/retomar sozinho em um Alt+Tab.
2. **Ícone da bandeja por estado** — clicando, pausado, parado.
3. **Mini-HUD** — indicador discreto, flutuante, opcional.
4. **CPS direto** — escolher o ritmo em cliques por segundo e ver "solicitado vs. enviado".

## Cenário que define o sucesso

O jogador vincula o V-Clicker ao Roblox uma vez, configura 15 CPS e aperta F6 dentro do Roblox. O HUD verde mostra
`15 CPS`. Ele dá Alt+Tab para o Discord: os cliques param na hora (nenhum clique cai no Discord), o HUD fica amarelo
com `Aguardando Roblox`, o contador é mantido. Ele volta ao Roblox: os cliques recomeçam sozinhos, sem um F6 novo.
No dia seguinte, abre o Roblox, aperta F6 e tudo funciona sem reconfigurar nada.

## Objetivos, não objetivos e anti-objetivos

**Objetivos**
- Window Lock confiável, com os mecanismos nativos do Windows e custo desprezível.
- Estado (clicando / pausado / parado) visível sem abrir a janela do V-Clicker.
- CPS como unidade principal, sem perda de precisão (15 CPS = 66,667 ms, não 67 ms).
- Painel honesto: mostra o que o V-Clicker *enviou*, nunca o que o jogo *reconheceu*.

**Não objetivos (desta etapa)**
- Perfis por aplicativo (próxima rodada, no ROADMAP).
- Seleção de janela por mira ou por atalho (ROADMAP).
- Clicar em janelas que não estão em primeiro plano (envio por mensagens à janela): a maioria dos jogos ignora, não prometer.
- Qualquer coisa da fase de produtividade.

**Anti-objetivos (seria fracasso mesmo funcionando)**
- Um clique cair em outro aplicativo com o Window Lock ligado (fora a janela de corrida descrita em "Proteção a cada clique").
- Uma sessão "armada" que começa a clicar sozinha quando o usuário abre o jogo.
- Injeção de código no jogo, mexer no renderizador, ganchos de entrada de baixo nível, captura de tela, reconhecimento
  de imagem, ou qualquer técnica que possa acionar anticheat.
- Monitoramento pesado ou contínuo; o HUD ou o Window Lock deixarem o motor mais lento ou menos preciso.
- A janela do casual virar um painel de opções: cada controle novo precisa merecer o lugar.
- Reescrever o motor de cliques. O motor atual (`src/ClickEngine.cs`) é sólido; mudanças nele são cirúrgicas.

## Restrições

- WinForms, .NET Framework 4.8, sem dependências de terceiros (como hoje). Um único `.exe`.
- Toda interação com outras janelas via Win32 nativo (P/Invoke em `src/Native.cs`).
- Todo texto novo em português e inglês (`src/Loc.cs`).
- Configurações continuam no `.ini` atual (`src/Settings.cs`), com leitura que nunca lança exceção e cai para o
  padrão em dado inválido. Arquivos `.ini` existentes precisam continuar abrindo com o mesmo comportamento.
- Testes no executor atual (`tests/VClicker.Tests`), que roda sem mover o mouse via `IClickSink`.
- Atalhos F6/F7/F8 (e personalizados) continuam globais e iguais; nada nesta etapa muda o que eles fazem fora do
  descrito aqui.

---

## 1. Window Lock

### O que o usuário vê

Um card novo na janela principal:

```
WINDOW LOCK
[ ] Restringir os cliques a um aplicativo
    Aplicativo:  [ícone] Roblox            [Escolher…]
    Ao perder o foco:  ( Pausar e retomar | Parar )
```

- **Desligado por padrão.** Desligado, o V-Clicker funciona exatamente como hoje (clica em qualquer janela).
- **Escolher…** abre uma lista das janelas abertas, com ícone e nome do aplicativo (e o título da janela). Selecionar
  um item vincula o aplicativo. É a única forma de seleção nesta etapa.
- **Ao perder o foco:** `Pausar e retomar` (padrão) ou `Parar`.
- Controles do card ficam desabilitados durante uma sessão (como os demais controles hoje, via `lockable`).

### Identidade: aplicativo salvo, janela por sessão

- **O que é salvo:** o nome do arquivo do executável (ex.: `RobloxPlayerBeta.exe`, comparação sem diferenciar
  maiúsculas) e um nome amigável para exibir (descrição do arquivo, ex.: "Roblox"; na falta, o nome do executável).
  Só o nome do arquivo, não o caminho: o Roblox instala cada versão em uma pasta diferente.
- **O que vale durante a sessão:** a janela *exata* que estava em primeiro plano quando o usuário apertou F6.
  Outra janela do mesmo programa não conta. Nunca reutilizar um identificador de janela salvo de outra sessão.

### Regras de execução

| Situação | Comportamento |
| --- | --- |
| F6 com o aplicativo vinculado em primeiro plano | Inicia e prende a sessão àquela janela. |
| F6 com outra janela em primeiro plano | **Recusa iniciar.** Aviso: "Ative a janela do Roblox para começar". Nada fica armado. |
| F6 com o lock ligado e nenhum aplicativo escolhido | Recusa iniciar. Aviso pedindo para escolher o aplicativo. |
| Janela da sessão perde o foco, modo `Pausar e retomar` | Pausa na hora. Sessão continua; contador mantido; estado `Pausado · Aguardando Roblox`. |
| Janela da sessão volta ao foco | Retoma sozinho, sem F6. Cliques "perdidos" na pausa são descartados (sem fila, sem rajada). |
| Janela da sessão perde o foco, modo `Parar` | Encerra a sessão, com aviso explicando o motivo. |
| F6 ou F8 a qualquer momento (inclusive pausado) | Encerra a sessão. Nada recomeça sozinho depois. |
| Janela da sessão fecha (ou o jogo é reaberto) | Encerra a sessão de verdade, com aviso. |
| Clique recusado pelo Windows | Encerra, como hoje (`StopReason.Blocked`). |
| Contagem regressiva inicial e a janela perde o foco | A contagem congela e continua de onde parou quando a janela voltar. No modo `Parar`, encerra. |
| Pausa dura mais de **5 minutos** | Encerra definitivamente, com aviso. Contado desde o início da pausa (avisos repetidos de foco não reiniciam a contagem). Valor fixo, sem opção na interface. |
| PC suspenso, hibernado ou bloqueado (Win+L) | Encerra definitivamente, sem retomada ao acordar ou desbloquear. Vale com ou sem Window Lock. |
| Botão **Iniciar** da janela ou da bandeja | Ver "Iniciar pelo botão" abaixo. |

### Iniciar pelo botão (janela principal ou bandeja)

Com o lock ligado, o V-Clicker está em primeiro plano quando o usuário clica em Iniciar. Então o botão, ao contrário
do F6, **traz a janela vinculada para a frente**:

1. Procura as janelas abertas do aplicativo vinculado (mesmos filtros da lista de seleção).
2. Nenhuma → recusa e informa ("Abra o Roblox para começar").
3. Várias → abre a lista de seleção filtrada a esse aplicativo; o usuário escolhe uma. Nunca escolhe sozinho.
4. Uma (ou a escolhida) → pede ao Windows para trazê-la ao primeiro plano e **confirma** que ela realmente recebeu o
   foco. Se o Windows recusar a troca, recusa iniciar e informa.
5. Inicia a sessão presa a essa janela, respeitando o atraso inicial; perda de foco durante a contagem segue a regra
   de pausa.

Nenhum clique acontece antes da janela correta estar confirmada em primeiro plano. Isso não é uma "sessão armada": o
usuário pediu o início explicitamente e o programa trouxe a janela certa para a frente. **F6 continua diferente:** só
inicia se o aplicativo já estiver em primeiro plano; nunca troca de janela.

**Pausa nunca deixa botão preso.** O motor hoje envia pressionar+soltar numa só chamada a `SendInput` e solta o botão
se a entrega for parcial; a pausa acontece só *entre* cliques, então essa garantia se mantém. Qualquer mudança no
motor precisa preservar isso.

### Proteção a cada clique

- **Imediatamente antes de cada clique**, o motor confere se a janela em primeiro plano ainda é a da sessão
  (chamada barata, ex.: `GetForegroundWindow`). Essa conferência é a garantia; não depende de eventos.
- Um aviso de mudança de foco do Windows (ex.: `SetWinEventHook` com `EVENT_SYSTEM_FOREGROUND`) pode ser usado para
  atualizar a interface/HUD na hora. Não é ele que impede o clique.
- Existe uma janela de corrida inevitável entre conferir o foco e o Windows entregar o clique. É aceita e documentada;
  não tentar "eliminar" com técnicas invasivas.
- **Posição fixa:** o ponto precisa estar dentro da área útil (client area) da janela da sessão.
  - Ao apertar F6: se estiver fora, recusa iniciar ("O ponto fixo está fora da janela do Roblox").
  - Durante a sessão (ex.: a janela foi movida): o clique não acontece e o estado vira pausado com esse motivo; volta
    sozinho quando o ponto estiver dentro de novo. *(Decisão do redator, ver "Decisões tomadas por mim".)*
- **Modo cursor:** confere só o foco. **Não** validar a posição do cursor: jogos escondem ou prendem o cursor, e uma
  validação dessas quebraria o uso principal.
- **Fechamento da janela:** a janela deixou de existir, ou o identificador agora pertence a outro processo
  (identificadores são reaproveitados), conta como "fechou".
- A conferência durante a pausa é periódica e leve (algo na ordem de dezenas de ms); o motor não fica girando em
  espera ativa enquanto pausado.

### Avisos quando o jogador está dentro do jogo

Recusa de início, encerramento no modo `Parar` e janela fechada acontecem com o jogador no jogo, onde o aviso da janela
principal não aparece. Por isso, além desse aviso *(decisão do redator)*:
- um **bipe de erro** próprio, mais grave que o bipe de parada (só se "Avisos sonoros" estiver ligado);
- o **HUD** (se ligado) mostra o motivo curto por ~2 s, no estado "parado", e some. Um F6 recusado conta como
  "primeiro F6" para o HUD aparecer.

Sem notificações do Windows, como no resto do design.

### Aplicativos da Microsoft Store

Não é requisito desta etapa. Janelas hospedadas pelo `ApplicationFrameHost.exe` ficam fora da lista de seleção, para
nunca vincular "qualquer app da Store" por engano. *(Decisão do redator.)*

### Modo Segurar

Com o lock ligado, o modo Segurar segue as mesmas regras: só começa com a janela vinculada em primeiro plano, pausa se
ela perder o foco enquanto a tecla está pressionada, e soltar a tecla encerra como hoje.

### Contadores

- O motor distingue **tempo de sessão** (desde o F6, incluindo pausas) de **tempo clicando** (sem as pausas e sem a
  contagem regressiva). Médias de CPS usam só o tempo clicando.
- O bloco TEMPO do painel mostra o tempo de sessão. *(Decisão do redator.)*

### Formato no motor (orientação, não prescrição)

Hoje o motor tem as fases `Idle`, `Countdown` e `Clicking`. A forma natural é uma fase `Paused` e uma verificação
injetável (como `IClickSink` e `isKeyDown` já são), que o motor consulta antes de cada clique e que responde
"pode clicar / pausar (com motivo) / encerrar (com motivo)". Isso mantém o motor testável sem janelas reais. Novos
`StopReason` cobrem "perdeu o foco" (modo `Parar`) e "janela fechada". O construtor decide os nomes e o desenho exato.

---

## 2. Ícone da bandeja por estado

- O ícone muda com o estado: **verde** (clicando), **amarelo** (pausado ou contagem regressiva), **cinza/normal** (parado).
- A dica do ícone mostra o detalhe (ex.: `V-Clicker · Pausado · Aguardando Roblox`).
- As variações são desenhadas em código a partir do ícone atual (ex.: um ponto colorido), sem arquivos novos.
- Hoje o ícone só aparece com "Minimizar para a bandeja" ligado. Esse comportamento não muda nesta etapa.

---

## 3. Mini-HUD

### O que o usuário vê

Uma pílula pequena e semitransparente, estética de indicador do Windows 11, no canto da tela:

| Estado | Visual |
| --- | --- |
| Clicando | Verde · `15 CPS` (o CPS solicitado) |
| Pausado | Amarelo · `Pausado`, ou `Aguardando Roblox` quando a pausa é do Window Lock |
| Contagem regressiva | Amarelo · `Começa em 2,4 s` *(decisão do redator)* |
| Parado | Cinza · `Parado` por ~2 s depois que a sessão termina, e então some |

- **Aparece só depois do primeiro F6** da execução do programa. Não aparece ao abrir o V-Clicker.
- **Opcional:** uma caixa liga/desliga nas opções, e a escolha de **um dos quatro cantos** (padrão: superior direito).
  Sem arrastar, sem mais personalização.
- **Sem notificações do Windows** e sem animações pesadas: só troca de cor e texto.

### Como é feito

- Uma janela própria e independente do V-Clicker: sempre no topo, transparente a cliques, nunca recebe foco nem
  aparece na barra de tarefas/Alt+Tab (estilos como `WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE |
  WS_EX_TOOLWINDOW | WS_EX_TOPMOST`, mostrada sem ativar).
- Isso protege o Window Lock: o HUD nunca vira a janela em primeiro plano, e um clique em posição fixa embaixo dele
  atravessa até o jogo. Isso precisa ser verificado no teste de integração.
- Atualiza só quando o estado ou o texto mudam (aproveitando o temporizador de 100 ms que já existe na interface); não
  desenha por quadro.
- **Monitor:** o do aplicativo vinculado quando o lock está ligado; senão, o da janela em primeiro plano no F6.
  *(Decisão do redator.)*
- **Limite aceito:** janelas comuns não aparecem por cima de jogos em tela cheia *exclusiva*. Nesse caso nada quebra:
  ficam os bipes e o ícone da bandeja. O Roblox usa tela cheia sem bordas, onde o HUD deve aparecer; confirmar em teste
  manual.

---

## 4. CPS direto, solicitado vs. enviado

### Escolher o ritmo

Card "Intervalo entre cliques" passa a ter um seletor `CPS | Intervalo`, com **CPS como padrão**:

- **Modo CPS:** um campo numérico de CPS e, ao lado, o intervalo equivalente (`≈ 66,67 ms`). Atalhos
  `5 · 10 · 15 · 20 · 50 CPS` substituem os atuais `1/s · 10/s · 50/s · 100/s · 500/s`.
- **Modo Intervalo:** os campos h / min / s / ms de hoje.
- **Uma velocidade só.** Os dois modos editam o mesmo valor salvo; trocar de modo nunca muda o ritmo nem arredonda.
- A variação aleatória (±0–50 %) continua igual e vale para os dois modos.
- O modo escolhido (CPS ou Intervalo) é lembrado.
- **Clique duplo:** CPS conta *ações* (um duplo = 1), como o motor e os atalhos já fazem hoje. Com Duplo selecionado, a
  conversão ao lado deixa explícito o total enviado ao Windows (ex.: `≈ 66,67 ms · 30 cliques/s`). *(Decisão do redator.)*
- **Valor canônico:** o que fica salvo é o intervalo em ms com precisão total (ex.: 1000/15, gravado de forma que
  releia idêntico). Os campos só *exibem* arredondado; um campo só altera o valor salvo quando o usuário edita
  aquele campo. Se o intervalo estiver fora da faixa do campo de CPS (abaixo de 0,1 CPS), o campo de CPS mostra `< 0,1`
  e a conversão indica usar o modo Intervalo. *(Decisão do redator.)*

### Precisão

- O motor já agenda em ticks do `Stopwatch` (bem abaixo de 1 ms). O único ponto inteiro é o intervalo em ms
  (`ClickSettings.IntervalMs`, `ClickPlan.IntervalMs`, `Interval`). Esse valor passa a aceitar fração de milissegundo,
  para 15 CPS ser 66,667 ms de verdade. Ajuste interno pequeno, sem reescrever o motor.
- Faixas: intervalo continua de 1 ms a 24 h. CPS aceita 0,1 a 1000 com uma casa decimal *(decisão do redator)*. Ritmos
  abaixo de 0,1 CPS são feitos no modo Intervalo. O campo de ms passa a aceitar decimais (até 3 casas), para que trocar
  de modo não perca precisão.
- **Compatibilidade do `.ini`:** arquivos antigos (`IntervalMs=100`) leem igual. Números sempre gravados com a cultura
  invariável (ponto decimal), como já é feito. Uma versão antiga do V-Clicker que leia um valor fracionário cai no
  padrão (não trava) — aceitável.

### Painel durante a execução

O bloco "Cliques/s" vira a comparação:

```
SOLICITADO        ENVIADO AO WINDOWS
15,0 CPS          14,9 CPS
O valor enviado não representa necessariamente os cliques reconhecidos pelo jogo.
```

- **Enviado** = taxa recente de cliques que o Windows *aceitou* (`SendInput` retornou sucesso), com a suavização que
  o painel já usa. Nunca chamar de "cliques reconhecidos" nem "cliques do jogo".
- Durante uma pausa do Window Lock, Enviado mostra `—` em vez de cair para zero.
- Com variação aleatória, Solicitado é o ritmo médio configurado.
- Parado: o painel volta ao formato de repouso (o construtor decide como, mantendo o visual atual).

---

## Configurações novas (`.ini`)

Nomes a critério do construtor; o conteúdo é:

- Window Lock ligado/desligado; executável vinculado; nome amigável; comportamento ao perder o foco (pausar/parar).
- HUD ligado/desligado *(padrão: ligado — decisão do redator)*; canto (padrão: superior direito).
- Modo de entrada do ritmo (CPS/Intervalo; padrão: CPS).
- Intervalo com fração de ms (mesma chave `IntervalMs`).

Tudo validado no `Validate()` com fallback seguro, como as chaves atuais.

## Verificação esperada

- **Unitários (sem janelas reais):** motor pausa, retoma sem rajada, descarta cliques da pausa, encerra por janela
  fechada e no modo `Parar`, congela a contagem regressiva; tempo clicando exclui pausas; F6/Stop durante pausa
  encerra; conversão CPS ↔ ms sem perda; `.ini` antigo e novo, valores inválidos.
- **Integração (`-Integration`, move o mouse):** com uma janela de teste vinculada, nenhum clique chega a outra janela
  após trocar o foco; o HUD não rouba foco e deixa o clique atravessar.
- **Manual:** Roblox real (tela cheia sem bordas): HUD visível, Alt+Tab pausa/retoma, fechar o Roblox encerra.
- README (PT e EN), CHANGELOG e a captura de tela atualizados.

## Decisões tomadas por mim (redator), para revisar

- Ponto fixo que sai da janela durante a sessão → pausa com motivo, em vez de encerrar.
- TEMPO no painel = tempo de sessão; médias usam o tempo clicando.
- HUD durante a contagem regressiva: amarelo `Começa em X s`.
- HUD ligado por padrão (só aparece depois do primeiro F6).
- Monitor do HUD: o do aplicativo vinculado, senão o da janela em primeiro plano.
- CPS de 0,1 a 1000 com uma casa decimal; campo de ms com até 3 casas decimais.
- Salvar só o nome do executável (sem caminho), por causa das pastas versionadas do Roblox.
- Avisos dentro do jogo: bipe de erro próprio + motivo curto no HUD por ~2 s.
- Apps da Microsoft Store (`ApplicationFrameHost`) fora da lista nesta etapa.
- CPS conta ações (duplo = 1), com o total de cliques explícito no modo Duplo.
- Valor salvo = intervalo com precisão total; campos só exibem arredondado.

## Deixado para o construtor

- Layout exato dos cards novos na janela principal (ela já usa rolagem em telas baixas; manter isso funcionando).
- Nomes de classes, chaves do `.ini`, `StopReason`/fases novas.
- Como desenhar as variações do ícone da bandeja e a pílula do HUD, dentro do tema escuro existente (`src/Theme.cs`).
- Intervalo exato da conferência durante a pausa.
- Como a lista de janelas filtra entradas inúteis (janelas invisíveis, sem título, "cloaked", o próprio V-Clicker).
