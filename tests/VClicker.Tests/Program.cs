using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace VClicker.Tests
{
    /// <summary>
    /// Dependency-free test runner.
    ///   (no args)        unit tests; safe, never touches the mouse or keyboard
    ///   --integration    also drives the real UI with real clicks and hotkeys (moves the cursor for a few seconds)
    ///   --screenshot P   renders the window to a PNG (docs / visual check)
    /// </summary>
    internal static class Program
    {
        private static int passed, failed;

        internal static void Test(string name, Action body)
        {
            try
            {
                body();
                passed++;
                Console.WriteLine("  ok    " + name);
            }
            catch (Exception error)
            {
                failed++;
                Console.WriteLine("  FAIL  " + name);
                Console.WriteLine("        " + error.Message.Replace("\n", "\n        "));
            }
        }

        internal static void True(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }

        internal static void Equal<T>(T expected, T actual, string what)
        {
            if (!Equals(expected, actual))
                throw new Exception(what + ": expected <" + expected + "> but was <" + actual + ">");
        }

        /// <summary>Every control under <paramref name="root"/>, depth first.</summary>
        internal static IEnumerable<Control> All(Control root) => root.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(All(c)));

        /// <summary>A click sink that accepts every click and does nothing (the unit tests never touch the mouse).</summary>
        internal sealed class NullSink : IClickSink
        {
            public bool Click(ClickButton button, bool doubleClick, Point? at) => true;
        }

        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (args.Length >= 2 && args[0] == "--screenshot")
                return Screenshot.Take(args[1], args.Skip(2).ToArray());

            Console.WriteLine("Unit tests");
            UnitTests.Run();

            if (args.Contains("--integration"))
            {
                Console.WriteLine("Integration tests (real input)");
                IntegrationTests.Run();
            }

            Console.WriteLine();
            Console.WriteLine(failed == 0
                ? "PASS: " + passed + " tests"
                : "FAIL: " + failed + " failed, " + passed + " passed");
            return failed == 0 ? 0 : 1;
        }
    }
}
