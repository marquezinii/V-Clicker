using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace VClicker
{
    /// <summary>"Support ♥" in the footer: Ko-fi for cards and PayPal, the Pix key for Brazil. Opened only when asked.</summary>
    internal sealed class SupportDialog : Form
    {
        public const string KofiUrl = "https://ko-fi.com/vemryx";
        public const string PixKey = "f1f20b16-c964-4465-9f45-2cb2a7647726";

        private readonly Label status;

        public SupportDialog()
        {
            Text = Loc.T("support.title");
            Dialogs.Style(this);
            int width = Theme.Dp(420);

            var body = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Dock = DockStyle.Fill, BackColor = Theme.Bg };
            body.Controls.Add(Line(Loc.T("support.intro"), Theme.Text, width, 16));

            body.Controls.Add(Line(Loc.T("support.kofi"), Theme.Dim, width, 6));
            var kofi = new KeyButton { Text = "Ko-fi", Kind = ButtonKind.Primary, Size = new Size(width, Theme.Dp(40)), Margin = Theme.Pad(0, 0, 0, 16) };
            kofi.AccessibleName = "Ko-fi";
            kofi.Click += delegate { Open(KofiUrl); };
            body.Controls.Add(kofi);

            body.Controls.Add(Line(Loc.T("support.pix"), Theme.Dim, width, 6));
            var row = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, BackColor = Theme.Bg, Margin = Theme.Pad(0, 0, 0, 8) };
            var key = new TextField { Size = new Size(width - Theme.Dp(128), Theme.Dp(36)), Margin = Theme.Pad(0, 0, 8, 0) };
            key.Box.Text = PixKey;
            key.Box.ReadOnly = true;
            key.Box.AccessibleName = Loc.T("support.pix");
            var copy = new KeyButton { Text = Loc.T("support.copy"), Kind = ButtonKind.Secondary, Size = Theme.Sz(120, 36), Margin = Padding.Empty };
            copy.Click += delegate { CopyPix(); };
            row.Controls.Add(key, 0, 0);
            row.Controls.Add(copy, 1, 0);
            body.Controls.Add(row);

            status = Line(string.Empty, Theme.Success, width, 12);
            body.Controls.Add(status);

            var close = new KeyButton { Text = Loc.T("prof.close"), Kind = ButtonKind.Secondary, Size = Theme.Sz(110, 36), Anchor = AnchorStyles.Right };
            close.Click += delegate { Close(); };
            body.Controls.Add(close);
            Controls.Add(body);
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };
        }

        private static Label Line(string text, Color color, int width, int below) => new Label
        {
            Text = text, AutoSize = true, MaximumSize = new Size(width, 0), ForeColor = color, BackColor = Theme.Bg,
            UseMnemonic = false, Margin = Theme.Pad(0, 0, 0, below)
        };

        internal string StatusText => status.Text;

        internal void CopyPix()
        {
            try
            {
                Clipboard.SetText(PixKey);
                status.Text = Loc.T("support.copied");
            }
            catch (ExternalException)
            {
                status.Text = PixKey; // the clipboard is busy: the key stays selectable above
            }
        }

        private void Open(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Win32Exception) { status.Text = url; } // no default browser: show the address to copy
        }
    }
}
