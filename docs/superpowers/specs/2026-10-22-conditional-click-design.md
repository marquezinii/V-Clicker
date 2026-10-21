# V-Clicker: Conditional Click V1 — design

Data: 2026-10-22 · Estado: aprovado

## Intenção

Segunda funcionalidade da fase de produtividade (Macro Studio → **Conditional Click** → Smart Target; veja
[ROADMAP](../../ROADMAP.md) e o [design do Macro Studio](2026-10-21-macro-studio-design.md)).

Hoje a macro dos 60 relatórios espera um tempo fixo depois de *Gerar relatório*. Se o sistema demora 2 s, perde 6 s;
se demora 10 s, clica em *Baixar PDF* antes de ele existir. O Conditional Click troca o tempo fixo por uma condição:
**esperar até o botão estar disponível**, seguir assim que estiver, e parar com uma explicação se não aparecer.

Princípio herdado do Macro Studio, e o mais importante aqui: **se o V-Clicker não conseguir confirmar que é seguro
prosseguir, ele para e explica; nunca continua clicando às cegas.**

## Cenário que define o sucesso

O funcionário grava a macro como sempre. No Studio, seleciona `Esperar 8 s` (depois de *Gerar relatório*) e clica em
**Transformar em condição**. O sistema vem para a frente; ele aponta para o botão *Baixar PDF* e aperta F7. O Studio
mostra o que encontrou ("Botão \"Baixar PDF\"") e um tempo máximo de 30 s. Ele confirma. O passo vira
`Esperar até "Baixar PDF" estar disponível (até 30 s)`.

Na execução, o indicador mostra `Aguardando "Baixar PDF"… 3 s`. Quando o botão aparece, a macro segue na hora para o
passo seguinte, o clique gravado em *Baixar PDF*. (O passo de condição só espera; quem clica é o passo gravado.) No
dia em que o sistema trava, a macro para depois de 30 s com "\"Baixar PDF\" não ficou disponível em 30 s. A macro parou
antes do passo 6." e nada é clicado.

## Objetivos, não objetivos e anti-objetivos

**Objetivos**
- Um passo novo, `Esperar até…`, com três tipos de condição observados num ponto fixo da janela:
  - **Elemento:** um elemento da interface (botão, link, campo) com aquele nome está disponível, via UI Automation;
  - **Cor:** uma pequena região em volta do ponto ficou com a cor capturada;
  - **Janela:** um diálogo da janela vinculada (por exemplo "Salvar como") abriu.
- Tempo máximo obrigatório (padrão 30 s); estourou, a macro para antes do passo seguinte, com mensagem clara.
- Condição satisfeita: segue na hora, sem esperar o resto da espera original.
- Criada pelo editor: converter uma espera (caminho principal) ou inserir do zero. Reversível para a espera original.
- F8 para na hora, inclusive enquanto observa.

**Não objetivos (V1)**
- Encontrar o elemento em outro lugar se a interface mudar de posição (é o Smart Target).
- Reconhecimento por imagem (Smart Target).
- Condições compostas (E/OU), "se… senão", repetir até, desvios: a macro continua linear.
- Configurar condições durante a gravação.
- Texto lido da tela (OCR).

**Anti-objetivos**
- Seguir para o passo seguinte sem a condição confirmada (por tempo esgotado, erro ou leitura duvidosa).
- Ler a cor de outro aplicativo que esteja por cima do ponto e tomar isso como "condição satisfeita".
- A condição mudar o que o usuário gravou sem ele pedir.

## Restrições

As mesmas do Macro Studio (net48, sem dependências de terceiros, PT/EN, testes no executor atual). UI Automation e
leitura de pixels são recursos do próprio Windows/.NET Framework. Um arquivo com `Esperar até…` aberto numa versão sem
o Conditional Click já é tratado como passo desconhecido: preservado e com a execução bloqueada (regra existente).

## Experiência

### Criar uma condição

