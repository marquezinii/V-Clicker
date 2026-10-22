# AI Macro Builder

Item 8 do pedido: o usuário descreve uma tarefa em linguagem natural e a IA monta uma proposta de macro editável, que
ele revisa e autoriza. As decisões foram tomadas por mim, sem perguntas, como o usuário pediu; as que fecham uma
escolha estão marcadas como **decisão**.

## Intenção

Encurtar o caminho entre "eu sei o que quero" e uma macro: "clique neste botão a cada dois segundos, mas só enquanto
esta janela estiver aberta" vira passos no Macro Studio, prontos para revisar, completar e executar. A IA nunca age
sozinha: ela só escreve uma proposta.

## A promessa "sem rede"

O PRODUCT.md promete "sem rede, sem telemetria". **Decisão:** a promessa passa a ser "nada sai do computador sem você
pedir". O V-Clicker continua sem nenhuma conexão por padrão; o AI Macro Builder é opcional, desligado até o usuário
colocar a própria chave da API da Anthropic, e só se conecta no instante em que ele aperta **Gerar**. A janela diz,
antes do primeiro uso, exatamente o que é enviado.

- **O que é enviado:** o texto que o usuário escreveu e o nome do aplicativo da macro aberta (se houver).
- **O que nunca é enviado:** capturas de tela, títulos de janelas, macros existentes, configurações, a chave para
  qualquer outro lugar que não a API da Anthropic.
- A chave fica no `settings.ini` cifrada com o DPAPI do Windows (só o mesmo usuário no mesmo computador a lê) e pode
  ser apagada na própria janela.

## Fluxo

1. No Macro Studio, **Criar com IA** abre uma janela com uma caixa de texto ("Descreva a tarefa"), a explicação do
   que é enviado, o campo da chave (no primeiro uso) e **Gerar**.
2. Gerar chama a API em segundo plano; a janela mostra "Gerando…" e pode ser cancelada.
3. A resposta vira uma macro nova, **não salva**, aberta no Studio, com um aviso no topo: "Proposta da IA: revise
   cada passo, capture os pontos marcados e só então execute." Nada é executado.
4. A proposta pode ser editada como qualquer macro. Executar segue as regras de sempre: SafeClick (revisão de
   segurança para ações impactantes, simulação disponível), F8, Window Lock da macro.

## O que a IA pode propor

**Decisão:** a IA responde num formato estruturado (JSON Schema validado pela API) e o V-Clicker converte, validando
cada campo como faz com um arquivo `.vcmacro`; nada da resposta vira passo sem passar por essa validação.

- Passos: clicar (botão, simples ou duplo), digitar texto, tecla, atalho, esperar N ms, esperar até uma janela abrir
  (pelo título), repetir N vezes ou até parar, limite de duração.
- **Pontos:** a IA não vê a tela (**decisão**: nenhuma captura é enviada), então cliques vêm sem ponto e aparecem
  marcados "capture o ponto (F7)"; o Studio já tem a captura por F7 e o alvo inteligente para isso. Uma macro com
  ponto pendente não executa até ser capturado.
- **"Só enquanto esta janela estiver aberta":** a macro já para quando a janela do aplicativo fecha; a proposta usa
  "repetir até parar" e explica isso numa nota.
- Pedidos fora do que o V-Clicker faz (por exemplo, ler o conteúdo da tela e decidir) voltam como uma nota da IA, sem
  passos inventados.
- Pressionar F6/F7/F8 (ou os atalhos configurados) é recusado como em qualquer macro.

## Modelo e API

- Claude Opus 5 (`claude-opus-5`) pela Messages API, com saída estruturada (`output_config.format` com JSON Schema),
  esforço `low` (tarefa curta e bem definida) e `fallbacks: "default"` para recusas.
- Chamada HTTP direta com `HttpClient` do .NET Framework, sem o SDK (**decisão**: o V-Clicker é um `.exe` único sem
  dependências de terceiros; o SDK traria DLLs junto). O formato do pedido segue a documentação da API.
- Erros (sem conexão, chave inválida, limite de uso, recusa) aparecem na janela com a causa, sem criar macro.

## Fora desta versão

- Enviar uma captura para a IA localizar botões: exigiria mandar a tela para fora do computador.
- A IA editar uma macro existente.
- Executar a proposta sem revisão: nunca.
