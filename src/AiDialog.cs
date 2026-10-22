using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace VClicker
{
    /// <summary>
    /// "Build with AI": what is sent, the API key (first use), the description, Generate. Connects only when Generate is
    /// pressed; the result is a proposal the Studio opens unsaved (docs/superpowers/specs/2026-10-21-ai-macro-builder-design.md).
    /// </summary>
    internal sealed class AiDialog : Form
    {
        private readonly ClickSettings settings;
        private readonly Action saveSettings;
        private readonly string appName;
        private readonly Macro bound;
        private readonly TextField keyField, description;
        private readonly Label keySaved, status;
        private readonly LinkLabel forget;
        private readonly KeyButton generate;
        private CancellationTokenSource working;

        /// <summary>Makes the proposal instead of the API (tests).</summary>
        internal Func<string, string, CancellationToken, Task<AiProposal>> Generator;

        public AiProposal Result { get; private set; }

        public AiDialog(ClickSettings settings, Action saveSettings, string appName, Macro bound)
        {
            this.settings = settings;
            this.saveSettings = saveSettings;
            this.appName = appName;
            this.bound = bound;
            Text = Loc.T("ai.title");
            Dialogs.Style(this);
            int width = Theme.Dp(500);

            var body = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Dock = DockStyle.Fill, BackColor = Theme.Bg };
            body.Controls.Add(Text_(Loc.F("ai.privacy", appName), Theme.Dim, width, 12));

            body.Controls.Add(Text_(Loc.T("ai.describe"), Theme.Text, width, 6));
            description = new TextField { Size = new Size(width, Theme.Dp(104)), Margin = Theme.Pad(0, 0, 0, 4) };
            description.Box.Multiline = true;
            description.Box.AcceptsReturn = true;
            description.Box.Height = Theme.Dp(88);
            description.Box.MaxLength = 2000;
            description.Box.AccessibleName = Loc.T("ai.describe");
            body.Controls.Add(description);
            body.Controls.Add(Text_(Loc.T("ai.example"), Theme.Dim, width, 12));

            keySaved = Text_(Loc.T("ai.keySaved"), Theme.Dim, width, 2);
            forget = new LinkLabel
            {
                Text = Loc.T("ai.forget"), AutoSize = true, BackColor = Theme.Bg, LinkColor = Theme.Text, ActiveLinkColor = Theme.Cap,
                VisitedLinkColor = Theme.Text, LinkBehavior = LinkBehavior.HoverUnderline, Margin = Theme.Pad(0, 0, 0, 12)
            };
            forget.LinkClicked += delegate
            {
                settings.AiKey = string.Empty;
                saveSettings?.Invoke();
                ShowKey();
            };
            var keyLabel = Text_(Loc.T("ai.key"), Theme.Text, width, 6);
            keyField = new TextField { Size = new Size(width, Theme.Dp(34)), Margin = Theme.Pad(0, 0, 0, 12) };
            keyField.Box.UseSystemPasswordChar = true;
            keyField.Box.AccessibleName = Loc.T("ai.key");
            keyField.Tag = keyLabel;
            body.Controls.Add(keySaved);
            body.Controls.Add(forget);
            body.Controls.Add(keyLabel);
            body.Controls.Add(keyField);

            status = Text_(string.Empty, Theme.Dim, width, 10);
            body.Controls.Add(status);

            var row = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, BackColor = Theme.Bg };
            generate = new KeyButton { Text = Loc.T("ai.generate"), Kind = ButtonKind.Primary, Size = Theme.Sz(120, 36), Margin = Theme.Pad(8, 0, 0, 0) };
            var cancel = new KeyButton { Text = Loc.T("ask.cancel"), Kind = ButtonKind.Secondary, Size = Theme.Sz(120, 36), Margin = Theme.Pad(8, 0, 0, 0) };
            generate.Click += async delegate { await GenerateAsync(); };
            cancel.Click += delegate { Close(); };
            row.Controls.Add(generate);
            row.Controls.Add(cancel);
            body.Controls.Add(row);
            Controls.Add(body);
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };
            Shown += delegate { description.Box.Focus(); };
            ShowKey();
        }

        private static Label Text_(string text, Color color, int width, int below) => new Label
        {
            Text = text, AutoSize = true, MaximumSize = new Size(width, 0), ForeColor = color, BackColor = Theme.Bg,
            UseMnemonic = false, Margin = Theme.Pad(0, 0, 0, below)
        };

        /// <summary>A saved key is never shown: only "saved" and Forget. Without one, the field to paste it.</summary>
        private void ShowKey()
        {
            bool saved = ApiKeys.Unprotect(settings.AiKey) != null;
            keySaved.Visible = forget.Visible = saved;
            keyField.Visible = ((Control)keyField.Tag).Visible = !saved;
        }

        /// <summary>The description as typed (tests set it).</summary>
        internal string Description { get => description.Box.Text; set => description.Box.Text = value; }

        internal string StatusText => status.Text;

        /// <summary>Generate: the proposal, or the reason there is none. The window closes with OK on success.</summary>
        internal async Task GenerateAsync()
        {
            if (working != null) return;
            string text = description.Box.Text.Trim();
            if (text.Length == 0)
            {
                Say(Loc.T("ai.needText"), Theme.Warning);
                return;
            }
            string key = ApiKeys.Unprotect(settings.AiKey) ?? keyField.Box.Text.Trim();
            if (key.Length == 0 && Generator == null)
            {
                Say(Loc.T("ai.needKey"), Theme.Warning);
                return;
            }
            if (keyField.Visible && key.Length > 0)
            {
                settings.AiKey = ApiKeys.Protect(key); // kept only once it is used; Forget removes it
                saveSettings?.Invoke();
            }
            working = new CancellationTokenSource();
            generate.Enabled = description.Enabled = keyField.Enabled = false;
            Say(Loc.T("ai.working"), Theme.Dim);
            try
            {
                Result = Generator != null
                    ? await Generator(text, appName, working.Token)
                    : await MacroAi.Generate(key, text, appName, bound, working.Token);
                DialogResult = DialogResult.OK;
            }
            catch (AiException error)
            {
                Say(error.Message, Theme.Warning);
            }
            catch (OperationCanceledException) { } // closed while waiting
            finally
            {
                working.Dispose();
                working = null;
                if (!IsDisposed)
                {
                    generate.Enabled = description.Enabled = keyField.Enabled = true;
                    ShowKey();
                }
            }
        }

        private void Say(string text, Color color)
        {
            status.Text = text;
            status.ForeColor = color;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            working?.Cancel();
            base.OnFormClosing(e);
        }
    }
}
