using System;
using System.Drawing;
using System.Windows.Forms;

namespace VClicker
{
    /// <summary>Small dark question and text dialogs (the stock MessageBox ignores the theme).</summary>
    internal static class Dialogs
    {
        internal static void Style(Form form)
        {
            form.FormBorderStyle = FormBorderStyle.FixedDialog;
            form.MaximizeBox = form.MinimizeBox = false;
            form.ShowInTaskbar = false;
            form.StartPosition = FormStartPosition.CenterParent;
            form.AutoScaleMode = AutoScaleMode.None;
            form.AutoSize = true;
            form.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            form.BackColor = Theme.Bg;
            form.ForeColor = Theme.Text;
            form.Font = Theme.Ui(9.5f);
            form.Padding = Theme.Pad(18);
            form.KeyPreview = true;
            form.HandleCreated += delegate
            {
                Native.StyleTitleBar(form.Handle);
            };
        }

        /// <returns>The index of the button pressed; -1 if the dialog was closed (or Esc).</returns>
        public static int Ask(IWin32Window owner, string text, params string[] buttons) => Ask(owner, text, ButtonKind.Primary, buttons);

        /// <param name="first">The first button's key: the coral one when it destroys something.</param>
        public static int Ask(IWin32Window owner, string text, ButtonKind first, params string[] buttons)
        {
            using (var form = new Form { Text = "V-Clicker" })
            {
                Style(form);
                int answer = -1;
                var body = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Dock = DockStyle.Fill, BackColor = Theme.Bg };
                body.Controls.Add(new Label
                {
                    Text = text, AutoSize = true, MaximumSize = new Size(Theme.Dp(440), 0), BackColor = Theme.Bg, ForeColor = Theme.Text,
                    UseMnemonic = false, Margin = Theme.Pad(0, 0, 0, 16)
                });
                var row = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, BackColor = Theme.Bg };
                for (int i = 0; i < buttons.Length; i++)
                {
                    int index = i;
                    var button = new KeyButton
                    {
                        Text = buttons[i], Kind = i == 0 ? first : ButtonKind.Secondary,
                        Size = new Size(Math.Max(Theme.Dp(100), TextRenderer.MeasureText(buttons[i], Theme.Ui(9.5f, true)).Width + Theme.Dp(28)), Theme.Dp(32)),
                        Margin = Theme.Pad(8, 0, 0, 0)
                    };
                    button.Click += delegate { answer = index; form.Close(); };
                    row.Controls.Add(button);
                }
                body.Controls.Add(row);
                form.Controls.Add(body);
                form.KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) form.Close(); };
                form.ShowDialog(owner);
                return answer;
            }
        }

        /// <returns>The text typed; null if cancelled.</returns>
        public static string Prompt(IWin32Window owner, string label, string initial)
        {
            using (var form = new Form { Text = "V-Clicker" })
            {
                Style(form);
                string result = null;
                var body = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Dock = DockStyle.Fill, BackColor = Theme.Bg };
                body.Controls.Add(new Label { Text = label, AutoSize = true, BackColor = Theme.Bg, ForeColor = Theme.Dim, UseMnemonic = false });
                var field = new TextField { Width = Theme.Dp(360), Margin = Theme.Pad(0, 8, 0, 18) };
                TextBox box = field.Box;
                box.Text = initial ?? string.Empty;
                box.MaxLength = 80;
                box.AccessibleName = label;
                body.Controls.Add(field);
                var row = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, BackColor = Theme.Bg };
                var ok = new KeyButton { Text = Loc.T("ask.ok"), Kind = ButtonKind.Primary, Size = Theme.Sz(100, 36), Margin = Theme.Pad(8, 0, 0, 0) };
                var cancel = new KeyButton { Text = Loc.T("ask.cancel"), Kind = ButtonKind.Secondary, Size = Theme.Sz(100, 36), Margin = Theme.Pad(8, 0, 0, 0) };
                ok.Click += delegate { result = box.Text; form.Close(); };
                cancel.Click += delegate { form.Close(); };
                row.Controls.Add(ok);
                row.Controls.Add(cancel);
                body.Controls.Add(row);
                form.Controls.Add(body);
                form.KeyDown += (s, e) =>
                {
                    if (e.KeyCode == Keys.Escape) form.Close();
                    if (e.KeyCode == Keys.Enter) { result = box.Text; form.Close(); e.SuppressKeyPress = true; }
                };
                form.Shown += delegate { box.SelectAll(); box.Focus(); };
                form.ShowDialog(owner);
                return result;
            }
        }
    }
}
