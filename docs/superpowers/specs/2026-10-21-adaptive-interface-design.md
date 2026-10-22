# Interface adaptativa: modo simples e modo avançado

Item 10 do pedido. As decisões foram tomadas por mim, sem perguntas, como o usuário pediu; as que fecham uma escolha
estão marcadas como **decisão** para a revisão do PR.

## Intenção

Quem só quer apertar F6 e clicar não deve encarar um painel com Window Lock, posições, modos e um editor de macros.
O V-Clicker passa a ter dois níveis, com um seletor **Simples | Avançado** no topo da coluna da esquerda, sempre no
mesmo lugar.

## Modo simples

Uma coluna só (a janela fica com cerca de metade da largura):

1. Seletor Simples | Avançado.
2. Visor (estado, CPS solicitado, cliques, CPS enviado, tempo).
3. Tecla Iniciar/Parar (com o atalho impresso).
4. Velocidade em CPS, com as teclas 5/10/15/20/50 e a linha "≈ 100 ms entre cliques".
5. Botão do mouse (esquerdo, meio, direito): **decisão**, entra no simples porque muitos jogos usam o direito.
6. Atalhos (F6, F8, F7).
7. Rodapé com os créditos.

Some no simples: CPS/intervalo (fica sempre em CPS), variação aleatória, tipo, repetições, modo do atalho, atraso,
posição, Window Lock, opções da janela, o botão do Macro Studio e o link Perfis. "Reiniciar como admin" fica,
porque as mensagens sobre aplicativos que rodam como administrador o citam.

## Honestidade: o que continua valendo

Esconder um controle não muda o que ele faz. Tudo o que foi configurado no avançado continua valendo no simples
(**decisão**: desligar em silêncio mudaria o comportamento ao trocar de modo). Para ninguém se surpreender, o simples
mostra uma linha "Também valendo: Window Lock (Roblox) · posição fixa (812, 440) · 100 cliques" sempre que algo fora
do padrão estiver ativo. A linha some quando não há nada.

## Padrão

- Instalação nova (sem arquivo de configurações): começa no **simples**.
- Quem já usa o V-Clicker (arquivo existente sem a chave): continua no **avançado**, para nada sumir depois da
  atualização.
- A escolha é salva (`Advanced=` no `settings.ini`).

## Detalhes

- Trocar de modo nunca para nem inicia nada e funciona durante uma execução.
- A janela se reajusta ao conteúdo; em telas baixas continua rolando, como antes.
- Atalhos globais, indicador, bandeja e sons funcionam igual nos dois modos.
- O Macro Studio aberto continua aberto ao trocar para o simples.

## Fora desta versão

- Trocar de modo sozinho conforme o uso: mudaria a tela sem o usuário pedir.