- **Converter:** no painel de um passo `Esperar`, o botão **Transformar em condição**.
- **Inserir:** na barra de edição, **+ Condição** (insere depois do passo selecionado).
- Os dois abrem o mesmo fluxo:
  1. O aplicativo da macro vem para a frente; a linha de estado (e o indicador) dizem
     `Aponte para o que a macro deve esperar e aperte F7`.
  2. No F7, o V-Clicker lê, naquele ponto: o elemento de interface (nome e tipo), a cor da região e a janela sob o
     ponto. O ponto precisa ser da janela vinculada ou de um diálogo dela (senão: aviso, nada muda).
  3. O Studio volta e mostra um pequeno diálogo:
     - o tipo de condição (Elemento / Cor / Janela), já escolhido pelo V-Clicker: **Elemento** se o ponto tem um
       elemento com nome; senão **Janela** se o ponto está num diálogo; senão **Cor**. O usuário pode trocar;
     - o que foi encontrado, em palavras: `Botão "Baixar PDF"`, `Cor #3A6BD1` (com amostra), `Janela "Salvar como"`;
       tipos sem informação aparecem desativados com o motivo ("nenhum elemento com nome neste ponto");
     - **Tempo máximo**, em segundos (padrão 30, de 1 a 3600);
     - **Confirmar** / **Cancelar**. Cancelar não muda nada.
- O passo fica:
  - `Esperar até "Baixar PDF" estar disponível (até 30 s)`
  - `Esperar até a cor #3A6BD1 em 640, 520 (até 30 s)`
  - `Esperar até a janela "Salvar como" abrir (até 30 s)`
- No painel do passo: o tempo máximo editável, **Apontar de novo**, e, quando veio de uma espera,
  **Voltar a ser espera de 8 s**. A espera original fica guardada inteira no passo (duração atual e tempo gravado),
  e voltar devolve exatamente o passo que existia, também depois de salvar e reabrir.
- Tudo entra no Desfazer/Refazer, como as outras edições.

### Executar

- O passo observa a condição a cada **100 ms**. A condição só conta como satisfeita depois de **duas leituras
  seguidas** positivas (evita um piscar da interface).
- Satisfeita: a macro segue imediatamente (com o espaçamento mínimo de 50 ms antes do passo seguinte).
- O indicador e a linha de estado mostram `Aguardando "Baixar PDF"… 3 s` (ou `Aguardando a cor em 640, 520…`,
  `Aguardando a janela "Salvar como"…`).
- **Antes de cada leitura**, as proteções dos outros passos, com uma divisão:
  - problema **de fora** do aplicativo (outra janela na frente, outro programa cobrindo o ponto): a macro **pausa**
    (como hoje) e o relógio do tempo máximo **para**; tempo em pausa não conta como "não apareceu";
  - problema **do próprio aplicativo** (o diálogo do ponto ainda não abriu, uma janela de "carregando" do próprio
    sistema por cima do botão): conta como **"ainda não"**, e o relógio corre. Assim um sistema travado termina no
    tempo máximo com a mensagem da condição, não no limite de 5 min de pausa.
  - A condição **Janela** não tem ponto: só exige o aplicativo na frente.
- **Tempo esgotado:** para com `"Baixar PDF" não ficou disponível em 30 s. A macro parou antes do passo 6.`; o passo
  fica selecionado no Studio. O passo seguinte não é executado.
- **Leitura impossível** (UI Automation não responde, a janela some): conta como "ainda não"; se persistir até o tempo
  máximo, para com a mesma mensagem. Janela vinculada fechada: para como hoje.
- **F8:** para na hora, também no meio de uma leitura (uma leitura lenta é abandonada).
- **Leituras inconclusivas nunca satisfazem a condição:** uma leitura que falha, que passa de 500 ms ou que encontra
  um elemento de outro tipo, nome ou processo conta como "ainda não" (e zera a contagem das duas leituras). A
  resposta de uma leitura que chega depois do limite, ou depois de a macro ter sido parada ou pausada, é descartada:
  ela nunca faz a macro avançar.
