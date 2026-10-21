# V-Clicker: Smart Target V1 — design

Data: 2026-10-23 · Estado: aprovado, com três ajustes já incorporados (processos do navegador, buscas sem sobreposição, reconhecimento visual sem promessa)

## Intenção

Terceira e última funcionalidade da fase de produtividade (Macro Studio → Conditional Click → **Smart Target**; veja
[ROADMAP](../../ROADMAP.md), o [design do Macro Studio](2026-10-21-macro-studio-design.md) e o
[do Conditional Click](2026-10-22-conditional-click-design.md)).

Hoje todo passo de mouse clica num ponto fixo da janela. Quando o sistema mostra uma barra de aviso ou reorganiza a
página, o botão muda de lugar e a macro clica no que estiver no ponto antigo. O Smart Target guarda **o que** clicar
(o botão "Baixar PDF", ou uma pequena imagem) em vez de **onde**, procura o alvo antes de cada clique e clica onde ele
estiver agora.

Divisão de papéis:
- o **Conditional Click** responde *quando* a macro pode continuar;
- o **Smart Target** responde *onde* ela deve clicar.

Os dois usam o mesmo localizador de alvos (seção "Smart Target e Conditional Click").

Princípio herdado, e o mais importante aqui: **se o V-Clicker não tiver certeza de qual é o alvo, ele não clica.**
Nunca escolhe entre dois candidatos e nunca volta para as coordenadas antigas sem avisar.

## Cenário que define o sucesso

O funcionário grava a macro dos 60 relatórios no Chrome:
1. abrir o registro;
2. clicar em *Gerar relatório* (812, 440);
3. esperar *Baixar PDF*;
4. clicar em *Baixar PDF* (640, 520).

No Studio, ele seleciona o passo `Clicar (esquerdo) em 640, 520` e clica em **Transformar em alvo inteligente**. O
Chrome vem para a frente; ele aponta para o botão e aperta F7. O Studio mostra:
- `Botão "Baixar PDF"`, com a imagem do botão recortada da tela;
- "Único nesta janela ✓";
- o tempo máximo para encontrar, 10 s.

Ele confirma. O passo vira `Clicar no botão "Baixar PDF"`.

Um dia o sistema mostra uma barra de aviso e o botão desce para (640, 610). A macro:
1. procura o botão;
2. confirma que ele está parado no mesmo lugar;
3. confirma que o ponto do clique cai nele mesmo;
4. clica em (640, 610).

O passo 3, a espera do Conditional Click, também encontra o botão no lugar novo.

Se a página passar a ter dois botões "Baixar PDF", a macro não clica em nenhum. Ela para com:
"Encontrei 2 botões "Baixar PDF" e não sei qual é o certo. A macro parou antes do passo 4."

## Objetivos, não objetivos e anti-objetivos

**Objetivos**
- Um passo de clique (esquerdo, direito ou do meio, simples ou duplo) pode ter um **alvo**, de um destes tipos:
  - **Elemento:** um controle da interface, identificado por tipo e nome, via UI Automation. É o caminho principal.
  - **Imagem:** uma pequena região da tela capturada no F7. É a alternativa para programas sem UI Automation, como
    jogos e interfaces desenhadas à mão.
- Antes de clicar, a macro procura o alvo na janela do passo. Se não encontrar, espera até um **tempo máximo
  configurável** (padrão 10 s). Esgotado o tempo, para com uma explicação.
- **Ambiguidade = não clicar:** dois ou mais candidatos que o V-Clicker não consegue distinguir nunca levam a um
  clique.
- Criado pelo editor: converter um clique gravado (caminho principal) e **Apontar de novo**. Reversível: o ponto
  gravado continua guardado no passo, e **Voltar para o ponto fixo** devolve exatamente o passo original.
- A condição **Elemento** do Conditional Click passa a usar o mesmo localizador: espera o elemento onde ele estiver na
  janela, não só no ponto apontado.
- F8 para na hora, também no meio de uma busca.

