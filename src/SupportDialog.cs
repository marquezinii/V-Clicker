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
        private readonly Image qr;

        /// <summary>The QR code shown (tests check it is there).</summary>
        internal Image Qr => qr;

        private static Image LoadQr()
        {
            using (System.IO.Stream stream = typeof(SupportDialog).Assembly.GetManifestResourceStream("pix-qr.png"))
            using (var decoded = new Bitmap(stream))
                return new Bitmap(decoded); // GDI+ needs the stream open for the life of a stream-backed Bitmap
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) qr?.Dispose();
            base.Dispose(disposing);
        }

        /// <summary>The QR on a white card, scaled without smoothing so every module stays sharp for the camera.</summary>
        private sealed class QrBox : Control
        {
            private readonly Image image;

            public QrBox(Image image)
            {
                this.image = image;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                AccessibleRole = AccessibleRole.Graphic;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.Clear(Color.White);
                e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                e.Graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                int pad = Theme.Dp(8);
                e.Graphics.DrawImage(image, new Rectangle(pad, pad, Width - 2 * pad, Height - 2 * pad));
            }
        }

        public SupportDialog()
        {
            Text = Loc.T("support.title");
            Dialogs.Style(this);
            int width = Theme.Dp(420);

            var body = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Dock = DockStyle.Fill, BackColor = Theme.Bg };
            body.Controls.Add(Dialogs.Paragraph(Loc.T("support.intro"), Theme.Text, width, 16));

            body.Controls.Add(Dialogs.Paragraph(Loc.T("support.kofi"), Theme.Dim, width, 6));
            var kofi = new KeyButton { Text = "Ko-fi", Kind = ButtonKind.Primary, Size = new Size(width, Theme.Dp(40)), Margin = Theme.Pad(0, 0, 0, 16) };
            kofi.AccessibleName = "Ko-fi";
            kofi.Click += delegate { Open(KofiUrl); };
            body.Controls.Add(kofi);

            body.Controls.Add(Dialogs.Paragraph(Loc.T("support.pix"), Theme.Dim, width, 8));
            qr = LoadQr();
            var code = new QrBox(qr) { Size = Theme.Sz(176, 176), Anchor = AnchorStyles.None, Margin = Theme.Pad(0, 0, 0, 10) };
            code.AccessibleName = Loc.T("support.qr");
            body.Controls.Add(code);
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

            status = Dialogs.Paragraph(string.Empty, Theme.Success, width, 12);
            body.Controls.Add(status);

            var close = new KeyButton { Text = Loc.T("prof.close"), Kind = ButtonKind.Secondary, Size = Theme.Sz(110, 36), Anchor = AnchorStyles.Right };
            close.Click += delegate { Close(); };
            body.Controls.Add(close);
            Controls.Add(body);
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };
        }

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
