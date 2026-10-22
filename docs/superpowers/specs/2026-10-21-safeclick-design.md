# V-Clicker: SafeClick V1 — design

Data: 2026-10-21 · Estado: aprovado, com dois ajustes já incorporados (aprovação por instância local, resultados
relativos à captura e estado inconclusivo)

## Intenção

Primeiro dos cinco itens da nova rodada (SafeClick → Profiles → Interface adaptativa → Developer Mode → AI Macro
Builder; veja [ROADMAP](../../ROADMAP.md)). O SafeClick também serve de base para o AI Macro Builder: uma macro proposta
pela IA será revisada com a mesma simulação.

O objetivo é que o usuário olhe para uma macro e pense: "agora eu entendo onde ela vai clicar, quais ações vai executar
e quais riscos existem", antes de apertar Executar.

**Decisão central do usuário:** o SafeClick V1 é um sistema de **pré-visualização e análise de segurança**, não um
segundo motor de automação. A simulação usa os mesmos cálculos de coordenadas e de identificação de alvos do player;
não existe um segundo mecanismo que possa dar um resultado diferente da execução real.

## O que já existe (e fica como está)

- **Parada de emergência:** o atalho Parar (F8, configurável) para o autoclicker e as macros; uma macro não pode
  pressionar os atalhos do V-Clicker (`MacroStudio.BeginRun`).
- **Bloqueio ou suspensão do Windows:** encerra o autoclicker e a macro (`MainForm.OnSessionSwitch`,
  `OnPowerModeChanged`). **Decisão do usuário:** manter o encerramento, sem retomada automática ao desbloquear.
- **Loops:** macros não têm laços nem saltos; "esperar até" e alvos inteligentes têm tempo máximo. O único caso sem fim
  é "repetir até parar", que o limite de duração (abaixo) resolve.

## Cenário que define o sucesso

A macro `Relatórios de sexta` tem 7 passos no sistema de relatórios (Chrome). O usuário seleciona a macro no Studio e
clica em **Simular**.

1. O V-Clicker captura a janela vinculada e a mostra dentro do Studio, sem trazer o Chrome para a frente.
2. No passo 4, `Clicar em Baixar PDF`, a captura mostra o contorno do botão e o marcador "4"; o painel diz
   "Clicaria no botão "Baixar PDF", atualmente na posição (640, 610)" e o selo "Alvo localizado".
3. Se o botão só aparece depois de clicar em *Gerar relatório*, o passo 4 mostra "Alvo não disponível no estado atual;
   pode depender de uma ação anterior."
4. O usuário clica em *Gerar relatório* no próprio Chrome e depois em **Atualizar captura**: o passo 4 passa a mostrar o
   botão.

Nenhuma entrada é enviada ao Chrome em momento algum.

Na primeira vez que ele clica em **Executar** numa macro com Shift+Delete no passo 8, aparece a revisão de segurança
(diálogo abaixo). Ele clica em "Executar mesmo assim"; nas execuções seguintes, sem mudanças na macro, não há pergunta.

## Objetivos, não objetivos e anti-objetivos

**Objetivos**
1. **Simulação passo a passo** dentro do Studio: captura da janela, marcadores sobre a captura, painel explicativo,
   navegação por Anterior e Próximo, botão **Atualizar captura**.
2. **Revisão de segurança** dentro da simulação (resumo do estado da macro).
3. **Confirmação antes da primeira execução** de uma macro com ações potencialmente impactantes, uma vez por versão.
4. **Limite opcional de duração** por macro.

**Não objetivos (V1)**
- Camada de marcadores sobre a janela real do aplicativo (vai para o ROADMAP como evolução).
- Simular o efeito dos passos: a simulação não imagina como a tela ficaria depois de um clique.
- Limite de duração no autoclicker principal (já tem o limite de cliques).
- Retomar a execução depois de desbloquear o Windows.