- **Limite conhecido:** duas leituras positivas confirmam que a condição observável ficou satisfeita, não que seja o
  elemento "certo" no sentido do usuário. Isso vale sobretudo para **Cor**: outra coisa da mesma cor no mesmo lugar
  satisfaz a condição. O diálogo de criação diz isso quando o tipo é Cor.

### Os três tipos em detalhe

- **Elemento (UI Automation):** no F7, o **controle interativo** sob o ponto (botão, link, campo, caixa de seleção,
  item de menu, aba, item de lista): se o ponto cai no texto dentro de um botão, sobe até o botão (até 3 níveis). Só
  quando não há controle interativo ali vale o primeiro elemento com nome (por exemplo, um texto "Concluído"). Guarda
  nome e tipo de controle. O elemento lido precisa ser do processo da janela vinculada. Na execução, lê o elemento
  sob o mesmo ponto pelo mesmo caminho e considera **disponível** quando nome e tipo batem, está habilitado e não está
  fora da tela. Funciona em programas do Windows e na maioria das páginas no Chrome/Edge; programas que não expõem
  UI Automation (vários jogos, alguns sistemas antigos) não têm esse tipo: o diálogo mostra o motivo e sugere **Cor**.
- **Cor:** a cor média de um quadrado de 5×5 pixels em volta do ponto. Satisfeita quando cada canal (R, G, B) está a
  até 24 da cor capturada. **Antes e depois** de ler os pixels, o V-Clicker confere que a janela sob o centro e os
  quatro cantos do quadrado é a janela do ponto; se não for (outra janela por cima, mesmo que por um instante), a
  leitura é inconclusiva. A cor é capturada no F7: o usuário aponta quando o botão já está visível.
- **Janela:** classe e título do diálogo sob o ponto, como as âncoras de passo do Macro Studio. Satisfeita quando um
  diálogo visível da janela vinculada com essa classe e título existe. Só aparece se o ponto estiver num diálogo.

## Arquivo

Um tipo de passo novo, com a mesma regra dos outros (o ponto e a janela-âncora iguais aos passos de mouse):

```
waitfor element 640 520 "Button" "Baixar PDF" timeout=30000 was=8000
waitfor color 640 520 3A6BD1 timeout=30000 in "#32770" "Salvar como"
waitfor window "#32770" "Salvar como" timeout=30000
```

`was=` é a duração da espera original e `recorded=` o tempo gravado dela (os dois ausentes quando a condição foi
inserida do zero; `recorded=` ausente quando a espera foi inserida à mão).

## O que fica para o construtor

- A estrutura do código (onde ficam a captura, a leitura e o diálogo), desde que a avaliação das condições seja
  testável sem tela real (uma função de leitura injetável no player, como o guard).
- Como cancelar uma leitura de UI Automation lenta sem travar o player (por exemplo, em outra thread com limite de
  tempo por leitura).
- O layout do diálogo, seguindo o tema atual.

Decisões tomadas no planejamento:
- O passo de condição só espera; não clica (o clique é o passo gravado depois dele).
- A regra das duas leituras seguidas recomeça depois de uma pausa.

## Critérios de aceitação

- Converter `Esperar 8 s` em condição e voltar dá exatamente o passo original.
- Elemento que aparece depois de 2 s: o passo seguinte começa em ~2 s (não 8 s, não 30 s).
- Elemento que nunca aparece: a macro para no tempo máximo, o passo seguinte não envia nada, a mensagem diz qual passo.
- Uma leitura positiva isolada (piscar) não satisfaz a condição.
- Com outra janela por cima do ponto, a macro pausa e o tempo máximo não corre.
- F8 durante a observação para em menos de 100 ms.
- Arquivo com `waitfor` salvo e reaberto fica idêntico.
