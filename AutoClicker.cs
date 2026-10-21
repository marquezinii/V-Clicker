using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        try
        {
            Application.Run(new AutoClickerForm());
        }
        catch (Exception error)
        {
            MessageBox.Show(error.Message, "AutoClicker", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}

internal sealed class AutoClickerForm : Form
{
    private readonly NumericUpDown interval = new NumericUpDown();
    private readonly Button toggle = new Button();
    private readonly Label status = new Label();
    private readonly Timer timer = new Timer();
    private readonly Native.Input[] click = Native.CreateClick();

    public AutoClickerForm()
    {
        Text = "AutoClicker";
        ClientSize = new Size(390, 260);
        Font = new Font("Segoe UI", 10);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;

        Controls.Add(new Label {
            Text = "Cliques automáticos", Font = new Font(Font, FontStyle.Bold),
            Location = new Point(24, 20), AutoSize = true
        });
        Controls.Add(new Label {
            Text = "Intervalo entre cliques (ms)",
            Location = new Point(24, 60), AutoSize = true
        });
        interval.Name = "Interval";
        interval.SetBounds(250, 56, 115, 30);
        interval.Minimum = 20;
        interval.Maximum = 60000;
        interval.Value = 100;
        interval.Increment = 10;
        interval.AccessibleName = "Intervalo entre cliques em milissegundos";
        Controls.Add(interval);

        toggle.Name = "Toggle";
        toggle.Text = "Iniciar (F6)";
        toggle.SetBounds(24, 105, 341, 40);
        toggle.Click += delegate { Toggle(); };
        Controls.Add(toggle);

        status.Name = "Status";
        status.SetBounds(24, 158, 341, 25);
        status.Text = "Parado";
        Controls.Add(status);
        Controls.Add(new Label {
            Text = "F6: ligar/desligar  •  F8: parar\nClique esquerdo na posição atual do cursor.\nAo iniciar, você tem 1 segundo para posicionar.",
            Location = new Point(24, 192), Size = new Size(350, 62)
        });

        timer.Tick += delegate { SendClick(); };
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        bool f6 = Native.RegisterHotKey(Handle, 1, 0x4000, (uint)Keys.F6);
        bool f8 = Native.RegisterHotKey(Handle, 2, 0x4000, (uint)Keys.F8);
        if (!f6 || !f8)
        {
            Native.UnregisterHotKey(Handle, 1);
            Native.UnregisterHotKey(Handle, 2);
            throw new InvalidOperationException(
                "Não foi possível registrar F6 e F8. Feche outra instância do AutoClicker " +
                "ou o aplicativo que já usa essas teclas e tente novamente.");
        }
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        Stop();
        Native.UnregisterHotKey(Handle, 1);
        Native.UnregisterHotKey(Handle, 2);
        base.OnHandleDestroyed(e);
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == 0x0312)
        {
            if (message.WParam.ToInt32() == 1) Toggle();
            if (message.WParam.ToInt32() == 2) Stop();
        }
        base.WndProc(ref message);
    }

    private void Toggle()
    {
        if (timer.Enabled)
        {
            Stop();
            return;
        }
        interval.Enabled = false;
        toggle.Text = "Parar (F6 / F8)";
        status.Text = "Iniciando em 1 segundo...";
        timer.Interval = 1000;
        timer.Start();
    }

    private void Stop()
    {
        timer.Stop();
        interval.Enabled = true;
        toggle.Text = "Iniciar (F6)";
        status.Text = "Parado";
    }

    private void SendClick()
    {
        // shortcut: timer da interface tem precisão limitada; use outro agendador se precisar de alta precisão.
        timer.Interval = (int)interval.Value;
        if (Native.SendInput(2, click, Marshal.SizeOf(typeof(Native.Input))) != 2)
        {
            Stop();
            Native.SendInput(1, new[] { click[1] }, Marshal.SizeOf(typeof(Native.Input)));
            MessageBox.Show(this,
                "O Windows bloqueou o clique. O autoclicker foi parado. " +
                "Aplicativos executados como administrador podem bloquear os cliques.",
                "AutoClicker", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        status.Text = "Clicando • F8 para parar";
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) timer.Dispose();
        base.Dispose(disposing);
    }
}

internal static class Native
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct MouseInput
    {
        internal int dx, dy;
        internal uint mouseData, flags, time;
        internal UIntPtr extraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Input
    {
        internal uint type;
        internal MouseInput mouse;
    }

    internal static Input[] CreateClick()
    {
        return new[] {
            new Input { mouse = new MouseInput { flags = 0x0002 } },
            new Input { mouse = new MouseInput { flags = 0x0004 } }
        };
    }

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint SendInput(uint count, Input[] inputs, int size);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool UnregisterHotKey(IntPtr window, int id);
}