**Anti-objetivos (falha mesmo que "funcione")**
- Qualquer entrada real durante a simulação: clique, tecla, texto, atalho, foco forçado, ou interação com controles que
  mude o estado do aplicativo. Isso inclui testar condições.
- Mostrar uma captura incompleta ou falha como se fosse confiável.
- Afirmar que um passo depende de outro sem ter verificado.
- Deixar a revisão irritante: perguntar de novo a cada execução, ou bloquear atalhos legítimos.
- Apresentar a confirmação como garantia de segurança: ela registra ciência dos riscos identificados, nada mais.
- Um segundo cálculo de posições ou de alvos, separado do player.

## Simulação

### Fluxo

1. O usuário seleciona uma macro e clica em **Simular** (novo botão no Studio, ao lado de Executar).
2. O V-Clicker escolhe a janela como a execução escolhe (mesma lista e mesmo seletor quando há várias janelas do
   aplicativo; mesmas recusas para janela elevada).
3. Captura a janela e avalia todos os passos uma vez, sobre o estado atual.
4. O usuário navega pelos passos com **Anterior** e **Próximo**.
5. **Atualizar captura** recaptura a janela e reavalia todos os passos.

A captura é estática: representa um instante, não uma visualização em tempo real. Não há atualização automática.

Se o aplicativo não estiver aberto, a simulação abre mesmo assim, sem captura: a lista de passos, as descrições e os
avisos de ações impactantes continuam disponíveis; posições e alvos mostram "aplicativo não está aberto". *(Decisão
do Claude.)*

### Onde a simulação aparece

Um diálogo **SafeClick · Simulação** aberto pelo Studio, no estilo dos diálogos de condição e de alvo já existentes:
captura com marcadores no alto; abaixo, "Passo N de M", o selo de estado, a explicação e a navegação; ao lado ou acima, o
resumo da revisão de segurança. Cabe numa tela de notebook (rola em telas baixas), PT e EN, acessível por teclado e
leitor de tela como o resto do app. *(Diálogo em vez de reorganizar a janela do Studio: decisão do Claude.)*

### O que cada passo mostra

| Passo             | Sobre a captura                               | No painel                                               |
| ----------------- | --------------------------------------------- | ------------------------------------------------------- |
| Clique            | Marcador numerado no ponto                    | Botão do mouse e posição                                 |
| Clique duplo      | Marcador com "×2"                             | Idem                                                     |
| Arrastar          | Linha do início ao destino, com seta          | Início e destino                                         |
| Passar o mouse    | Marcador numerado                             | Posição                                                  |
| Rolar             | Marcador com indicação de direção             | Quantidade                                               |
| Digitar           | —                                             | O texto que seria enviado, sem digitá-lo                 |
| Senha             | —                                             | "A macro para aqui" (comportamento atual do player)      |
| Tecla / Atalho    | —                                             | `Ctrl+C`, `Alt+F4` etc., com o número de vezes           |
| Esperar           | —                                             | Duração prevista                                         |
| Esperar até       | Ponto ou contorno do elemento, quando houver  | A condição e o resultado da leitura agora                |
| Smart Target      | Contorno do elemento ou da imagem encontrada  | "Alvo localizado", "não disponível" ou "ambíguo (N)"     |

**Sobreposição:** o passo atual tem o marcador em destaque; os outros passos da mesma janela aparecem como números
pequenos e esmaecidos; números a poucos pixels uns dos outros viram um só rótulo ("2, 5"). *(Decisão do Claude.)*

**Repetições:** a simulação mostra uma passagem; o painel informa quantas repetições a execução faria.

### Estados de um passo

- **Alvo localizado / ponto dentro da janela:** verificado agora.
- **Ambíguo:** o Smart Target encontrou mais de um candidato; a execução pararia ali (regra do Smart Target).
- **Alvo não disponível no estado atual:**
  - com passos anteriores na macro: "Alvo não disponível no estado atual; pode depender de uma ação anterior."
  - sem passos anteriores: "Alvo não encontrado." *(Decisão do usuário: nunca afirmar a dependência sem verificar.)*
