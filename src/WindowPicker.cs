using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace VClicker
{
    /// <summary>Small dark dialog listing app windows (icon, app name, window title) to pick one for Window Lock.</summary>
    internal sealed class WindowPicker : Form
    {
        private readonly ListBox list = new ListBox();
        private readonly Dictionary<string, Icon> icons = new Dictionary<string, Icon>(StringComparer.OrdinalIgnoreCase);

        private WindowPicker(string prompt, List<AppWindow> windows)
        {
            Text = Loc.T("picker.title");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.Ui(9.5f);
            Padding = Theme.Pad(16);
            ClientSize = Theme.Sz(460, 380);

            var label = new Label
            {
                Text = prompt, Dock = DockStyle.Top, AutoSize = false, Height = Theme.Dp(40), ForeColor = Theme.Dim,
                BackColor = Theme.Bg, UseMnemonic = false
            };

            list.Dock = DockStyle.Fill;
            list.BorderStyle = BorderStyle.None;
            list.BackColor = Theme.Field;
            list.ForeColor = Theme.Text;
            list.DrawMode = DrawMode.OwnerDrawFixed;
            list.ItemHeight = Theme.Dp(40);
            list.IntegralHeight = false;
            list.FormattingEnabled = true; // the Format text is what screen readers and type-ahead get
            list.AccessibleName = "App windows";
            foreach (AppWindow window in windows) list.Items.Add(window);
            if (list.Items.Count > 0) list.SelectedIndex = 0;
            list.DrawItem += DrawWindow;
            list.DoubleClick += delegate { if (list.SelectedItem != null) Accept(); };
            list.Format += (sender, e) => e.Value = ((AppWindow)e.ListItem).DisplayName + " - " + ((AppWindow)e.ListItem).Title;

            var ok = new FlatButton { Kind = ButtonKind.Primary, Size = Theme.Sz(120, 34), Margin = Theme.Pad(8, 0, 0, 0) };
            ok.Text = Loc.T("picker.ok");
            ok.Enabled = list.Items.Count > 0;
            ok.Click += delegate { Accept(); };
            var cancel = new FlatButton { Kind = ButtonKind.Secondary, Size = Theme.Sz(120, 34), Margin = Padding.Empty };
            cancel.Text = Loc.T("picker.cancel");
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; };

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = Theme.Dp(50),
                Padding = Theme.Pad(0, 12, 0, 0), BackColor = Theme.Bg
            };
            buttons.Controls.Add(ok);
            buttons.Controls.Add(cancel);

            Controls.Add(list);
            Controls.Add(label);
            Controls.Add(buttons);
            if (list.Items.Count == 0)
            {
                label.Text = prompt + Environment.NewLine + Loc.T("picker.empty");
                label.Height = Theme.Dp(60);
            }
            KeyPreview = true;
            KeyDown += (sender, e) =>
            {
                if (e.KeyCode == Keys.Escape) DialogResult = DialogResult.Cancel;
                // Only from the list: Enter on the Cancel button must cancel.
                else if (e.KeyCode == Keys.Enter && ActiveControl == list && list.SelectedItem != null) Accept();
            };
        }

        public AppWindow Selected { get; private set; }

        /// <summary>Shows the dialog; null if cancelled or nothing was there to pick.</summary>
        public static AppWindow Pick(IWin32Window owner, string prompt, List<AppWindow> windows)
        {
            using (var picker = new WindowPicker(prompt, windows))
            {
                // Started from the tray, the main window is hidden: center on the screen and stay in front instead.
                if (owner is Form form && form.Visible && form.WindowState != FormWindowState.Minimized)
                    return picker.ShowDialog(owner) == DialogResult.OK ? picker.Selected : null;
                picker.StartPosition = FormStartPosition.CenterScreen;
                picker.TopMost = true;
                return picker.ShowDialog() == DialogResult.OK ? picker.Selected : null;
            }
        }

        private void Accept()
        {
            Selected = list.SelectedItem as AppWindow;
            if (Selected != null) DialogResult = DialogResult.OK;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.StyleTitleBar(Handle, MainForm.ColorRef(Theme.Bg), MainForm.ColorRef(Theme.Text), MainForm.ColorRef(Theme.Border));
        }

        private void DrawWindow(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            var window = (AppWindow)list.Items[e.Index];
            bool selected = (e.State & DrawItemState.Selected) != 0;
            using (var back = new SolidBrush(selected ? Theme.Mix(Theme.Field, Theme.Accent, 0.35f) : Theme.Field))
                e.Graphics.FillRectangle(back, e.Bounds);

            int pad = Theme.Dp(10), size = Theme.Dp(20);
            Icon icon = IconFor(window.ExePath);
            if (icon != null)
                e.Graphics.DrawIcon(icon, new Rectangle(e.Bounds.Left + pad, e.Bounds.Top + (e.Bounds.Height - size) / 2, size, size));

            int left = e.Bounds.Left + pad * 2 + size;
            var nameArea = new Rectangle(left, e.Bounds.Top + Theme.Dp(3), e.Bounds.Right - left - pad, e.Bounds.Height / 2);
            var titleArea = new Rectangle(left, e.Bounds.Top + e.Bounds.Height / 2, e.Bounds.Right - left - pad, e.Bounds.Height / 2 - Theme.Dp(3));
            const TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix |
                                          TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter;
            TextRenderer.DrawText(e.Graphics, window.DisplayName + "  (" + window.ExeName + ")", Theme.Ui(9.5f, true), nameArea, Theme.Text, flags);
            // Several windows of one game look alike; the process id tells them apart.
            TextRenderer.DrawText(e.Graphics, window.Title + "  \u00b7  PID " + window.ProcessId, Theme.Ui(8.5f), titleArea, Theme.Dim, flags);
        }

        private Icon IconFor(string path)
        {
            if (icons.TryGetValue(path, out Icon icon)) return icon;
            try { icon = Icon.ExtractAssociatedIcon(path); }
            catch (ArgumentException) { icon = null; }
            catch (System.IO.IOException) { icon = null; }
            catch (UnauthorizedAccessException) { icon = null; }
            icons[path] = icon;
            return icon;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                foreach (Icon icon in icons.Values) icon?.Dispose();
            base.Dispose(disposing);
        }
    }
}
