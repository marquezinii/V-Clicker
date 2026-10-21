# V-Clicker: Macro Studio V1 — design

Data: 2026-10-21 · Estado: aprovado

## Intenção

O Macro Studio é a primeira das três funcionalidades da fase de produtividade, nesta ordem: **Macro Studio** →
**Conditional Click** → **Smart Target** (veja [ROADMAP](../../ROADMAP.md)). Cada uma funciona sozinha; as duas
seguintes tornam as mesmas macros mais inteligentes, sem mudar o que já foi gravado.

O público é quem repete a mesma sequência de cliques e teclas num sistema: o funcionário que baixa 60 relatórios
toda sexta-feira. Ele não toma decisões, só repete. Ele não programa e não vai assistir a um tutorial.

Prioridade que manda em todas as decisões: **confiabilidade antes de velocidade**. A macro pode alterar dados em
sistemas reais; é melhor parar e explicar do que continuar no lugar errado.

## Cenário que define o sucesso

O funcionário abre o Macro Studio, clica em **Gravar**, escolhe o sistema interno na lista de aplicativos e, depois
da contagem de 3 s, faz o processo uma vez: `Enter` para abrir o registro já selecionado, clique em *Gerar
relatório*, espera o processamento, clique em *Baixar PDF*, volta à lista, `↓` para o próximo registro. Aperta F8.

O Studio mostra a sequência em linguagem comum, avisa que há uma pausa longa (ele ficou 25 s distraído) e oferece
encurtá-la. Ele encurta, salva como "Relatórios de sexta", define 60 repetições, seleciona o primeiro registro e
clica em **Executar**. A macro roda; o indicador mostra `Relatórios de sexta · 12/60`. Se ele der Alt+Tab, a macro
pausa; se ele apertar F8, para na hora. Na sexta seguinte, abre a macro salva e executa.

Ele grava e executa a primeira macro sem tutorial.

## Objetivos, não objetivos e anti-objetivos

**Objetivos**
- Gravar, ver, editar, salvar e repetir sequências de mouse e teclado com coordenadas e intervalos.
- A lista de passos é legível por qualquer pessoa: cada passo é uma frase.
- A execução faz exatamente a sequência exibida, com os tempos exibidos.
- Uma macro só age na janela à qual está vinculada (veja "Janela vinculada") e nos diálogos e menus dela.
- F8 interrompe tudo na hora, gravando ou executando.

Neste documento, **F8** e **F7** significam os atalhos de Parar e de Capturar configurados pelo usuário (F8 e F7 são
apenas o padrão). Todos os atalhos do V-Clicker ficam fora da gravação, com ou sem modificadores.

**Não objetivos (V1)**
- Esperar por condições (Conditional Click) e achar elementos (Smart Target): próximas entregas.
- Deslocar coordenadas a cada repetição. Navegar entre registros é feito pelo teclado que o usuário gravar; se o
  sistema não permitir, é uma limitação reconhecida da V1.
- Macros que passam por vários aplicativos.
- Variáveis, laços dentro da macro, condições, planilhas.
- Atalho global para executar uma macro (só pelo botão **Executar**).
- Agendamento.

**Anti-objetivos (seria fracasso mesmo funcionando)**
- A macro digitar ou clicar em outro aplicativo (o Discord recebendo texto que era para o sistema).
- Reduzir uma espera sem o usuário autorizar.
- O usuário precisar entender programação, seletores ou coordenadas de tela para usar.
- O modo casual mudar: a janela principal, o motor de cliques e o Window Lock continuam como estão.
- Capturar entrada do teclado ou do mouse fora de uma gravação iniciada pelo usuário.

## Restrições

- WinForms, .NET Framework 4.8, sem dependências de terceiros. Um único `.exe`. Assemblies do próprio .NET Framework
  (como UI Automation) são permitidos.
- Todo texto novo em português e inglês (`src/Loc.cs`).
- A gravação usa os ganchos de baixo nível do Windows para mouse e teclado (`WH_MOUSE_LL`, `WH_KEYBOARD_LL`). Eles
  ficam ativos **somente** entre Gravar e Parar e nunca no modo casual. Isso não injeta nada em outros programas.
  (A spec da etapa casual proíbe ganchos de baixo nível no modo casual; isso continua valendo.)
- O callback dos ganchos tem que ser curto (o Windows descarta ganchos lentos): ele só enfileira o evento; todo o
  resto, inclusive consultas de UI Automation, roda fora dele.
- Eventos de entrada injetados (flag `LLMHF_INJECTED` / `LLKHF_INJECTED`) são ignorados na gravação.
- A reprodução usa `SendInput`, como o motor de cliques. Texto sai com `KEYEVENTF_UNICODE` (independe do layout do
  teclado).
