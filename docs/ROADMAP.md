# Roadmap

Itens decididos, mas fora da entrega atual. Cada um ganha planejamento próprio quando chegar a vez.

## Próxima rodada

### Perfis automáticos por aplicativo (V2, depois do Window Lock): implementado

[Design](superpowers/specs/2026-10-21-profiles-design.md). As regras abaixo valeram como base; a exportação e a
importação (com as macros do aplicativo) entraram a pedido, e os atalhos continuam fora do perfil.

O V-Clicker lembra as configurações de clique de cada aplicativo vinculado no Window Lock, identificado pelo executável
(o mesmo que o Window Lock já persiste).

| Aplicativo     | Exemplo de configuração salva |
| -------------- | ----------------------------- |
| Roblox         | 15 CPS, botão esquerdo        |
| Cookie Clicker | 50 CPS, botão esquerdo        |
| Outro          | 10 CPS, botão direito         |

Regras:

1. **Automático:** o usuário não cria, nomeia nem salva perfis. Selecionar o aplicativo no Window Lock carrega o perfil dele; mudar as configurações atualiza o perfil.
2. **Window Lock desligado:** vale a configuração global (a última usada), como hoje.
3. **Atalhos globais:** F6/F7/F8 (e qualquer atalho personalizado) são os mesmos para todos os aplicativos; nunca fazem parte do perfil.
4. **Sem troca durante a execução:** um perfil só é carregado com o V-Clicker parado, nunca com uma sessão ativa ou pausada.

### Window Lock: formas extras de selecionar a janela

A V1 seleciona por uma lista das janelas abertas. Ficam para depois, se valerem a complexidade:

- **Mira:** arrastar um alvo até a janela desejada (programas com várias janelas).
- **Captura por atalho:** com o aplicativo em primeiro plano, um atalho registra a janela (jogos em tela cheia).

### Window Lock: restrição estrita à área da janela (opcional, desligada por padrão)

Hoje, no modo cursor, o Window Lock confere só se a janela vinculada está em primeiro plano (decisão mantida para não
atrapalhar jogos que ocultam, prendem ou reposicionam o cursor). Limite conhecido: se o cursor estiver sobre outro
aplicativo (por exemplo, em outro monitor, com o jogo em janela), um clique pode cair fora do jogo antes de a pausa
entrar; o teste de integração e a revisão de código reproduziram isso.

Opção futura: só clicar se a janela sob o cursor pertencer à janela vinculada; caso contrário, pausar como "ponto fora
da janela". Desligada por padrão, com um aviso de que pode interferir em jogos que manipulam o cursor.

### Window Lock: seguir a janela quando o jogo a recria

Hoje, se o aplicativo fecha e reabre a própria janela (alguns jogos fazem isso ao alternar a tela cheia, ou passam por
uma janela de abertura com o mesmo executável), a sessão para como "janela fechada". Opção futura: por 2 a 3 s, procurar
outra janela do mesmo processo ou executável e continuar nela.

## Nova rodada (SafeClick → Profiles → Interface adaptativa → Developer Mode → AI Macro Builder)

- **SafeClick V1:** implementado ([design](superpowers/specs/2026-10-21-safeclick-design.md)): simulação passiva sobre
  uma captura da janela, revisão de segurança antes de atalhos impactantes (uma vez por versão da macro) e limite
  opcional de duração.
- **SafeClick, depois da V1:** camada de marcadores sobre a janela real do aplicativo (exige lidar com
  compatibilidade, posicionamento e foco); ficou fora da V1 por decisão do usuário.
- **Profiles V2:** implementado ([design](superpowers/specs/2026-10-21-profiles-design.md)): perfis automáticos por
  aplicativo do Window Lock, exportar e importar com as macros do aplicativo; atalhos ficam globais.
- **Profiles, depois da V2:** macro preferida por aplicativo no Studio e atalho global para executar uma macro.
- **Interface adaptativa:** implementada ([design](superpowers/specs/2026-10-21-adaptive-interface-design.md)):
  modo simples (velocidade, botão do mouse, atalhos) e avançado; instalações novas começam no simples.
- **Próximos:** Developer Mode, AI Macro Builder (exige decidir sobre a promessa de "sem rede").

## Fase de produtividade

Ordem: Macro Studio → Conditional Click → Smart Target. Ficam isoladas na janela do Macro Studio; o usuário casual
não precisa entender nada disso para usar o autoclicker.

- **Macro Studio V1:** implementado ([design](superpowers/specs/2026-10-21-macro-studio-design.md)).
- **Conditional Click V1:** implementado ([design](superpowers/specs/2026-10-22-conditional-click-design.md)): esperar
  até elemento (onde estiver na janela), cor ou janela.
- **Smart Target V1:** implementado ([design](superpowers/specs/2026-10-23-smart-target-design.md)): clicar no
  elemento (pelo nome) ou na imagem (pela aparência) onde estiver; nunca com ambiguidade.
- **Macro Studio, depois da V1:**
  - Deslocar o ponto de um clique a cada repetição (listas de linhas fixas). Adiado porque falha com rolagem, linhas
    de alturas diferentes e mudanças de layout; a V1 navega pelo teclado gravado.
  - Identificar a aba ou a página do navegador, para pausar quando o usuário troca de aba. A troca de título da
    janela pode servir de proteção complementar, mas não identifica uma página com segurança.
  - Macros que passam por vários aplicativos.
  - Atalho global para executar uma macro.
- **Conditional Click, depois da V1:** condições compostas (E/OU), "se… senão" e repetir até; esperar até algo **sumir** (por exemplo, um "carregando").
- **Smart Target, depois da V1:** alvo em arrastar, passar o mouse e rolar; rolar a página até o alvo aparecer;
  distinguir elementos iguais pelo contexto ("o Baixar PDF da linha de março"); imagem em outra escala.
- **Dados de planilha:** preencher campos a partir de uma planilha (exige digitar texto, não só clicar).
