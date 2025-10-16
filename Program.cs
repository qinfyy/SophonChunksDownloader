//#define ENABLE_CONSOLE

using System.Runtime.InteropServices;

namespace SophonChunksDownloader
{
    internal static class Program
    {
#if ENABLE_CONSOLE
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AllocConsole();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeConsole();
#endif

        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
#if ENABLE_CONSOLE
            AllocConsole();
#endif

            // To customize application configuration such as set high DPI settings or default font,
            // see https://aka.ms/applicationconfiguration.
            ApplicationConfiguration.Initialize();
            Application.Run(new Form1());

#if ENABLE_CONSOLE
            FreeConsole();
#endif
        }
    }
}