using System;
using System.IO;
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

        [STAThread]
        private static int Main(string[] args)
        {
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
                SignalRunningInstance();
                return 0;
            }

            if (!owned)
            {
                SignalRunningInstance();
                mutex.Dispose();
                return 0;
            }

            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += (sender, e) => ReportCrash(e.Exception);

                using (var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName))
                using (var form = new MainForm(SettingsPath()))
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
                }
                return 0;
            }
            finally
            {
                mutex.ReleaseMutex();
                mutex.Dispose();
            }
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

        private static void ReportCrash(Exception error)
        {
            MessageBox.Show(Loc.F("msg.crash", error.Message), "V-Clicker",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
