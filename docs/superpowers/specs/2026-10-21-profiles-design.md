# Profiles V2: perfis automáticos por aplicativo

Item 6 do pedido ("Profiles — perfis inteligentes"), conciliado com as regras que o ROADMAP já tinha para perfis
automáticos. O usuário pediu que eu tomasse as decisões sem perguntar; as que mudam o pedido original estão marcadas
como **decisão** e podem ser revistas na revisão do PR.

## Intenção

Quem usa o V-Clicker em mais de um jogo ou programa não deve reconfigurar a velocidade, o botão e a posição a cada
troca. O V-Clicker lembra a configuração de clique de cada aplicativo do Window Lock e a traz de volta quando o
aplicativo é escolhido de novo. Além disso, o perfil de um aplicativo pode ser exportado (com as macros dele) e
importado em outro computador.

## Regras

1. **Automático.** O perfil é do aplicativo do Window Lock (o executável, como o Window Lock já identifica). Ninguém
   cria, nomeia ou salva perfis: com o Window Lock ligado, toda mudança de configuração atualiza o perfil do
   aplicativo escolhido.
2. **Carregar = escolher o aplicativo.** Escolher um aplicativo em "Escolher…" ou ligar o Window Lock com um
   aplicativo já escolhido carrega o perfil dele, se existir, e avisa no visor ("Perfil de Roblox carregado: 15 CPS,
   botão esquerdo"). Sem perfil, a configuração atual vira o ponto de partida do perfil desse aplicativo. Este é o
   "sugerir o último perfil usado para o programa" do pedido: a sugestão é aplicada e dita, sem uma pergunta a mais
   (**decisão**: uma pergunta a cada troca atrapalharia quem joga).
3. **Window Lock desligado:** vale a configuração global (a última usada), como antes.
4. **Nunca durante uma execução.** Um perfil só é carregado com o V-Clicker parado. Os controles que carregam perfis
   já ficam desativados durante uma execução, inclusive o link "Perfis" (uma importação durante a execução não
   poderia ser aplicada e seria sobrescrita pela configuração em uso).
5. **O que entra no perfil:** velocidade (e a forma de editá-la, CPS ou intervalo), variação aleatória, botão, tipo,
   repetições, posição (cursor ou fixa, com X/Y), atraso inicial, modo do atalho (alternar/segurar) e o que fazer
   quando o aplicativo perde o foco. São as "posições" e "condições de execução" do pedido.
6. **O que não entra (decisão, mantém a regra do ROADMAP):** atalhos globais (F6/F7/F8 e personalizados), opções da
   janela, indicador, sons e idioma. O atalho de parar é um controle de segurança: precisa significar a mesma coisa em
   qualquer aplicativo. O pedido sugeria hotkeys no perfil; ficou de fora por isso.
7. **Macros:** já pertencem a um aplicativo (cada macro guarda o executável). Elas não são copiadas para o perfil
   local, mas **entram na exportação**: exportar o perfil de um aplicativo leva junto as macros salvas dele.

## Exportar e importar

- Uma janela "Perfis" (link no rodapé da janela principal) lista os perfis salvos, do mais recente para o mais
  antigo, com um resumo ("15 CPS · esquerdo · 2 macros"), e tem Importar…, Exportar… e Excluir.
- **Arquivo `.vcprofile`** (texto, UTF-8): a linha `V-Clicker profile 1`, cabeçalho `exe=`, `name=`, `used=` e as
  chaves de configuração no mesmo formato do `settings.ini`; na exportação, cada macro vem depois de uma linha
  `[macro]`, no formato `.vcmacro` sem alterações.
- **Importar** valida tudo como o `settings.ini` (valores fora da faixa são limitados; chaves fora do perfil, como
  atalhos, são ignoradas). Macros ilegíveis ou de outro aplicativo são puladas e contadas no aviso. Uma macro
  importada idêntica a uma salva não é duplicada; com nome igual e conteúdo diferente, recebe " (2)".
- Substituir um perfil existente pede confirmação.
- **SafeClick:** a aprovação de segurança pertence à cópia local da macro; uma macro importada é uma cópia nova e
  passa pela revisão de segurança no primeiro uso, como qualquer macro recebida.
- Arquivos acima de 20 MB são recusados (imagens de alvos inteligentes deixam macros grandes, mas não tanto).

## Armazenamento

`Profiles\<executável>.vcprofile` ao lado do arquivo de configurações (como a pasta `Macros`, inclusive no modo
portátil).

## Fora desta versão

- Perfis do Macro Studio (macro preferida por aplicativo) e atalho global para executar uma macro: ficam no ROADMAP.
- Detectar o aplicativo em primeiro plano sem o Window Lock e trocar de perfil sozinho: troca configurações sem o
  usuário pedir; não entra.