**Não objetivos (V1)**
- Alvo em passos de arrastar, passar o mouse e rolar (só cliques na V1).
- Rolar a página ou a lista para trazer o alvo à vista: um alvo fora da área visível conta como "não encontrado".
- Achar uma imagem em outra escala ou rotação. Mudar o zoom do navegador ou a escala do Windows faz a imagem não ser
  encontrada; a macro para e explica.
- Distinguir dois elementos iguais pela posição ("o segundo Baixar PDF") ou pelo contexto ("o Baixar PDF da linha de
  março").
- Capturar o alvo automaticamente durante a gravação.
- Ler texto da tela (OCR), IA generativa, processamento em nuvem e bibliotecas de terceiros.

**Anti-objetivos**
- Clicar quando houver mais de um candidato, ou quando o alvo estiver desativado, coberto ou ainda se movendo.
- Usar o ponto gravado como plano B quando o alvo não for encontrado.
- Clicar em algo de outro programa porque "parecia" o alvo.

## Restrições

As mesmas das entregas anteriores:
- .NET Framework 4.8, um único `.exe`, sem dependências de terceiros, PT/EN, testes no executor atual.
- UI Automation, captura de tela e PNG vêm do próprio Windows e do .NET Framework.

Um arquivo com alvo aberto numa versão sem o Smart Target vira passo desconhecido: fica preservado e a execução é
bloqueada (regra existente). O Conditional Click ainda não foi lançado (PR aberto), então mudar o significado de
`waitfor element` não quebra arquivos de ninguém.

## Smart Target e Conditional Click

Há um único **localizador**. Dado um alvo e a janela do passo, ele responde uma destas leituras:

| Leitura | Quando |
|---|---|
| **Encontrado** | um candidato, disponível; vem com o retângulo dele na tela |
| **Não encontrado** | nenhum candidato |
| **Ambíguo** | mais de um candidato |
| **Indisponível** | o candidato está desativado ou fora da área visível |
| **Inconclusivo** | a busca falhou ou passou do tempo |

Mais as leituras de proteção que já existem:
- **Ausente:** outro aplicativo na frente;
- **Coberto:** outro programa por cima do alvo;
- **Fechado:** a janela vinculada foi fechada.

- O **passo de clique com alvo** observa o localizador como o `Esperar até…` observa a condição: mesmo laço, mesmas
  regras de pausa e de relógio. Quando o alvo é encontrado (e confirmado, veja abaixo), clica nele.
- A **condição Elemento** passa a ser "o localizador encontra exatamente um elemento disponível com esse tipo e nome
  na janela". O ponto do `waitfor element` fica só como referência de onde foi apontado.
- Um clique com alvo já espera o alvo aparecer, até o seu tempo máximo. Por isso o `Esperar até` antes dele fica para
  esperas longas, como um relatório que demora minutos. O V-Clicker não muda nada disso sozinho.

## Experiência

### Criar um alvo

No painel de um passo `Clicar` ou `Clique duplo` há o botão **Transformar em alvo inteligente**, com a dica "Clica no
botão onde ele estiver, mesmo que mude de lugar". O fluxo é o mesmo da condição:

1. O aplicativo vem para a frente; a linha de estado e o indicador dizem `Aponte para o que a macro deve clicar e aperte F7`.
2. No F7, o V-Clicker lê o ponto:
   - o elemento de interface, se houver (como na condição: o controle interativo, subindo até 3 níveis a partir do
     texto);
   - o recorte de imagem em volta do ponto.

   O ponto precisa ser da **mesma janela do passo**: a janela vinculada, ou o mesmo diálogo em que o clique foi
   gravado. Senão aparece um aviso ("Aponte dentro da mesma janela do passo") e nada muda. Assim:
   - a janela onde procurar é a do passo;
   - o ponto gravado nunca é alterado;
   - "Voltar para o ponto fixo" é exato.
   A leitura do F7 roda fora da interface. O indicador mostra "Analisando o ponto…", o Studio continua respondendo,
   e o F8 cancela. A interface nunca congela, nem com uma página lenta. O aplicativo fica na frente até a leitura
   terminar, porque ler o elemento sob o ponto exige que ele esteja visível.
3. O Studio volta e mostra o diálogo **Alvo inteligente**. O usuário não precisa saber o que é UI Automation:
   - O diálogo abre com a escolha do V-Clicker em palavras simples. Por exemplo: "Reconhecido como o botão
     "Baixar PDF"", ou "Reconhecido pela aparência (este programa não informa os nomes dos botões)".
   - Usuários avançados podem trocar num controle secundário, **Reconhecer pelo nome** / **Reconhecer pela
     aparência**. O padrão é **pelo nome** (Elemento) quando há um elemento com nome único na janela; senão, **pela
     aparência** (Imagem).
   - A **prévia**:
     - Elemento: `Botão "Baixar PDF"` e a imagem do retângulo do elemento recortada da tela, para o usuário ver que é
       o botão inteiro e não um texto dentro dele.
     - Imagem: o recorte, ampliado.
   - O **resultado da busca naquela tela**:
     - "Único nesta janela ✓";
     - ou "Há 2 botões "Baixar PDF" nesta janela; o V-Clicker não teria como saber qual clicar". Nesse caso
       **Elemento** fica indisponível;
     - para Imagem, "Aparece 3 vezes; use uma região maior" ou "Esta região tem pouco detalhe para ser reconhecida".
   - Imagem: o **tamanho da região**, Pequena / Média / Grande (32, 64 ou 96 pixels de lado). O V-Clicker já escolhe
     o menor tamanho que seja único e tenha detalhe suficiente. Trocar o tamanho refaz a prévia e o resultado.
   - **Tempo máximo para encontrar**, em segundos (padrão 10, de 1 a 3600).
   - **Confirmar** / **Cancelar**. Cancelar não muda nada. Confirmar só fica ativo com um alvo único.
   - Quando nenhum tipo serve, o diálogo diz por quê e sugere **Apontar de novo** ou manter o ponto fixo.
4. O passo fica, por exemplo:
   - `Clicar no botão "Baixar PDF"`
   - `Clique duplo (direito) no item de lista "Março"`
   - `Clicar na imagem capturada em 640, 520`

   Na lista, os passos com alvo mostram um marcador discreto.
5. No painel do passo:
   - o tipo do alvo e a prévia: para Imagem, o recorte; para Elemento, o tipo e o nome;
   - o tempo máximo editável;
   - **Apontar de novo**;
   - **Voltar para o ponto fixo (640, 520)**.

   Tudo entra no Desfazer/Refazer.

### Executar um clique com alvo

1. **Procurar.** O localizador procura o alvo na janela do passo, a vinculada ou o diálogo gravado.
   - Os **100 ms** são o intervalo desejado entre o fim de uma busca e o começo da próxima. Uma busca nunca começa
     enquanto outra ainda roda.
   - Buscas rodam fora da interface e da thread do player.
   - Uma busca que passa do limite é **abandonada**: o player segue (inconclusivo), e a resposta dela, quando vier,
     é descartada.
   - Enquanto uma busca abandonada não terminar, nenhuma nova começa. Existe no máximo uma busca em andamento no
     programa todo, e buscas abandonadas não se acumulam.
2. **Confirmar que parou.** O clique só acontece depois de **duas buscas seguidas** encontrarem o alvo **no mesmo
   lugar** (até 2 px de diferença). Isso cobre uma página que ainda está se reorganizando, como a barra de aviso
   empurrando o conteúdo.
3. **Ponto do clique:**
   - Elemento: o centro do retângulo dele.
   - Imagem: o centro da região encontrada, ou seja, o mesmo ponto relativo que o usuário apontou.
4. **Conferência final, logo antes de clicar:**
   - As proteções de sempre no ponto do clique: o aplicativo na frente, o ponto dentro da janela do passo, a janela
     sob o ponto ser a do passo.
   - Elemento: o elemento sob o ponto, com a mesma regra de subir até 3 níveis, precisa ser o próprio alvo ou estar
     dentro dele. Isso pega um aviso ou uma camada da página por cima do botão, que a verificação de janela não vê.
   - Imagem: a imagem é comparada de novo naquele lugar exato.

   Se a conferência falhar, conta como "ainda não", e as duas buscas recomeçam.
5. **Clicar** e seguir com o espaçamento mínimo de 50 ms. O tempo gasto procurando empurra os passos seguintes, como
   no `Esperar até`.

**Feedback:** o indicador e a linha de estado mostram `Procurando "Baixar PDF"… 2 s` ou `Procurando a imagem…`.

**Pausa e relógio**, como no Conditional Click:
- Um problema **de fora** pausa a macro e para o relógio do tempo máximo: outra janela na frente, outro programa
  cobrindo o alvo.
- Um problema **do próprio aplicativo** conta como "ainda não", e o relógio corre: o diálogo do passo ainda não abriu,
  o alvo não apareceu, está desativado ou é ambíguo. Uma busca lenta ou que falhou também conta assim.

**Tempo esgotado:** a macro para antes do clique. O passo fica selecionado no Studio, e a mensagem diz o último motivo
visto:
- `Não encontrei o botão "Baixar PDF" em 10 s. A macro parou antes do passo 4.`
- `Encontrei 2 botões "Baixar PDF" e não sei qual é o certo. …`
- `O botão "Baixar PDF" ficou desativado …` (ou "fora da área visível …")
- Imagem não encontrada: `Não encontrei a imagem em 10 s. Se o zoom ou a escala da tela mudou, aponte de novo. …`

Em nenhum caso o ponto gravado é usado.

**F8** para na hora, sem esperar a busca em andamento terminar. Essa busca é abandonada. Uma resposta que chegue depois da parada, da
pausa ou do limite é descartada e nunca leva a um clique.

### Os dois tipos em detalhe

**Elemento (UI Automation)**
- Guarda o tipo de controle e o nome, como a condição.
- Se tipo e nome não forem únicos na janela no F7, mas o identificador de automação do elemento for (no Chrome, o
  `id` do HTML), guarda também esse identificador. Ele passa a ser exigido na busca. Se mesmo assim não for único,
  Elemento não é oferecido.
- A condição Elemento do Conditional Click usa esta mesma busca, com o mesmo limite de 2 s por busca (antes, 500 ms
  por leitura num ponto).
- **Busca:** os descendentes da janela do passo na árvore de acessibilidade, a partir do elemento da própria janela,
  com o mesmo tipo, o mesmo nome e o mesmo identificador quando guardado.
  - Não se exige que cada elemento seja do mesmo processo da janela: Chrome e Edge usam vários processos.
  - A proteção contra outros aplicativos vem do escopo (só a árvore da janela do passo) e das conferências na tela
    (aplicativo na frente, janela sob o ponto, elemento sob o ponto = o alvo).
  - No F7, o elemento apontado só é aceito se essa mesma busca, feita a partir da janela, o encontrar.
- **Candidato** é todo elemento que bate, esteja visível ou não. Um único candidato habilitado e na tela é
  **Encontrado**. Um único desativado ou fora da tela é **Indisponível**. Dois ou mais são **Ambíguos**.
- Elementos sem nome, como ícones sem rótulo de acessibilidade, ficam para o tipo Imagem.
- **Limite de cada busca:** 2 s; passou disso, a leitura é inconclusiva. Páginas grandes podem ser lentas; veja Riscos.

**Imagem**
- No F7, o recorte é tirado da tela centrado no ponto. O cursor não aparece na captura.
- **Comparação por forma, bloco a bloco:**
  - Uma passada grosseira, em 1/4 da resolução, propõe lugares parecidos.
  - Cada lugar é julgado na resolução real em **blocos de 8×8 pixels que se sobrepõem pela metade**, e **todos os
    blocos** precisam bater, por correlação normalizada de tons de cinza.
  - No F7, um tamanho só serve se, além de único, ficar **claramente diferente** de tudo o mais na tela (o próximo
    lugar mais parecido abaixo de 0,40). Um recorte que mostra só "xar P", ao lado de um "xar C", poderia mais tarde
    bater com um "xar D". Os testes cobrem telas em escala de 100%, 125% e 150%.
  - Um bloco basta para reprovar: um botão parecido com outra palavra ("Baixar CSV" no lugar de "Baixar PDF") é
    reprovado.
  - Dentro de cada bloco, uma mudança uniforme de brilho e contraste não importa. Pretende tolerar mudanças uniformes
  de brilho e contraste, como o destaque quando o mouse passa por cima, e pequenas diferenças de suavização. Uma
  conferência de cor média evita que uma forma igual em cor muito diferente conte como o mesmo alvo. O construtor
  calibra os limites com testes.
- **Medição (teste do risco 2)**, em telas renderizadas pelo Chrome e em botões nativos do Windows:
  - acha o botão depois de mudar de lugar e com destaque de mouse;
  - nunca confunde o alvo com "Baixar CSV/XLS";
  - com dois "Baixar PDF", o resultado é ambíguo;
  - com "Baixar PDF" trocado por "Baixar DOC", nada é encontrado, em todos os tamanhos;
  - menos de 40 ms numa tela de 1920×1080.
- **Regiões maiores incluem o que está em volta do botão.** Se o entorno mudar (por exemplo, uma barra de aviso
  aparecendo logo acima), a imagem grande deixa de ser encontrada; a pequena continua. Por isso o padrão é o menor
  tamanho único, e o diálogo avisa isso ao trocar para um tamanho maior.
- O indicador do V-Clicker fica fora das capturas de tela (recurso do Windows 10 2004+), para não esconder o alvo.
- **Não é infalível, e o V-Clicker não promete que seja.** A tolerância a destaque, brilho e contraste é validada com
  testes reais. Se a aparência mudar além do que dá para reconhecer com segurança, a leitura é "não encontrado": o
  Smart Target continua procurando e, no tempo máximo, para com a explicação. Nunca afrouxa a comparação para achar
  alguma coisa.
- A busca cobre a área útil da janela do passo, num print da tela.
- Se a imagem não for encontrada e uma janela de **outro programa** estiver por cima de parte dessa área, a leitura
  é **Coberto**, não "não encontrado": a macro pausa e o relógio para. Um alvo escondido por uma notificação não
  esgota o tempo máximo. Se a imagem for encontrada, valem as proteções de cobertura no ponto encontrado.
- Duas regiões que batem e se sobrepõem menos da metade são **dois candidatos**, logo **Ambíguo**.
- **Pouco detalhe** (uma região quase lisa) é recusada no F7.
- **Desempenho:** cada busca deve levar no máximo ~300 ms numa janela de 1920×1080 num PC comum, por exemplo com uma
  busca grosseira em resolução reduzida e confirmação na resolução real. A forma de chegar lá fica com o construtor.
- Sem escala ou rotação na V1.

## Arquivo

O alvo é um sufixo opcional da linha do clique. O ponto e a janela-âncora continuam como estão: são o ponto gravado e
a janela onde procurar.

```
click left 640 520 find element "Button" "Baixar PDF" timeout=10000
click left 640 520 find element "Button" "Baixar PDF" id "btnPdf" timeout=10000 in "#32770" "Salvar como"
double right 300 210 find image iVBORw0KGgoAAAANSUhEUgAA... timeout=10000
waitfor element 640 520 "Button" "Baixar PDF" id "btnPdf" timeout=30000 was=8000
```

- `find image <base64 de um PNG quadrado de 32, 64 ou 96 pixels>`: a imagem fica dentro do próprio arquivo (até ~40 KB numa linha), para a
  macro continuar sendo um arquivo só.
- Sem `find`, o clique é de ponto fixo, exatamente como hoje.
- O `id` na condição Elemento segue a mesma regra do alvo.

## Riscos a verificar cedo (antes do resto da implementação)

1. **Velocidade da busca por UI Automation no Chrome** numa página real e grande. Medir antes de construir o resto.
   **Medido:** 350–600 ms por busca em páginas com cerca de 10 mil elementos (uma página local com 4.500 botões e a
   Wikipedia), no Chrome e no Edge. O limite de 2 s basta. Os elementos da página vêm com o processo da própria
   janela do navegador, e a conferência do elemento sob o ponto funciona.
   - A unicidade é sempre verificada na **janela inteira**. Nunca se procura só numa região, porque isso não prova
     que não existe um segundo candidato.
   - Se 2 s não bastarem, o limite de cada busca pode subir até o que a medição mostrar razoável (no máximo 5 s).
   - Se nem assim der, Elemento fica indisponível para aquela página: o diálogo explica e sugere Imagem.
2. **Primeira chamada no Chrome:** o Chrome só monta a árvore de acessibilidade quando um cliente pede.
   **Medido:** durante cerca de 1 s depois do primeiro pedido, as buscas não acham nada; depois disso, acham.
   - Na execução isso conta como "ainda não", dentro do tempo máximo.
   - No F7, a busca de unicidade é repetida algumas vezes antes de concluir.
3. **Destaque de mouse sobre o recorte de imagem.** **Medido** com renderizações reais, veja "Imagem". Ainda falta
   conferir à mão num sistema real.

## O que fica para o construtor

- A estrutura do código, desde que:
  - o localizador seja injetável no player, como a leitura de condição, e testável sem tela;
  - a comparação de imagens seja uma função pura sobre bitmaps, testável com imagens sintéticas.
- Como reaproveitar o laço do `Esperar até` para o clique com alvo, sem duplicá-lo.
- O layout do diálogo e do marcador na lista, seguindo o tema atual.
- Os limites numéricos da comparação de imagem, calibrados com testes.

Decisões minhas, para conferir:
- Tempo máximo padrão de **10 s** para encontrar, contra 30 s da condição: um clique normalmente vem depois do
  conteúdo carregado, e falhar rápido é melhor.
- Duas buscas seguidas no mesmo lugar antes de clicar. Custa cerca de 100 ms por clique com alvo.
- O identificador de automação só é usado quando é necessário para desempatar, porque em muitos sistemas web ele muda
  a cada carregamento.
- O tamanho da imagem é escolhido automaticamente: o menor que seja único.
- O F7 precisa cair na mesma janela do passo. O alvo não guarda janela nem ponto próprios, e o formato não muda
  além do sufixo `find`.
- Imagem não encontrada com outro programa por cima de parte da janela = pausa, não "não encontrado".
- A unicidade é sempre na janela inteira, também se a busca for lenta: um limite maior ou "Elemento indisponível",
  nunca uma busca parcial.
- Branch `feature/smart-target` sobre `feature/conditional-click`, num PR empilhado (#4). Mudar o significado de
  `waitfor element` só é seguro se o Conditional Click e o Smart Target saírem **na mesma versão**. Se o #3 for
  publicado sozinho antes, este design precisa manter o significado antigo para os arquivos existentes.
- O diálogo da condição Elemento ganha a mesma verificação de unicidade e o mesmo `id` do alvo.
- A condição **Elemento** passa a procurar na janela inteira, para que a espera do cenário funcione quando o botão
  muda de lugar. Antes ela procurava só no ponto.

## Critérios de aceitação

- O botão "Baixar PDF" passa de (640, 520) para (640, 610): o clique com alvo acerta o botão no lugar novo, e a
  espera com condição Elemento também o encontra.
- Dois botões com o mesmo tipo e nome (e sem identificador que os distinga): nada é clicado, a macro para e a
  mensagem diz quantos encontrou e o número do passo.
- Alvo que nunca aparece: a macro para no tempo máximo, nada é clicado e o ponto gravado nunca é usado.
- Alvo desativado: a macro espera; se não ativar no tempo máximo, para com a mensagem de desativado.
- Uma camada da própria página por cima do botão faz a conferência final falhar, e não há clique.
- Alvo que ainda se move entre duas buscas: o clique só acontece depois que ele para.
- Imagem com o brilho mudado (destaque de mouse) é encontrada; uma região lisa é recusada no F7; uma imagem que
  aparece duas vezes é Ambígua.
- Outra janela na frente durante a busca: a macro pausa e o tempo máximo não corre.
- F8 durante uma busca para em menos de 100 ms.
- Converter em alvo e **Voltar para o ponto fixo** devolve exatamente o passo original, também depois de salvar e
  reabrir. Arquivo com alvo salvo e reaberto fica idêntico.
