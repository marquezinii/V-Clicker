# Roadmap

Itens decididos, mas fora da entrega atual. Cada um ganha planejamento próprio quando chegar a vez.

## Próxima rodada

### Perfis automáticos por aplicativo (V2, depois do Window Lock)

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

## Fase de produtividade

Ordem: Macro Studio → Conditional Click → Smart Target. Ficam isoladas na janela do Macro Studio; o usuário casual
não precisa entender nada disso para usar o autoclicker.

- **Macro Studio V1:** implementado ([design](superpowers/specs/2026-10-21-macro-studio-design.md)).
- **Conditional Click V1:** implementado ([design](superpowers/specs/2026-10-22-conditional-click-design.md)): esperar
  até elemento, cor ou janela, num ponto fixo. Próximo: Smart Target (achar o elemento mesmo que mude de lugar).
- **Macro Studio, depois da V1:**
  - Deslocar o ponto de um clique a cada repetição (listas de linhas fixas). Adiado porque falha com rolagem, linhas
    de alturas diferentes e mudanças de layout; a V1 navega pelo teclado gravado.
  - Identificar a aba ou a página do navegador, para pausar quando o usuário troca de aba. A troca de título da
    janela pode servir de proteção complementar, mas não identifica uma página com segurança.
  - Macros que passam por vários aplicativos.
  - Atalho global para executar uma macro.
- **Conditional Click, depois da V1:** condições compostas (E/OU), "se… senão" e repetir até; esperar até algo **sumir** (por exemplo, um "carregando").
- **Smart Target:** achar o alvo por elemento de interface ou imagem, mesmo que a janela mude de posição.
- **Dados de planilha:** preencher campos a partir de uma planilha (exige digitar texto, não só clicar).