- Testes no executor atual (`tests/VClicker.Tests`), sem mover o mouse nos testes de unidade.

## Experiência

### Estado sem o HUD

O HUD é opcional. Com ele desligado, gravar e executar continuam igualmente claros:
- a janela do Studio tem uma linha de estado sempre visível (contagem regressiva, `Gravando`, `Gravação pausada ·
  volte para <app>`, `Executando · 12/60 · passo 3 de 7`, `Pausada · aguardando <app>`, motivo da parada);
- os bipes e o ícone da bandeja por estado, que já existem, valem também para o Studio: um bipe por segundo da
  contagem, um ao começar e outro ao parar ou pausar, e o ícone da bandeja com o estado e o texto da linha de estado
  na dica.
Com o HUD ligado, o HUD mostra o mesmo texto da linha de estado.

### Onde fica

Um botão **Macro Studio** na janela principal abre uma janela própria. A janela principal não ganha outros
controles. Autoclicker e macro nunca rodam ao mesmo tempo: enquanto um roda, o outro não inicia (F6 é ignorado
durante uma macro; **Executar** é desativado durante o autoclicker).

A janela do Studio tem:
- no topo: a macro aberta (lista das salvas), **Nova**, **Renomear**, **Excluir** (vai para a Lixeira);
- uma barra: **Gravar**, **Executar**, repetições (número, padrão 1, ou "até eu parar"), **Salvar**;
- a lista de passos numerada, com ícone por tipo;
- um painel do passo selecionado, onde os valores são editados;
- ações de edição: apagar, duplicar, mover para cima/baixo, inserir espera, **Desfazer**/**Refazer**;
- avisos (texto digitado, pausa longa, janela de tamanho diferente) como faixas acima da lista.

Fechar o Studio ou trocar de macro com alterações não salvas pergunta se quer salvar.

### Gravar

1. **Gravar** abre a lista de aplicativos (a mesma do Window Lock). O aplicativo escolhido vem para a frente; o HUD
   mostra a contagem de 3 s e depois `Gravando · F8 para parar`.
2. O usuário faz o processo uma vez.
3. Para parar: F8 (em qualquer lugar) ou o botão **Parar** do Studio. Os dois mantêm o que foi gravado.
4. O Studio volta para a frente com a sequência.

Gravar numa macro que já tem passos pergunta: substituir ou acrescentar ao fim.

O que entra na gravação: só ações feitas na janela vinculada e nos diálogos e menus dela. Fica fora, sozinho, tudo
que acontece no Studio, no V-Clicker, nos atalhos do V-Clicker, em outros aplicativos e em outras janelas do mesmo
aplicativo (outra janela do Chrome, por exemplo). Quando o usuário sai da janela vinculada, o HUD mostra
`Gravação pausada · volte para <app>`; o tempo passado fora vira uma espera normal (pode ser encurtada depois).

O clique que seleciona o primeiro registro deve ser feito **antes** de Gravar. Se ele entrar na gravação, o usuário
apaga o passo; não há mecanismo especial para "passos que rodam uma vez".

### Tipos de passo (V1)

| Passo | Exemplo exibido | Observação |
| --- | --- | --- |
| Clique | `Clicar (esquerdo) em 812, 440` | esquerdo, direito ou do meio |
| Clique duplo | `Clique duplo em 812, 440` | dois cliques dentro do tempo de clique duplo do Windows, no mesmo ponto |
| Arrastar | `Arrastar de 100, 200 até 400, 200` | botão pressionado durante o movimento |
| Passar o mouse | `Passar o mouse sobre 300, 210` | veja "Movimentos" |
| Rolar | `Rolar 3 para baixo em 500, 400` | notches seguidos no mesmo ponto viram um passo |
| Digitar | `Digitar "Relatório mensal"` | tudo que produz um caractere: com ou sem Shift, e AltGr (que o Windows entrega como Ctrl+Alt) |
| Tecla | `Tecla Enter`, `Tecla Shift+Tab` | Enter, Tab, Esc, setas, F1–F12, Delete, Backspace, Home/End, PgUp/PgDn; com ou sem Shift |
| Atalho | `Atalho Ctrl+C` | tecla com Ctrl e/ou Alt que não produz um caractere (veja as decisões do construtor) |
| Esperar | `Esperar 8 s` | veja "Tempos" |
| Senha não gravada | `Senha não gravada` | veja "Dados sensíveis"; na execução, **para** a macro |

As coordenadas exibidas são relativas ao canto da área útil da janela (sem barra de título), não à tela.

### Movimentos

- Movimentos que só levam o cursor até um clique não viram passo; na execução o cursor vai direto ao ponto do clique.
- **Arrastar** vira passo sempre que um botão estava pressionado durante o movimento.
- **Passar o mouse** vira passo só quando o cursor parou por **1 s ou mais** num ponto e o passo seguinte não é um
  clique, rolagem ou arrasto naquele mesmo ponto (até 4 px). Paradas seguidas sem nada entre elas viram um único
  passo (a última). Na dúvida, não gera: o passo é fácil de inserir e de apagar, e o aviso da lista lembra disso.

### Tempos

Gravar fielmente → mostrar os passos → oferecer otimização opcional → executar exatamente a sequência exibida.

- Todo intervalo de **0,25 s ou mais** entre duas ações vira um passo `Esperar`, arredondado a 0,1 s.
- Intervalos menores não aparecem; entre dois passos quaisquer a execução garante um espaçamento **mínimo** de
  **50 ms**, para o aplicativo processar a ação anterior. Dentro de `Digitar`, o mínimo entre caracteres também é
  50 ms.
- Os tempos são uma agenda, não somas: cada passo tem um horário de início calculado a partir do início da
  repetição (esperas exibidas + espaçamentos mínimos). Ações com duração própria (`Digitar`, tecla repetida,
  arrastar) já têm essa duração na agenda, e um atraso não se acumula de passo em passo. Quando uma ação passa do
  horário, o próximo passo começa 50 ms depois de ela terminar. Pausas (foco perdido) empurram a agenda: o tempo
  pausado não é "recuperado" com passos mais rápidos.
- O Windows não garante precisão de milissegundos; os tempos são o ritmo pedido, não uma promessa de precisão.
- O tempo entre a última ação e o fim da gravação (F8, ou sair do aplicativo para clicar em **Parar**) vira uma
  última espera, para que a repetição seguinte não comece antes de o sistema terminar.
- **Pausa longa detectada:** se houver esperas de **5 s ou mais**, aparece a faixa do protótipo aprovado na conversa
  ("Você pode reduzir esperas desnecessárias para acelerar a macro. Confira se alguma delas é necessária para o
  sistema carregar.") com **Definir otimização de pausas**. Ele abre uma lista dessas esperas, cada uma com caixa de
  seleção (todas **desmarcadas**) e o novo valor (padrão 1 s). Nada muda sem o usuário marcar e confirmar.
- Toda espera guarda o tempo gravado. Uma espera alterada mostra `Esperar 1 s (gravado: 25 s)` e **Restaurar**.

### Dados sensíveis

- Ao gravar uma tecla de caractere, o Studio consulta (UI Automation) se o campo com foco é de senha. Se for, as
  teclas daquele campo **não são gravadas**; no lugar entra o passo `Senha não gravada`, destacado. A detecção
  depende de o aplicativo marcar o campo como senha; o aviso diz isso.
- Na execução, chegar a um passo `Senha não gravada` **para** a macro com o motivo "A macro chegou a um campo de
  senha que não foi gravado. Digite a senha você mesmo e apague este passo, ou faça o login antes de executar." Ela
  nunca continua depois de uma autenticação incompleta. Enquanto a macro tiver esse passo, a faixa do editor avisa
  que ela vai parar ali.
- Quando a macro tem passos `Digitar`, uma faixa fixa avisa: "Esta macro contém texto digitado. Ele fica salvo em
  texto simples no seu computador. Não grave senhas nem dados confidenciais."

### Editar

- Selecionar um passo mostra seus valores no painel: botão, ponto, texto, tecla, duração.
- **Capturar** (passos com ponto): uma janela do aplicativo da macro vem para a frente e o usuário aperta F7 com o
  cursor no ponto desejado; o ponto vira relativo à janela sob o cursor, que precisa ser essa janela ou um diálogo ou
  menu dela.
- Inserir espera, apagar, duplicar, mover; tudo com Desfazer/Refazer (vários níveis, enquanto o Studio estiver aberto).
- Uma macro sem passos não executa.

### Executar

1. **Executar** procura uma janela do aplicativo da macro (pelo executável). Uma só: vem para a frente. Várias: a
   lista do Window Lock filtrada, e a escolhida vira a janela vinculada desta execução. Nenhuma: aviso "Abra <app> e tente de novo". Aplicativo como administrador: recusa,
   com a mesma mensagem do Window Lock.
2. Se a área útil da janela tem tamanho diferente do gravado, aviso antes de começar: "A janela de <app> está com
   outro tamanho (gravado: 1280×720, agora: 1024×600). Os cliques podem cair em outro lugar." **Executar mesmo
   assim** / **Cancelar**.
3. Contagem de 3 s no HUD, depois a sequência; o passo atual fica destacado na lista. HUD:
   `<nome> · 12/60` (ou `<nome> · 12` em "até eu parar").
4. Ao terminar ou parar, o Studio volta e diz o motivo: concluída, parada pelo usuário (F8 ou **Parar**),
   janela fechada, pausa longa demais, aplicativo inacessível.

**Janela vinculada.** Uma macro age numa **instância** de janela, não num aplicativo inteiro:
- Na gravação, é a janela escolhida em **Gravar**; na execução, a escolhida (ou encontrada) em **Executar**. O
  arquivo guarda o executável, não a janela, porque a janela de ontem não existe mais hoje.
- "Dela" quer dizer: a própria janela e as janelas do mesmo processo que ela possui (diálogos como "Salvar como",
  cuja janela dona, subindo a cadeia de donos visíveis, é a vinculada) ou que surgem enquanto ela está na frente
  (menus e listas suspensas). Outra janela independente do mesmo aplicativo **não** conta: outra janela do Chrome
  com o Discord web é tratada como outro aplicativo.

**Proteção a cada passo** (mesmas regras do Window Lock, ligadas sempre, mesmo com o Window Lock casual desligado):
- Antes de cada passo de mouse, a janela-âncora do passo (veja abaixo) precisa existir e estar visível; o ponto
  precisa cair dentro da área útil dela; a janela em primeiro plano precisa ser a vinculada ou uma janela dela; e
  a janela que está de fato sob o ponto precisa ser a âncora (nada cobrindo, nem o próprio Studio, nem uma
  notificação). Se algo cobre o ponto: pausa, `Algo está cobrindo <app>`.
- Antes de cada passo de teclado, a janela em primeiro plano precisa ser a vinculada ou uma janela dela.
- Se não estiver: **pausa** (`Aguardando <app>`), retoma sozinha quando voltar, depois de 200 ms de estabilidade
  (como no Window Lock). Pausada por mais de 5 min: para.
- Janela vinculada fechada: para.
- Um arrasto também precisa ser solto dentro da mesma janela. Se a janela sair da frente no meio do arrasto, o botão
  é solto e a macro para (não fica pausada com o botão pressionado). Um arrasto solto em outro aplicativo durante a
  gravação não é gravado.
- Um passo que aperta um atalho do V-Clicker (por exemplo F8, que pararia a macro) impede a execução, com aviso.
- Sono ou bloqueio do PC: para (como hoje).
- F8 para na hora, inclusive no meio de uma espera ou de um `Digitar` (entre dois caracteres). Nenhum botão do mouse
  ou tecla fica pressionado depois de parar: o player solta tudo que pressionou.

**Janela-âncora.** Cada passo de mouse é gravado relativo à janela de nível superior sob o cursor, que precisa ser a
vinculada ou uma janela dela. Se for a vinculada, o passo guarda só isso, e na execução usa a vinculada. Se for outra
(um diálogo como "Salvar como", um menu), o passo guarda a classe **e o título** dela, e na execução usa a janela
visível da vinculada com a mesma classe e o mesmo título que estiver mais à frente
(menus, que não têm título, usam só a classe). Se ela ainda
não existe (o diálogo não abriu), o passo **pausa** como acima até ela aparecer ou o limite de 5 min. Isso não é o
Conditional Click: é só não clicar numa janela que não está lá.

### Navegadores

Navegadores (Chrome, Edge, Firefox, Brave, Opera, Vivaldi) são permitidos, com as regras de janela vinculada acima.
Limite reconhecido da V1, dito ao usuário sem prometer o que o programa não faz:
- Trocar de **aba** dentro da janela vinculada não é detectado. Se a janela continuar em primeiro plano, a macro
  continua executando, na aba que estiver aberta.
- Ao gravar ou executar uma macro num navegador, uma faixa avisa: "Em navegadores, a macro fica presa a esta
  janela, mas não percebe troca de aba. Use uma janela só para este sistema e não troque de aba enquanto a macro
  roda." O texto nunca diz que a macro protege contra troca de aba.
- O título da janela (que muda com a aba e com a página) **não** é usado como proteção na V1.

## Arquivos

- Uma macro por arquivo, em `%APPDATA%\V-Clicker\Macros\<nome>.vcmacro`; no modo portátil, `Macros\` ao lado do
  `.exe` (a mesma regra do `V-Clicker.ini`).
- Texto simples, legível, com cabeçalho de versão do formato. Guarda: nome, a identificação do alvo (na V1: o
  executável, mais o título e o tamanho da área útil no momento da gravação, só para avisos), repetições, passos
  (com o tempo gravado das esperas).
- A identificação do alvo é um bloco próprio do arquivo, que versões futuras podem enriquecer (por exemplo, com a
  página ou a aba de um navegador) sem mudar os passos.
- Ler um arquivo nunca derruba o programa: arquivo inválido mostra "Não foi possível abrir <nome>" e não o altera.
- Um tipo de passo desconhecido (arquivo de uma versão mais nova) bloqueia a execução com aviso; nunca é pulado em
  silêncio. Isso permite que Conditional Click e Smart Target acrescentem tipos de passo sem quebrar arquivos antigos.
- Nomes de macro viram nomes de arquivo válidos; nome repetido pede outro.

## Preparação para as próximas entregas

- Cada passo tem um tipo; o player executa uma lista de passos. Conditional Click entra como um tipo novo
  (`Esperar até…`); Smart Target entra como outra forma de dizer onde fica o ponto de um passo de mouse.
- A verificação "esta é a janela certa?" fica num único lugar do player, chamado antes de cada passo. Identificar
  a aba ou a página de um navegador no futuro é uma regra a mais nesse lugar e um campo a mais na identificação do
  alvo, sem reconstruir o Studio.
- Nada disso é construído agora; só não pode ser impedido pelo formato ou pelo player.

## O que fica para o construtor

- A estrutura de classes (gravador, conversão de eventos em passos, player, formato, janela), desde que a conversão
  de eventos em passos e o player sejam testáveis sem mover o mouse (eventos sintéticos, sink falso).
- O layout exato da janela do Studio, seguindo o tema atual (`src/Theme.cs`, `src/Controls.cs`).
- A sintaxe do arquivo `.vcmacro`.
- O espaço de coordenadas: o processo é "system DPI aware" (`src/app.manifest`); gravação, captura e reprodução
  precisam usar o mesmo espaço em monitores com escalas diferentes (por exemplo, tornando as threads do gravador e do
  player "per-monitor aware"). Verificar em 1,5× (a máquina de testes).
- Teclas fora da tabela de "Tecla" (por exemplo, Print Screen ou teclas de mídia) podem ser ignoradas na gravação.

Decisões tomadas pelo construtor no planejamento (dentro do espírito acima):
- Dentro de um `Digitar`, pausas de até 1 s entre caracteres não quebram o texto (o ritmo de digitação não importa
  para o sistema); a partir de 1 s, o texto vira dois `Digitar` com uma espera no meio.
- Teclas repetidas seguidas (segurar `↓`, ou apertar várias vezes com menos de 0,25 s entre elas) viram um passo só,
  `Tecla ↓ ×5`.
- Atalhos que trocam de janela ou são do sistema não são gravados: Alt+Tab, Alt+Shift+Tab, Alt+Esc e qualquer
  combinação com a tecla Windows. Gravados, eles tirariam a macro da janela vinculada na execução. Por isso o
  `Atalho` da V1 é Ctrl e/ou Alt (com ou sem Shift).
- Alt sozinho (que abre o menu em alguns programas) e a rolagem horizontal não são gravados na V1.
- Teclas mortas (acentos do ABNT2) são compostas: `´` seguido de `a` vira `á` no `Digitar`.

## Critérios de aceitação

- Gravar o cenário acima num aplicativo de teste produz exatamente: `Tecla Enter`, `Esperar`, `Clicar`, `Esperar`,
  `Clicar`, …, `Tecla ↓`, sem movimentos soltos, sem cliques no Studio, sem a tecla F8.
- Texto digitado vira um único `Digitar`; Ctrl+C vira `Atalho Ctrl+C`; campo de senha vira `Senha não gravada`.
- A reprodução repete N vezes com os tempos exibidos: cada passo começa até 50 ms depois do horário da agenda, e o
  desvio não cresce ao longo das repetições. Para em F8 em menos de 100 ms, sem deixar teclas ou botões pressionados.
- Um passo `Senha não gravada` para a execução com o motivo.
- Com o HUD desligado, a linha de estado do Studio mostra contagem, progresso e pausa.
- Alt+Tab durante a execução pausa antes do passo seguinte e nenhum passo cai em outro aplicativo; voltar retoma.
- Mover a janela-alvo entre gravação e execução não muda onde os cliques caem.
- Com a macro vinculada a uma janela do Chrome, trazer outra janela do Chrome para a frente pausa a macro.
- Uma macro salva, fechada e reaberta é idêntica; um arquivo corrompido não derruba o programa.
- O modo casual se comporta exatamente como antes (testes atuais passam sem mudança).
