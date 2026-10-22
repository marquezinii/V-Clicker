using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace VClicker
{
    internal static class Program
    {
        private const string MutexName = @"Local\V-Clicker.SingleInstance";
        private const string ShowEventName = @"Local\V-Clicker.Show";

        /// <summary>Portable mode: a V-Clicker.ini next to the .exe wins; otherwise %APPDATA%\V-Clicker.</summary>
        internal static string SettingsPath()
        {
            string portable = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "V-Clicker.ini");
            if (File.Exists(portable)) return portable;
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "V-Clicker", "settings.ini");
        }

        [DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int processId);

        private static bool consoleAttached;

        /// <summary>A line for whoever started V-Clicker from a console (a GUI app has none of its own).</summary>
        internal static void Say(string text)
        {
            if (!consoleAttached) consoleAttached = AttachConsole(-1); // ATTACH_PARENT_PROCESS
            try { Console.Error.WriteLine(text); }
            catch (IOException) { } // no console, nobody to tell
        }

        [STAThread]
        private static int Main(string[] args)
        {
            // Developer Mode's command line (docs/developer.md): checked before anything opens.
            CommandLine command = CommandLine.Parse(args);
            if (command.Help || command.Error != null)
            {
                Say((command.Error != null ? command.Error + "\n\n" : string.Empty) + CommandLine.Usage);
                return command.Help ? ExitCodes.Done : ExitCodes.BadArguments;
            }

            // Relaunching as administrator: give the old instance a moment to exit first.
            bool waitForPrevious = Array.IndexOf(args, "--wait-previous") >= 0;

            Mutex mutex;
            bool owned;
            try
            {
                mutex = new Mutex(false, MutexName);
                try
                {
                    owned = mutex.WaitOne(waitForPrevious ? 8000 : 0);
                }
                catch (AbandonedMutexException)
                {
                    owned = true; // previous instance crashed; the mutex is ours now
                }
            }
            catch (UnauthorizedAccessException)
            {
                // The running instance is elevated and we are not (or vice versa).
                return AlreadyRunning(command);
            }

            if (!owned)
            {
                mutex.Dispose();
                return AlreadyRunning(command);
            }

            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += (sender, e) => ReportCrash(e.Exception);

                using (var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName))
                using (var form = new MainForm(SettingsPath()) { Command = command })
                {
                    RegisteredWaitHandle registration = ThreadPool.RegisterWaitForSingleObject(
                        showSignal, (state, timedOut) => RequestShow(form), null, Timeout.Infinite, false);
                    try
                    {
                        Application.Run(form);
                    }
                    finally
                    {
                        registration.Unregister(null);
                    }
                    return form.ExitCode;
                }
            }
            finally
            {
                mutex.ReleaseMutex();
                mutex.Dispose();
            }
        }

        /// <summary>
        /// A second launch shows the open window. A command does not act through it: one V-Clicker at a time moves the mouse.
        /// </summary>
        private static int AlreadyRunning(CommandLine command)
        {
            SignalRunningInstance();
            if (!command.HasAction) return ExitCodes.Done;
            Say("V-Clicker is already open: close it to run a command.");
            return ExitCodes.AlreadyRunning;
        }

        private static void RequestShow(MainForm form)
        {
            try
            {
                if (form.IsHandleCreated && !form.IsDisposed) form.BeginInvoke(new Action(form.ShowFromTray));
            }
            catch (InvalidOperationException)
            {
                // The window is closing; nothing to show.
            }
        }

        private static void SignalRunningInstance()
        {
            try
            {
                using (EventWaitHandle signal = EventWaitHandle.OpenExisting(ShowEventName)) signal.Set();
            }
            catch (WaitHandleCannotBeOpenedException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static bool reporting;

        /// <summary>One dialog at a time: a fault in a timer tick would otherwise stack a dialog per tick.</summary>
        private static void ReportCrash(Exception error)
        {
            if (reporting) return;
            reporting = true;
            try
            {
                MessageBox.Show(Loc.F("msg.crash", error.Message), "V-Clicker",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally { reporting = false; }
        }
    }
}
