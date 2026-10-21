using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

internal static class AutoClickerTests
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void Pump(int milliseconds, Form target, Button button)
    {
        Stopwatch watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < milliseconds)
        {
            Check(GetForegroundWindow() == target.Handle &&
                button.RectangleToScreen(button.ClientRectangle).Contains(Cursor.Position),
                "Teste interrompido: foco=" + GetForegroundWindow() + ", alvo=" + target.Handle +
                ", cursor=" + Cursor.Position + ", área=" + button.RectangleToScreen(button.ClientRectangle));
            Application.DoEvents();
            Thread.Sleep(5);
        }
    }

    [STAThread]
    private static int Main()
    {
        Point originalCursor = Cursor.Position;
        AutoClickerForm app = null;
        Form target = null;
        try
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Check(Marshal.SizeOf(typeof(Native.Input)) == 40, "Layout INPUT x64 incorreto.");
            Native.Input[] click = Native.CreateClick();
            Check(click.Length == 2 && click[0].mouse.flags == 2 && click[1].mouse.flags == 4,
                "O clique deve pressionar e soltar o botão esquerdo.");

            app = new AutoClickerForm();
            app.StartPosition = FormStartPosition.Manual;
            Rectangle screen = Screen.PrimaryScreen.WorkingArea;
            app.Location = new Point(screen.Left + 30, screen.Top + 30);
            app.Show();
            Thread.Sleep(400);
            Application.DoEvents();
            using (Bitmap picture = new Bitmap(screen.Width, screen.Height))
            {
                using (Graphics graphics = Graphics.FromImage(picture))
                    graphics.CopyFromScreen(screen.Location, Point.Empty, screen.Size);
                picture.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test-window.png"));
            }

            NumericUpDown interval = (NumericUpDown)app.Controls["Interval"];
            Check(interval.Minimum == 20 && interval.Maximum == 60000 && interval.Value == 100,
                "Limites ou intervalo padrão incorretos.");
            Check(!Native.RegisterHotKey(IntPtr.Zero, 50, 0x4000, (uint)Keys.F6),
                "F6 não foi registrado globalmente.");
            Check(!Native.RegisterHotKey(IntPtr.Zero, 51, 0x4000, (uint)Keys.F8),
                "F8 não foi registrado globalmente.");

            int clicks = 0;
            target = new Form { Text = "Teste de cliques", Size = new Size(350, 260),
                StartPosition = FormStartPosition.Manual, TopMost = true,
                Location = new Point(screen.Left + 450, screen.Top + 30) };
            Button button = new Button { Dock = DockStyle.Fill, Text = "Alvo seguro do teste" };
            button.Click += delegate { clicks++; };
            target.Controls.Add(button);
            target.Show();
            target.Activate();
            Application.DoEvents();
            Rectangle bounds = button.RectangleToScreen(button.ClientRectangle);
            Cursor.Position = new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);

            SendKeys.SendWait("{F6}");
            Pump(750, target, button);
            Check(!interval.Enabled && clicks == 0, "A preparação de 1 segundo não foi respeitada.");
            Pump(600, target, button);
            Check(clicks >= 2, "Não houve cliques reais na janela de teste.");
            SendKeys.SendWait("{F8}");
            Pump(100, target, button);
            int stoppedCount = clicks;
            Pump(250, target, button);
            Check(interval.Enabled && clicks == stoppedCount, "F8 não parou os cliques.");

            SendKeys.SendWait("{F6}");
            Pump(100, target, button);
            SendKeys.SendWait("{F8}");
            Pump(1100, target, button);
            Check(clicks == stoppedCount, "F8 não cancelou a preparação.");

            app.WindowState = FormWindowState.Minimized;
            SendKeys.SendWait("{F6}");
            Pump(1250, target, button);
            Check(clicks > stoppedCount, "Não funcionou com a janela minimizada.");
            SendKeys.SendWait("{F6}");
            Pump(100, target, button);
            stoppedCount = clicks;
            Pump(200, target, button);
            Check(interval.Enabled && clicks == stoppedCount, "F6 não desligou os cliques.");

            SendKeys.SendWait("{F6}");
            Pump(50, target, button);
            app.Close();
            Check(Native.RegisterHotKey(IntPtr.Zero, 50, 0x4000, (uint)Keys.F6), "F6 não foi liberado ao fechar.");
            Check(Native.RegisterHotKey(IntPtr.Zero, 51, 0x4000, (uint)Keys.F8), "F8 não foi liberado ao fechar.");
            Console.WriteLine("PASS: layout nativo, limites, preparação, cliques reais, F6/F8 globais, cancelamento, minimização e encerramento.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("FAIL: " + error);
            return 1;
        }
        finally
        {
            if (app != null && !app.IsDisposed)
            {
                SendMessage(app.Handle, 0x0312, new IntPtr(2), IntPtr.Zero);
                app.Dispose();
            }
            Native.UnregisterHotKey(IntPtr.Zero, 50);
            Native.UnregisterHotKey(IntPtr.Zero, 51);
            if (target != null) target.Dispose();
            Cursor.Position = originalCursor;
        }
    }
}