- **Fora da janela:** o ponto calculado cai fora da área da janela (a execução pausaria).
- **Janela do passo fechada:** passos em diálogos ou menus (`WindowRef`) cujo diálogo não está aberto agora caem em
  "não disponível no estado atual", com a mesma regra acima. Se o diálogo estiver aberto, a simulação captura e mostra
  esse diálogo para o passo. *(Decisão do Claude.)*

Uma prévia bem-sucedida não elimina nenhuma verificação do player na execução real.

### Mesmo cálculo da execução

- **Posições:** a parte de `MacroTarget.Check` que encontra a janela do passo (`FindAnchor`), converte o ponto da área
  cliente para a tela e confere se ele cai dentro da janela vira uma função compartilhada, usada pelo player e pela
  simulação. A simulação **não** aplica as verificações de foco e de cobertura (`NotFocused`, `Covered`): o Studio está
  na frente por definição.
- **Smart Target e Esperar até:** a simulação usa os mesmos localizadores (`SmartTargets`, `MacroConditions`) com **uma
  única leitura**, sem esperar o tempo máximo.
- **Pixels:** a busca por imagem e a condição de cor do player leem a tela. Na simulação, o Studio pode estar na frente
  do aplicativo, então elas leem a **captura da simulação**. A lógica de comparação é a mesma; só a origem dos pixels
  muda. Isso exige que esses localizadores aceitem a imagem como entrada.

### Captura

- **Captura da própria janela, sem trazê-la para a frente** (por exemplo `PrintWindow` com renderização completa): a
  troca de foco fecharia menus e poderia mudar o estado do aplicativo. *(Decisão do Claude; o método exato fica com
  quem construir, desde que não mude o foco nem envie entradas.)*
- **Escala e DPI:** a captura é feita em pixels físicos, como as coordenadas dos passos (`MacroTarget.PerMonitorDpi`); a
  imagem é reduzida para caber no diálogo, e os marcadores seguem a mesma escala. Os marcadores precisam cair exatamente
  sobre o ponto calculado em qualquer DPI e tamanho de janela.
