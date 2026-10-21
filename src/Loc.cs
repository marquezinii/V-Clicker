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
            ["stat.clicks"] = new[] { "CLIQUES", "CLICKS" },
            ["stat.cps"] = new[] { "CLIQUES/S", "CLICKS/S" },
            ["stat.time"] = new[] { "TEMPO", "TIME" },

            // interval
            ["card.interval"] = new[] { "INTERVALO ENTRE CLIQUES", "CLICK INTERVAL" },
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

            // options / footer
            ["opt.top"] = new[] { "Sempre no topo", "Always on top" },
            ["opt.tray"] = new[] { "Minimizar para a bandeja", "Minimize to tray" },
            ["opt.sounds"] = new[] { "Avisos sonoros", "Sound cues" },
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
            ["msg.captured"] = new[] { "Posição capturada: X {0}, Y {1}", "Position captured: X {0}, Y {1}" },
            ["msg.done"] = new[] { "Concluído: {0} cliques.", "Finished: {0} clicks." },
            ["msg.adminFailed"] = new[] { "Não foi possível reiniciar como administrador.", "Could not restart as administrator." },
            ["msg.crash"] = new[] { "Ocorreu um erro inesperado:\n\n{0}", "An unexpected error occurred:\n\n{0}" },
        };
    }
}
