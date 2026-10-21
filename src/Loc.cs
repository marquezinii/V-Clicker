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
            ["stat.note"] = new[] { "CPS enviados ao Windows; o jogo pode reconhecer menos.", "CPS sent to Windows; the game may register fewer." },

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
            ["lock.enable"] = new[] { "Clicar só em um aplicativo", "Only click inside one app" },
            ["lock.tip"] = new[] { "Os cliques só acontecem com o aplicativo escolhido em primeiro plano; num Alt+Tab, o V-Clicker pausa.", "Clicks only happen while the chosen app is in front; on Alt+Tab, V-Clicker pauses." },
            ["lock.none"] = new[] { "Nenhum escolhido", "None chosen" },
            ["lock.choose"] = new[] { "Escolher…", "Choose…" },
            ["lock.onFocusLoss"] = new[] { "Ao perder o foco", "On focus loss" },
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
            ["msg.lockOutside"] = new[] { "A posição fixa está fora da janela de {0}.", "The fixed spot is outside the {0} window." },
            ["msg.focusLost"] = new[] { "{0} perdeu o foco e o V-Clicker parou.", "{0} lost focus and V-Clicker stopped." },
            ["msg.targetClosed"] = new[] { "A janela de {0} foi fechada e o V-Clicker parou.", "The {0} window was closed and V-Clicker stopped." },
            ["msg.pauseTimeout"] = new[] { "Pausado por mais de 5 minutos: o V-Clicker parou.", "Paused for over 5 minutes: V-Clicker stopped." },
            ["msg.suspended"] = new[] { "O computador foi bloqueado ou suspenso e o V-Clicker parou.", "The computer was locked or went to sleep, so V-Clicker stopped." },
            ["msg.adminFailed"] = new[] { "Não foi possível reiniciar como administrador.", "Could not restart as administrator." },
            ["msg.crash"] = new[] { "Ocorreu um erro inesperado:\n\n{0}", "An unexpected error occurred:\n\n{0}" },
        };
    }
}