- **Falhas transparentes:** se a captura falhar, se a janela estiver minimizada, ou se a imagem vier toda de uma cor só
  (jogos com aceleração gráfica costumam capturar preto), o diálogo mostra a limitação ("Não foi possível capturar esta
  janela") em vez da imagem. Passos, descrições e avisos continuam disponíveis; posições aparecem só como texto. A
  janela minimizada não é restaurada automaticamente. *(Detecção de imagem de uma cor: decisão do Claude.)*

### Confiança dos resultados (ajuste do usuário)

Alguns aplicativos, principalmente jogos e programas com aceleração gráfica, fornecem capturas desatualizadas ou
incompletas. Por isso:

- Todo resultado é apresentado como **verificação do estado capturado**, nunca como garantia sobre o estado atual. O
  diálogo mostra a hora da captura ("Captura das 14:32:05") e as frases usam "na captura": "Encontrado na captura",
  "Clicaria em (640, 610) na captura".
- **Inconclusivo** é um estado próprio, nunca uma confirmação positiva. Um passo é inconclusivo quando o resultado
  depende dos pixels e a captura não passou na validação, ou quando a leitura não terminou a tempo.
- **Validação mínima da captura** *(decisão do Claude)*: a captura sem foco funcionou, a janela não está minimizada, a
  imagem tem o tamanho da área cliente e não é toda de uma cor só. Uma captura que passa nessa validação ainda pode
  estar desatualizada sem que o V-Clicker perceba; é por isso que a linguagem é sempre relativa à captura.
- Alvos por elemento (UI Automation) são lidos da interface no momento da atualização, não da imagem; a frase diz
  "Encontrado na interface no momento da captura".

### Revisão de segurança (dentro da simulação)

Resumo no topo do diálogo, recalculado a cada atualização:
- Janela vinculada encontrada (ou não);
- Alvos identificados: quantos foram localizados agora;
- Ações potencialmente impactantes: quais passos;
- Passos não verificáveis agora: "Alguns alvos só poderão ser verificados durante a execução."

Alertas não significam que a macro está errada; explicam a situação.

## Ações potencialmente impactantes

Passos de **Tecla** ou **Atalho** com uma destas combinações (lista inicial aprovada):

| Combinação       | Explicação no aviso                                       |
| ---------------- | --------------------------------------------------------- |
| Shift+Delete     | Pode excluir itens permanentemente, dependendo do aplicativo. |
| Delete           | Pode excluir itens, dependendo do aplicativo.            |
| Alt+F4           | Pode fechar a janela ativa.                               |
| Ctrl+W           | Pode fechar a aba ou a janela ativa.                      |
| Ctrl+Shift+Esc   | Abre o Gerenciador de Tarefas.                            |

Nome: "ações potencialmente impactantes", não "destrutivas" (decisão do usuário). Shift+Delete e Delete usam o ícone de
alerta; as outras, o de atenção, como no mockup do usuário. *(Níveis: decisão do Claude.)* Avisam; nunca bloqueiam.

## Confirmação antes de executar

Ao clicar em **Executar** numa macro com ações impactantes ainda não aprovadas na versão atual, o Studio mostra:

> **Revisão de segurança**
> Esta macro contém ações que podem afetar o aplicativo ou seus dados.
> **Passo 8 — Shift+Delete** · Pode excluir itens permanentemente, dependendo do aplicativo.
> **Passo 12 — Alt+F4** · Pode fechar a janela ativa.
> [Revisar na simulação] [Cancelar] [Executar mesmo assim]

Regras (do usuário):
1. Primeira execução: exige confirmação se houver ações impactantes. Sem ações impactantes, nada aparece.
2. **Executar mesmo assim:** aprova a versão atual e segue o fluxo normal de execução (as recusas e a pergunta de
   tamanho de janela continuam valendo).
3. Executar de novo sem mudanças: não pergunta.
4. Mudar qualquer passo, parâmetro de execução (repetições, limite de duração) ou aplicativo-alvo invalida a aprovação.
5. **Cancelar:** encerra a tentativa sem enviar nenhuma entrada.
6. **Revisar na simulação:** abre a simulação; não aprova nem executa.
7. A simulação nunca é obrigatória.

**A aprovação vale para uma instância local da macro e para o seu conteúdo** (ajuste do usuário: duas macros iguais têm
o mesmo conteúdo, então a assinatura sozinha não basta).

- **Conteúdo:** hash SHA-256 da macro como `MacroFile.Write` a grava, **sem a linha do nome**. Alterações externas no
  arquivo também invalidam a aprovação.
- **Instância local:** a data de criação do arquivo `.vcmacro` (UTC, com a precisão do sistema de arquivos). Salvar pelo
  Studio (`File.Replace`) e renomear (`File.Move`) preservam essa data, verificado no Windows; um arquivo copiado,
  baixado ou extraído recebe uma data nova. *(Mecanismo: decisão do Claude.)*
- **Resultado:** renomear pelo Studio preserva a aprovação; mudar qualquer parâmetro relevante a invalida; uma macro
  importada, ou uma cópia dentro da própria pasta, exige nova aprovação mesmo com conteúdo idêntico.
- **Onde fica:** nas configurações locais do V-Clicker (pares conteúdo + instância aprovados, com limite de tamanho),
  nunca no arquivo compartilhável.
- **Limite conhecido:** uma ferramenta que copie arquivos preservando a data de criação exata (até a fração de segundo)
  por cima de uma macro aprovada, com o mesmo conteúdo, herdaria a aprovação. Como o conteúdo é idêntico ao aprovado, o
  risco aceito é nulo na prática.

A pergunta da confirmação deixa claro que é ciência dos riscos identificados, não garantia de que a macro é segura.

## Limite de duração

- Opcional, por macro, desligado por padrão; quando ligado, o valor é explícito, em minutos (1 a 5999; a mensagem de
  parada mostra "1 h 30 min"), e aparece ao lado de Repetições no Studio.
- Gravado no cabeçalho do arquivo da macro (nova chave; arquivos antigos carregam sem limite; arquivos com a chave
  continuam abrindo em versões que a ignorem, porque o cabeçalho já preserva linhas desconhecidas).
- Conta o tempo desde o fim da contagem regressiva, **incluindo pausas** (o objetivo é evitar execuções intermináveis).
  *(Decisão do Claude.)*
- Ao atingir o limite, a macro para entre ações (nunca no meio de um arraste) e explica: "Limite de duração atingido
  (1 h 30 min)." Teclas e botões presos são soltos como em qualquer parada.

## Testes

- Unitários: detecção de ações impactantes; assinatura (mudança de passo, repetição, limite ou executável invalida;
  renomear não invalida; edição externa do arquivo invalida); cópia com conteúdo idêntico não herda a aprovação;
  leitura e gravação do limite; o player para no limite.
- Captura que falha na validação deixa os passos que dependem de pixels como **inconclusivos**, nunca positivos.
- Simulação: posição dos marcadores na captura em escala diferente de 1; estados "localizado", "ambíguo", "não
  disponível" com e sem passos anteriores; captura de uma cor só tratada como falha; **nenhuma entrada enviada** durante
  a simulação (o `IInputSink` de teste não recebe nada).
- Integração pelo Studio (`studio.BeginRun()`, regra do projeto): macro com Alt+F4 pede confirmação na primeira execução,
  não pede na segunda, volta a pedir depois de editar um passo; Cancelar não envia nada.

## Documentação

- ROADMAP: SafeClick V1 implementado; camada de marcadores sobre a janela real como evolução possível.
- PRODUCT.md: simulação, revisão e limite de duração na lista de capacidades.
- README (PT e EN): seção curta sobre Simular.

## Detalhes resolvidos na revisão do design *(decisões do Claude)*

- **Foco e cobertura nos localizadores:** além da posição, `SmartTargets` e `MacroConditions` também deixam de exigir o
  aplicativo na frente e o ponto descoberto quando chamados pela simulação.
- **Alvo encontrado mas inutilizável** (por exemplo, desativado): "não disponível no estado atual". Leitura que esgotou o
  tempo de 2 s: "não verificável agora", nunca "localizado".
- **Passos em menus:** um menu só existe enquanto o aplicativo está na frente, então na simulação esses passos sempre
  aparecem como "não disponível no estado atual". É o comportamento esperado, não uma falha.
- **Ordem ao Executar:** as recusas rápidas (sem passos, passo desconhecido, passo que aperta um atalho do V-Clicker)
  vêm antes da confirmação; as que dependem do aplicativo (não aberto, elevado, tamanho da janela) vêm depois dela.
- **Combinações impactantes:** só as combinações exatas da lista; `Ctrl+Delete`, por exemplo, não gera aviso.
- **Onde o limite de duração para:** em qualquer fronteira entre entradas, como o F8 (inclusive no meio de um texto),
  nunca no meio de um arraste.
- **Estabilidade da assinatura:** a chave do limite só é gravada quando há limite, para não mudar a assinatura de
  macros existentes.

## Fica com quem construir

- O método de captura que respeite "sem foco, sem entrada".
- O desenho exato dos marcadores, do contorno e da linha de arraste, seguindo o DESIGN.md.
- Como o diálogo se organiza, dentro das regras de tamanho, DPI e acessibilidade do projeto.
- Como extrair a parte compartilhada de `MacroTarget.Check` e dos localizadores sem mudar o comportamento do player.
