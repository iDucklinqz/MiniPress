using System;
using System.Reflection;
using System.Windows.Forms;

[assembly: AssemblyTitle("MiniPress")]
[assembly: AssemblyDescription("拖入视频，一键压缩")]
[assembly: AssemblyProduct("MiniPress")]
[assembly: AssemblyCopyright("MIT License")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace MiniPress
{
    static class Program
    {
        public const string Version = "1.0.0";

        /// <summary>Files passed on the command line (e.g. dropped onto MiniPress.exe) are loaded at start.</summary>
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm(args));
        }
    }
}
