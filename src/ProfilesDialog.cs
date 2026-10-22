using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace VClicker
{
    /// <summary>
    /// The saved per-app profiles: export one (with the app's macros), import one, delete one. The profiles themselves
    /// are made and loaded by the Window Lock (docs/superpowers/specs/2026-10-21-profiles-design.md).
    /// </summary>
    internal sealed class ProfilesDialog : Form
    {
        private readonly string folder, macroFolder;
        private readonly MainForm host; // null in tests
        private readonly ListBox list;
        private readonly Label empty, result;
        private readonly KeyButton export, delete;
        private List<AppProfile> profiles = new List<AppProfile>();

        /// <summary>Answers a yes/no question instead of the dialog (tests).</summary>
        internal Func<string, bool> Confirm;

        public ProfilesDialog(string folder, string macroFolder, MainForm host)
        {
            this.folder = folder;
            this.macroFolder = macroFolder;
            this.host = host;
            Text = Loc.T("prof.title");
            Dialogs.Style(this);

            var body = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Dock = DockStyle.Fill, BackColor = Theme.Bg };
            body.Controls.Add(new Label
            {
                Text = Loc.T("prof.intro"), AutoSize = true, MaximumSize = new Size(Theme.Dp(480), 0), ForeColor = Theme.Dim,
                BackColor = Theme.Bg, UseMnemonic = false, Margin = Theme.Pad(0, 0, 0, 12)
            });

            var well = new WellPanel { Size = Theme.Sz(480, 220), Padding = Theme.Pad(6), Margin = Theme.Pad(0, 0, 0, 10) };
            list = new ListBox
            {
                Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, BackColor = Theme.Field, ForeColor = Theme.Text,
                Font = Theme.Ui(10f), IntegralHeight = false, AccessibleName = "Profiles",
                DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = Theme.Dp(32)
            };
            list.DrawItem += DrawProfile;
            list.SelectedIndexChanged += delegate { UpdateEnabled(); };
            empty = new Label
            {
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Theme.Dim, BackColor = Theme.Field,
                UseMnemonic = false, Padding = Theme.Pad(24), Text = Loc.T("prof.empty")
            };
            well.Controls.Add(list);
            well.Controls.Add(empty);
            body.Controls.Add(well);

            result = new Label
            {
                AutoSize = true, MaximumSize = new Size(Theme.Dp(480), 0), ForeColor = Theme.Dim, BackColor = Theme.Bg,
                UseMnemonic = false, Margin = Theme.Pad(0, 0, 0, 10)
            };
            body.Controls.Add(result);

            var row = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, BackColor = Theme.Bg };
            KeyButton close = Button("prof.close", ButtonKind.Primary, delegate { Close(); });
            delete = Button("prof.delete", ButtonKind.Secondary, delegate { DeleteSelected(); });
            export = Button("prof.export", ButtonKind.Secondary, delegate { ExportSelected(); });
            KeyButton import = Button("prof.import", ButtonKind.Secondary, delegate { Import(); });
            row.Controls.AddRange(new Control[] { close, delete, export, import });
            body.Controls.Add(row);
            Controls.Add(body);
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };

            Reload();
        }

        private static KeyButton Button(string key, ButtonKind kind, EventHandler click)
        {
            string text = Loc.T(key);
            var button = new KeyButton
            {
                Text = text, Kind = kind, Margin = Theme.Pad(8, 0, 0, 0),
                Size = new Size(Math.Max(Theme.Dp(100), TextRenderer.MeasureText(text, Theme.Ui(9.5f, true)).Width + Theme.Dp(28)), Theme.Dp(36))
            };
            button.Click += click;
            return button;
        }

        /// <summary>Like the Studio's step list: the selected line sits on a key-colored bar.</summary>
        private void DrawProfile(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            float k = Theme.Scale;
            using (var back = new SolidBrush(Theme.Field)) g.FillRectangle(back, e.Bounds);
            if ((e.State & DrawItemState.Selected) != 0)
            {
                var bar = new RectangleF(e.Bounds.Left + k, e.Bounds.Top + k, e.Bounds.Width - 2 * k, e.Bounds.Height - 2 * k);
                using (System.Drawing.Drawing2D.GraphicsPath path = Theme.RoundRect(bar, 5 * k))
                using (var fill = new SolidBrush(Theme.Key))
                    g.FillPath(fill, path);
                if (list.Focused) Theme.DrawFocus(g, bar, 5 * k);
            }
            var text = new Rectangle(e.Bounds.Left + Theme.Dp(10), e.Bounds.Top, e.Bounds.Width - Theme.Dp(20), e.Bounds.Height);
            TextRenderer.DrawText(g, (string)list.Items[e.Index], list.Font, text, Theme.Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }

        /// <summary>What the list shows, one line per profile.</summary>
        internal List<string> Items => list.Items.Cast<string>().ToList();

        private void Reload(string select = null)
        {
            profiles = Profiles.List(folder);
            list.BeginUpdate();
            list.Items.Clear();
            foreach (AppProfile profile in profiles)
            {
                int macros = Profiles.MacrosOf(macroFolder, profile.ExeName).Count;
                string count = macros == 0 ? string.Empty : " · " + (macros == 1 ? Loc.T("prof.macros1") : Loc.F("prof.macrosN", macros));
                list.Items.Add(profile.Name + " — " + Profiles.Summary(profile.Settings) + count);
            }
            list.EndUpdate();
            int index = select == null ? 0 : Math.Max(0, profiles.FindIndex(p => p.ExeName.Equals(select, StringComparison.OrdinalIgnoreCase)));
            if (profiles.Count > 0) list.SelectedIndex = index;
            empty.Visible = profiles.Count == 0;
            list.Visible = profiles.Count > 0;
            UpdateEnabled();
        }

        private AppProfile SelectedProfile => list.SelectedIndex >= 0 && list.SelectedIndex < profiles.Count ? profiles[list.SelectedIndex] : null;

        private void UpdateEnabled() => export.Enabled = delete.Enabled = SelectedProfile != null;

        private bool Ask(string text, string yes, ButtonKind kind) =>
            Confirm != null ? Confirm(text) : Dialogs.Ask(this, text, kind, yes, Loc.T("ask.cancel")) == 0;

        private void Say(string text, Color color)
        {
            result.Text = text;
            result.ForeColor = color;
        }

        /// <summary>Deletes the selected profile (the app's macros stay).</summary>
        internal void DeleteSelected()
        {
            AppProfile profile = SelectedProfile;
            if (profile == null || !Ask(Loc.F("prof.ask.delete", profile.Name), Loc.T("prof.delete"), ButtonKind.Danger)) return;
            if (!Profiles.Delete(folder, profile.ExeName)) Say(Loc.F("prof.ioFailed", profile.ExeName + Profiles.Extension), Theme.Danger);
            Reload();
        }

        private void ExportSelected()
        {
            AppProfile profile = SelectedProfile;
            if (profile == null) return;
            using (var save = new SaveFileDialog
            {
                Filter = Loc.T("prof.filter"), DefaultExt = Profiles.Extension, AddExtension = true, OverwritePrompt = true,
                FileName = MacroFile.SafeFileName(profile.Name) + Profiles.Extension
            })
            {
                if (save.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    Profiles.Export(profile, macroFolder, save.FileName);
                    Say(Loc.F("prof.exported", profile.Name), Theme.Success);
                }
                catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
                {
                    Say(Loc.F("prof.ioFailed", error.Message), Theme.Danger);
                }
            }
        }

        private void Import()
        {
            string file;
            using (var open = new OpenFileDialog { Filter = Loc.T("prof.filter"), CheckFileExists = true })
            {
                if (open.ShowDialog(this) != DialogResult.OK) return;
                file = open.FileName;
            }
            AppProfile profile;
            List<Macro> macros;
            int skipped;
            try
            {
                profile = Profiles.ReadFile(file, out macros, out skipped);
            }
            catch (FormatException)
            {
                Say(Loc.T("prof.importFailed"), Theme.Danger);
                return;
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                Say(Loc.F("prof.ioFailed", error.Message), Theme.Danger);
                return;
            }
            if (Profiles.Load(folder, profile.ExeName) != null &&
                !Ask(Loc.F("prof.ask.replace", profile.Name), Loc.T("prof.replace"), ButtonKind.Primary)) return;
            profile.UsedUtc = DateTime.UtcNow;
            int added;
            try
            {
                if (!Profiles.Save(folder, profile)) throw new IOException(profile.ExeName + Profiles.Extension);
                added = Profiles.AddMacros(macroFolder, macros);
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                Say(Loc.F("prof.ioFailed", error.Message), Theme.Danger);
                return;
            }
            Say(Loc.F("prof.imported", profile.Name, added) + (skipped > 0 ? Loc.F("prof.skipped", skipped) : string.Empty),
                skipped > 0 ? Theme.Warning : Theme.Success);
            host?.ProfileImported(profile);
            Reload(profile.ExeName);
        }
    }
}
