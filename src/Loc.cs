using System;
using System.Collections.Generic;
using System.Globalization;

namespace VClicker
{
    /// <summary>Tiny two-language (Portuguese / English) string table.</summary>
    internal static class Loc
    {
        private static int index; // 0 = pt, 1 = en

        /// <summary>"pt", "en" or "auto" (follow the Windows display language).</summary>
        public static void SetLanguage(string setting)
        {
            if (setting == "pt") index = 0;
            else if (setting == "en") index = 1;
            else index = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "pt" ? 0 : 1;
        }

        public static string Current => index == 0 ? "pt" : "en";

        internal static IEnumerable<KeyValuePair<string, string[]>> All => Table;

        public static string T(string key) =>
            Table.TryGetValue(key, out string[] texts) ? texts[index] : key;

        public static string F(string key, params object[] args) =>
            string.Format(CultureInfo.CurrentCulture, T(key), args);

        private static readonly Dictionary<string, string[]> Table = new Dictionary<string, string[]>
        {
            // header / status
            ["subtitle"] = new[] { "Autoclicker rápido e preciso", "Fast, precise autoclicker" },
            ["start"] = new[] { "Iniciar", "Start" },
            ["stop"] = new[] { "Parar", "Stop" },
            ["status.idle"] = new[] { "Parado", "Stopped" },
            ["status.countdown"] = new[] { "Iniciando em {0}", "Starting in {0}" },
            ["status.clicking"] = new[] { "Clicando", "Clicking" },
            ["status.done"] = new[] { "Concluído", "Finished" },
            ["status.released"] = new[] { "Tecla solta", "Key released" },
            ["status.blocked"] = new[] { "Bloqueado", "Blocked" },
            ["status.paused"] = new[] { "Pausado", "Paused" },
            ["status.waiting"] = new[] { "Pausado · Aguardando {0}", "Paused · Waiting for {0}" },
            ["status.outside"] = new[] { "Pausado · ponto fora da janela", "Paused · spot outside the window" },
            ["stat.clicks"] = new[] { "CLIQUES", "CLICKS" },
            ["stat.requested"] = new[] { "SOLICITADO", "REQUESTED" },
            ["stat.sent"] = new[] { "ENVIADO", "SENT" },
            ["stat.time"] = new[] { "TEMPO", "TIME" },
            ["stat.note"] = new[] { "Enviados ao Windows; o jogo pode reconhecer menos.", "Sent to Windows; the game may register fewer." },
            ["stat.note.tip"] = new[] { "Cliques e CPS contam o que o Windows aceitou (um clique duplo conta 1). O V-Clicker não sabe quantos o jogo reconheceu.", "Clicks and CPS count what Windows accepted (a double click counts as 1). V-Clicker cannot know how many the game registered." },

            // interval
            ["card.interval"] = new[] { "VELOCIDADE DOS CLIQUES", "CLICK SPEED" },
            ["speed.cps"] = new[] { "CPS", "CPS" },
            ["speed.interval"] = new[] { "Intervalo", "Interval" },
            ["speed.perSecond"] = new[] { "Cliques por segundo", "Clicks per second" },
            ["rate.ms"] = new[] { "≈ {0} ms entre cliques", "≈ {0} ms between clicks" },
            ["rate.belowCps"] = new[] { "Abaixo de 0,1 CPS: ajuste no modo Intervalo.", "Below 0.1 CPS: set it in Interval mode." },
            ["rate.double"] = new[] { "  ·  duplo = {0} cliques/s", "  ·  double = {0} clicks/s" },
            ["unit.h"] = new[] { "h", "h" },
            ["unit.min"] = new[] { "min", "min" },
            ["unit.s"] = new[] { "s", "s" },
            ["unit.ms"] = new[] { "ms", "ms" },
            ["rate.perSecond"] = new[] { "≈ {0} cliques por segundo", "≈ {0} clicks per second" },
            ["rate.every"] = new[] { "1 clique a cada {0} s", "1 click every {0} s" },
            ["jitter"] = new[] { "Variação aleatória", "Random jitter" },
            ["jitter.tip"] = new[] { "Varia cada intervalo em ± este percentual, para cliques menos mecânicos.", "Varies each interval by ± this percentage for less mechanical clicking." },

            // click
            ["card.click"] = new[] { "CLIQUE E INÍCIO", "CLICK & START" },
            ["button"] = new[] { "Botão", "Button" },
            ["button.left"] = new[] { "Esquerdo", "Left" },
            ["button.middle"] = new[] { "Meio", "Middle" },
            ["button.right"] = new[] { "Direito", "Right" },
            ["type"] = new[] { "Tipo", "Type" },
            ["type.single"] = new[] { "Simples", "Single" },
            ["type.double"] = new[] { "Duplo", "Double" },
            ["repeat"] = new[] { "Repetir", "Repeat" },
            ["repeat.infinite"] = new[] { "Sem fim", "Forever" },
            ["repeat.count"] = new[] { "Vezes", "Times" },

            // position
            ["card.position"] = new[] { "POSIÇÃO", "POSITION" },
            ["pos.cursor"] = new[] { "Cursor atual", "At cursor" },
            ["pos.fixed"] = new[] { "Posição fixa", "Fixed spot" },
            ["capture"] = new[] { "Capturar", "Capture" },
            ["capture.tip"] = new[] { "Grava a posição atual do cursor (atalho: {0}).", "Saves the current cursor position (hotkey: {0})." },

            // hotkeys
            ["card.hotkeys"] = new[] { "ATALHOS", "HOTKEYS" },
            ["hk.toggle"] = new[] { "Iniciar / parar", "Start / stop" },
            ["hk.stop"] = new[] { "Parar", "Stop" },
            ["hk.capture"] = new[] { "Capturar posição", "Capture position" },
            ["hk.press"] = new[] { "Pressione o atalho…", "Press the shortcut…" },
            ["mode"] = new[] { "Modo", "Mode" },
            ["mode.toggle"] = new[] { "Alternar", "Toggle" },
            ["mode.hold"] = new[] { "Segurar", "Hold" },
            ["mode.tip"] = new[] { "Alternar: o atalho liga e desliga. Segurar: clica só enquanto o atalho estiver pressionado.", "Toggle: the hotkey switches on and off. Hold: clicks only while the hotkey is held down." },
            ["delay"] = new[] { "Atraso inicial", "Start delay" },
            ["delay.tip"] = new[] { "Espera antes do primeiro clique, para você posicionar o mouse (ignorado no modo Segurar).", "Wait before the first click so you can position the mouse (ignored in Hold mode)." },

            // window lock
            ["card.lock"] = new[] { "WINDOW LOCK", "WINDOW LOCK" },
            ["lock.enable"] = new[] { "Só em um aplicativo", "Only in one app" },
            ["lock.tip"] = new[] { "Os cliques só acontecem com o aplicativo escolhido em primeiro plano; num Alt+Tab, o V-Clicker pausa.", "Clicks only happen while the chosen app is in front; on Alt+Tab, V-Clicker pauses." },
            ["lock.none"] = new[] { "Nenhum escolhido", "None chosen" },
            ["lock.choose"] = new[] { "Escolher…", "Choose…" },
            ["lock.onFocusLoss"] = new[] { "Quando o aplicativo perde o foco (Alt+Tab): pausar e retomar ao voltar, ou parar de vez.", "When the app loses focus (Alt+Tab): pause and resume when it is back, or stop for good." },
            ["lock.pause"] = new[] { "Pausar e retomar", "Pause & resume" },
            ["lock.stop"] = new[] { "Parar", "Stop" },
            ["picker.title"] = new[] { "Escolher aplicativo", "Choose an app" },
            ["picker.hint"] = new[] { "Selecione a janela do aplicativo:", "Select the app's window:" },
            ["picker.which"] = new[] { "{0} tem mais de uma janela. Escolha em qual clicar:", "{0} has more than one window. Pick the one to click in:" },
            ["picker.empty"] = new[] { "Nenhuma janela de aplicativo aberta.", "No app windows are open." },
            ["picker.ok"] = new[] { "Selecionar", "Select" },
            ["picker.cancel"] = new[] { "Cancelar", "Cancel" },

            // on-screen indicator
            ["hud.starting"] = new[] { "Começa em {0}", "Starts in {0}" },
            ["hud.waiting"] = new[] { "Aguardando {0}", "Waiting for {0}" },
            ["hud.switchTo"] = new[] { "Ative {0}", "Switch to {0}" },
            ["hud.open"] = new[] { "Abra {0}", "Open {0}" },
            ["hud.chooseApp"] = new[] { "Escolha o aplicativo", "Choose the app" },
            ["hud.outside"] = new[] { "Ponto fora da janela", "Spot outside the window" },
            ["hud.closed"] = new[] { "{0} fechou", "{0} closed" },
            ["hud.focusLost"] = new[] { "Foco perdido", "Focus lost" },
            ["hud.timeout"] = new[] { "Pausa longa demais", "Paused too long" },
            ["hud.blocked"] = new[] { "Clique recusado", "Click refused" },
            ["hud.elevated"] = new[] { "Precisa de admin", "Needs admin" },

            // options / footer
            ["opt.top"] = new[] { "Sempre no topo", "Always on top" },
            ["opt.tray"] = new[] { "Minimizar para a bandeja", "Minimize to tray" },
            ["opt.sounds"] = new[] { "Avisos sonoros", "Sound cues" },
            ["opt.hud"] = new[] { "Indicador na tela", "On-screen indicator" },
            ["opt.hud.tip"] = new[] { "Um pequeno indicador de estado no canto da tela, por cima do jogo. Não recebe cliques nem foco.", "A small status indicator in a screen corner, over the game. It never takes clicks or focus." },
            ["hint"] = new[] { "{0} inicia/para  ·  {1} para  ·  {2} captura a posição", "{0} starts/stops  ·  {1} stops  ·  {2} captures position" },
            ["admin.on"] = new[] { "Administrador", "Administrator" },
            ["link.admin"] = new[]{ "Reiniciar como admin", "Restart as admin" },
            ["link.admin.tip"] = new[] { "Necessário para clicar em programas que rodam como administrador.", "Needed to click inside programs that run as administrator." },
            ["tray.show"] = new[] { "Abrir V-Clicker", "Open V-Clicker" },
            ["tray.exit"] = new[] { "Sair", "Exit" },

            // messages
            ["msg.hkTaken"] = new[] { "{0} já está em uso por outro aplicativo.", "{0} is already used by another app." },
            ["msg.hkDuplicate"] = new[] { "{0} já é usado por outro atalho do V-Clicker.", "{0} is already used by another V-Clicker shortcut." },
            ["msg.hkInvalid"] = new[] { "Use uma tecla F, de navegação ou combine com Ctrl/Alt.", "Use an F key, a navigation key, or combine with Ctrl/Alt." },
            ["msg.hkUnavailable"] = new[] { "O atalho {0} está indisponível (outro app o usa). Troque-o em \"Atalhos\".", "The {0} shortcut is unavailable (another app uses it). Change it under \"Hotkeys\"." },
            ["msg.blocked"] = new[] { "O Windows recusou o clique e o V-Clicker parou (tela bloqueada ou aviso de permissão aberto?).", "Windows refused the click and V-Clicker stopped (locked screen or a permission prompt open?)." },
            ["msg.saveFailed"] = new[] { "Não foi possível salvar as configurações (pasta somente leitura?).", "Could not save the settings (read-only folder?)." },
            ["msg.captured"] = new[] { "Posição capturada: X {0}, Y {1}", "Position captured: X {0}, Y {1}" },
            ["msg.done"] = new[] { "Concluído: {0} cliques.", "Finished: {0} clicks." },
            ["msg.lockNone"] = new[] { "Escolha um aplicativo no Window Lock para começar.", "Choose an app under Window Lock to start." },
            ["msg.lockFocus"] = new[] { "Ative a janela de {0} para começar.", "Switch to the {0} window to start." },
            ["msg.lockNotOpen"] = new[] { "Abra {0} para começar.", "Open {0} to start." },
            ["msg.lockFocusFailed"] = new[] { "O Windows não deixou trazer {0} para a frente. Clique nele e use {1}.", "Windows would not bring {0} to the front. Click it and press {1}." },
            ["msg.lockElevated"] = new[] { "{0} roda como administrador: use \"Reiniciar como admin\" para clicar nele.", "{0} runs as administrator: use \"Restart as admin\" to click in it." },
            ["msg.lockOutside"] = new[] { "A posição fixa está fora da janela de {0}.", "The fixed spot is outside the {0} window." },
            ["msg.focusLost"] = new[] { "{0} perdeu o foco e o V-Clicker parou.", "{0} lost focus and V-Clicker stopped." },
            ["msg.targetClosed"] = new[] { "A janela de {0} foi fechada e o V-Clicker parou.", "The {0} window was closed and V-Clicker stopped." },
            ["msg.pauseTimeout"] = new[] { "Pausado por mais de 5 minutos: o V-Clicker parou.", "Paused for over 5 minutes: V-Clicker stopped." },
            ["msg.suspended"] = new[] { "O computador foi bloqueado ou suspenso e o V-Clicker parou.", "The computer was locked or went to sleep, so V-Clicker stopped." },
            ["msg.adminFailed"] = new[] { "Não foi possível reiniciar como administrador.", "Could not restart as administrator." },
            ["msg.crash"] = new[] { "Ocorreu um erro inesperado:\n\n{0}", "An unexpected error occurred:\n\n{0}" },

            // macro steps
            ["step.left"] = new[] { "esquerdo", "left" },
            ["step.right"] = new[] { "direito", "right" },
            ["step.middle"] = new[] { "do meio", "middle" },
            ["step.click"] = new[] { "Clicar ({0}) em {1}, {2}", "Click ({0}) at {1}, {2}" },
            ["step.double"] = new[] { "Clique duplo ({0}) em {1}, {2}", "Double-click ({0}) at {1}, {2}" },
            ["step.drag"] = new[] { "Arrastar ({0}) de {1}, {2} até {3}, {4}", "Drag ({0}) from {1}, {2} to {3}, {4}" },
            ["step.hover"] = new[] { "Passar o mouse sobre {0}, {1}", "Hover over {0}, {1}" },
            ["step.scrollUp"] = new[] { "Rolar {0} para cima em {1}, {2}", "Scroll {0} up at {1}, {2}" },
            ["step.scrollDown"] = new[] { "Rolar {0} para baixo em {1}, {2}", "Scroll {0} down at {1}, {2}" },
            ["step.type"] = new[] { "Digitar \"{0}\"", "Type \"{0}\"" },
            ["step.key"] = new[] { "Tecla {0}", "Key {0}" },
            ["step.shortcut"] = new[] { "Atalho {0}", "Shortcut {0}" },
            ["step.wait"] = new[] { "Esperar {0} s", "Wait {0} s" },
            ["step.recorded"] = new[] { " (gravado: {0} s)", " (recorded: {0} s)" },
            ["step.password"] = new[] { "Senha não gravada", "Password not recorded" },
            ["step.unknown"] = new[] { "Passo desconhecido: {0}", "Unknown step: {0}" },
            ["step.inWindow"] = new[] { " · em \"{0}\"", " · in \"{0}\"" },
            ["step.inMenu"] = new[] { " · no menu", " · in a menu" },

            // macro studio
            ["studio.open"] = new[] { "Macro Studio", "Macro Studio" },
            ["studio.open.tip"] = new[] { "Grave uma tarefa uma vez e repita quantas vezes quiser.", "Record a task once and repeat it as often as you like." },
            ["studio.untitled"] = new[] { "Nova macro (sem nome)", "New macro (unnamed)" },
            ["studio.new"] = new[] { "Nova", "New" },
            ["studio.rename"] = new[] { "Renomear", "Rename" },
            ["studio.delete"] = new[] { "Excluir", "Delete" },
            ["studio.save"] = new[] { "Salvar", "Save" },
            ["studio.record"] = new[] { "Gravar", "Record" },
            ["studio.run"] = new[] { "Executar", "Run" },
            ["studio.stop"] = new[] { "Parar", "Stop" },
            ["studio.repeat"] = new[] { "Repetições", "Repetitions" },
            ["studio.forever"] = new[] { "Até eu parar", "Until I stop" },
            ["studio.empty"] = new[] { "Clique em Gravar, escolha o aplicativo e faça a tarefa uma vez.\nCada clique, tecla e espera aparece aqui como um passo.", "Click Record, choose the app and do the task once.\nEvery click, key and wait shows up here as a step." },
            ["studio.up"] = new[] { "Subir", "Up" },
            ["studio.down"] = new[] { "Descer", "Down" },
            ["studio.duplicate"] = new[] { "Duplicar", "Duplicate" },
            ["studio.remove"] = new[] { "Apagar", "Delete" },
            ["studio.addWait"] = new[] { "+ Espera", "+ Wait" },
            ["studio.undo"] = new[] { "Desfazer", "Undo" },
            ["studio.redo"] = new[] { "Refazer", "Redo" },
            ["studio.detail"] = new[] { "PASSO {0}", "STEP {0}" },
            ["studio.noSelection"] = new[] { "Selecione um passo para ver e editar os valores dele.", "Select a step to see and edit its values." },
            ["studio.button"] = new[] { "Botão", "Button" },
            ["studio.toX"] = new[] { "Até X", "To X" },
            ["studio.toY"] = new[] { "Até Y", "To Y" },
            ["studio.notches"] = new[] { "Rolagem (+ sobe, − desce)", "Scroll (+ up, − down)" },
            ["studio.text"] = new[] { "Texto", "Text" },
            ["studio.seconds"] = new[] { "Segundos", "Seconds" },
            ["studio.count"] = new[] { "Vezes", "Times" },
            ["studio.capture"] = new[] { "Capturar ponto", "Capture point" },
            ["studio.restore"] = new[] { "Restaurar {0} s", "Restore {0} s" },
            ["studio.relative"] = new[] { "Posição a partir do canto da área útil da janela (sem a barra de título).", "Position from the corner of the window's content area (below the title bar)." },
            ["studio.keyInfo"] = new[] { "Para trocar a tecla, grave o passo de novo.", "To change the key, record the step again." },
            ["studio.passwordInfo"] = new[] { "Na execução, a macro para aqui para você digitar a senha. Apague este passo se fizer o login antes de executar.", "When run, the macro stops here so you can type the password. Delete this step if you log in before running." },
            ["studio.unknownInfo"] = new[] { "Este passo vem de uma versão mais nova do V-Clicker. A macro não executa enquanto ele existir.", "This step comes from a newer V-Clicker. The macro will not run while it is here." },

            ["banner.longPause.title"] = new[] { "Pausa longa detectada", "Long pause detected" },
            ["banner.longPause"] = new[] { "Você pode reduzir esperas desnecessárias para acelerar a macro. Confira se alguma delas é necessária para o sistema carregar.", "You can shorten unnecessary waits to speed the macro up. Check whether any of them is needed for the system to load." },
            ["banner.longPause.button"] = new[] { "Definir otimização de pausas", "Set up pause optimization" },
            ["banner.text"] = new[] { "Esta macro contém texto digitado. Ele fica salvo em texto simples no seu computador. Não grave senhas nem dados confidenciais.", "This macro contains typed text. It is saved as plain text on your computer. Do not record passwords or confidential data." },
            ["banner.password"] = new[] { "A macro vai parar no passo \"Senha não gravada\". A detecção de senha depende de o aplicativo marcar o campo como senha.", "The macro will stop at the \"Password not recorded\" step. Password detection depends on the app marking the field as a password." },
            ["banner.browser"] = new[] { "Em navegadores, a macro fica presa a esta janela, mas não percebe troca de aba. Use uma janela só para este sistema e não troque de aba enquanto a macro roda.", "In browsers, the macro is tied to this window but does not notice tab switches. Use a window just for this system and do not switch tabs while the macro runs." },
            ["banner.unknown"] = new[] { "Esta macro tem passos de uma versão mais nova do V-Clicker e não pode ser executada.", "This macro has steps from a newer V-Clicker and cannot be run." },
            ["banner.hover"] = new[] { "\"Passar o mouse\" entra quando o cursor para 1 s num lugar. Apague os que não forem necessários.", "\"Hover\" is added when the cursor rests 1 s somewhere. Delete the ones you do not need." },

            ["mstatus.ready"] = new[] { "Pronto", "Ready" },
            ["mstatus.recCountdown"] = new[] { "Gravando em {0}…", "Recording in {0}…" },
            ["mstatus.recording"] = new[] { "Gravando {0} · {1} para parar", "Recording {0} · {1} to stop" },
            ["mstatus.recPaused"] = new[] { "Gravação pausada · volte para {0}", "Recording paused · go back to {0}" },
            ["mstatus.runCountdown"] = new[] { "Executando em {0}…", "Running in {0}…" },
            ["mstatus.running"] = new[] { "{0} · {1}/{2} · passo {3} de {4}", "{0} · {1}/{2} · step {3} of {4}" },
            ["mstatus.runningForever"] = new[] { "{0} · {1} · passo {2} de {3}", "{0} · {1} · step {2} of {3}" },
            ["mstatus.waitApp"] = new[] { "Pausada · aguardando {0}", "Paused · waiting for {0}" },
            ["mstatus.waitWindow"] = new[] { "Pausada · aguardando a janela \"{0}\"", "Paused · waiting for the \"{0}\" window" },
            ["mstatus.waitMenu"] = new[] { "Pausada · aguardando o menu", "Paused · waiting for the menu" },
            ["mstatus.outside"] = new[] { "Pausada · o ponto está fora da janela de {0}", "Paused · the point is outside the {0} window" },
            ["mstatus.covered"] = new[] { "Pausada · algo está cobrindo {0}", "Paused · something is covering {0}" },
            ["mstatus.capture"] = new[] { "Aponte para o lugar em {0} e aperte {1}", "Point at the spot in {0} and press {1}" },

            ["mstop.Completed"] = new[] { "Macro concluída.", "Macro finished." },
            ["mstop.Manual"] = new[] { "Macro parada.", "Macro stopped." },
            ["mstop.TargetClosed"] = new[] { "A janela de {0} foi fechada e a macro parou.", "The {0} window was closed and the macro stopped." },
            ["mstop.PauseTimeout"] = new[] { "Pausada por mais de 5 minutos: a macro parou.", "Paused for over 5 minutes: the macro stopped." },
            ["mstop.Password"] = new[] { "A macro chegou a um campo de senha que não foi gravado. Digite a senha você mesmo e apague este passo, ou faça o login antes de executar.", "The macro reached a password field that was not recorded. Type the password yourself and delete this step, or log in before running." },
            ["mstop.Blocked"] = new[] { "O Windows recusou a ação e a macro parou (tela bloqueada ou aviso de permissão aberto?).", "Windows refused the action and the macro stopped (locked screen or a permission prompt open?)." },
            ["mstop.Suspended"] = new[] { "O computador foi bloqueado ou suspenso e a macro parou.", "The computer was locked or went to sleep, so the macro stopped." },
            ["mstop.Unsupported"] = new[] { "Esta macro tem passos de uma versão mais nova do V-Clicker e não pode ser executada.", "This macro has steps from a newer V-Clicker and cannot be run." },
            ["mstop.Interrupted"] = new[] { "A janela de {0} saiu da frente no meio de um arrasto: o botão foi solto e a macro parou.", "The {0} window left the front in the middle of a drag: the button was released and the macro stopped." },
            ["mstop.recorded"] = new[] { "Gravação concluída: {0} passos.", "Recording finished: {0} steps." },
            ["mstop.nothing"] = new[] { "Nada foi gravado em {0}.", "Nothing was recorded in {0}." },

            ["ask.replace"] = new[] { "Esta macro já tem passos. O que fazer com a nova gravação?", "This macro already has steps. What should the new recording do?" },
            ["ask.replace.yes"] = new[] { "Substituir", "Replace" },
            ["ask.replace.append"] = new[] { "Acrescentar ao fim", "Add at the end" },
            ["ask.size"] = new[] { "A janela de {0} está com outro tamanho (gravado: {1}, agora: {2}). Os cliques podem cair em outro lugar.", "The {0} window has another size (recorded: {1}, now: {2}). Clicks may land somewhere else." },
            ["ask.size.yes"] = new[] { "Executar mesmo assim", "Run anyway" },
            ["ask.delete"] = new[] { "Excluir a macro \"{0}\"? Ela vai para a Lixeira.", "Delete the macro \"{0}\"? It goes to the Recycle Bin." },
            ["ask.unsaved"] = new[] { "Salvar as alterações em \"{0}\"?", "Save the changes to \"{0}\"?" },
            ["ask.discard"] = new[] { "Descartar", "Discard" },
            ["ask.cancel"] = new[] { "Cancelar", "Cancel" },
            ["ask.ok"] = new[] { "OK", "OK" },
            ["ask.name"] = new[] { "Nome da macro", "Macro name" },

            ["mmsg.nameTaken"] = new[] { "Já existe uma macro com esse nome.", "There is already a macro with that name." },
            ["mmsg.noSteps"] = new[] { "Grave a macro antes de executar.", "Record the macro before running it." },
            ["mmsg.otherApp"] = new[] { "Esta macro é de {0}. Para acrescentar passos, grave no mesmo aplicativo.", "This macro belongs to {0}. To add steps, record in the same app." },
            ["mmsg.openApp"] = new[] { "Abra {0} e tente de novo.", "Open {0} and try again." },
            ["mmsg.front"] = new[] { "O Windows não deixou trazer {0} para a frente. Clique nele e tente de novo.", "Windows would not bring {0} to the front. Click it and try again." },
            ["mmsg.clickerBusy"] = new[] { "Pare o autoclicker antes de usar o Macro Studio.", "Stop the autoclicker before using Macro Studio." },
            ["mmsg.studioBusy"] = new[] { "O Macro Studio está gravando ou executando. Pare-o antes de usar o autoclicker.", "Macro Studio is recording or running. Stop it before using the autoclicker." },
            ["mmsg.hooks"] = new[] { "O Windows não permitiu gravar: {0}", "Windows did not allow recording: {0}" },
            ["mmsg.saveFailed"] = new[] { "Não foi possível salvar a macro: {0}", "Could not save the macro: {0}" },
            ["mmsg.openFailed"] = new[] { "Não foi possível abrir {0}.", "Could not open {0}." },
            ["mmsg.captureOutside"] = new[] { "Esse ponto não é da janela de {0}.", "That spot is not in the {0} window." },
            ["mmsg.captured"] = new[] { "Ponto capturado: {0}, {1}.", "Point captured: {0}, {1}." },
            ["mmsg.hotkeyStep"] = new[] { "O passo {0} aperta {1}, que é um atalho do V-Clicker. Troque o atalho ou apague o passo.", "Step {0} presses {1}, which is a V-Clicker hotkey. Change the hotkey or delete the step." },
            ["mmsg.saved"] = new[] { "Macro salva.", "Macro saved." },
            ["picker.record"] = new[] { "Em qual aplicativo você vai gravar a tarefa?", "Which app will you record the task in?" },

            ["opt.title"] = new[] { "Otimizar pausas", "Optimize pauses" },
            ["opt.intro"] = new[] { "Marque só as esperas que podem ser encurtadas. As outras ficam como foram gravadas, e qualquer uma pode ser restaurada depois.", "Tick only the waits that can be shortened. The others stay as recorded, and any of them can be restored later." },
            ["opt.row"] = new[] { "Passo {0} · Esperar {1} s", "Step {0} · Wait {1} s" },
            ["opt.apply"] = new[] { "Aplicar", "Apply" },
        };
    }
}
