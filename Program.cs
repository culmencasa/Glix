using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace Glix;

/// <summary>
/// 程序入口。
/// </summary>
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using Mutex singleInstance = new Mutex(initiallyOwned: true, @"Local\Glix.SingleInstance", out bool createdNew);

        if (!createdNew)
        {
            return;
        }

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.ThreadException += (_, e) => LogException("界面线程异常", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => LogException("未处理异常", e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString() ?? "未知异常"));

        try
        {
            Application.Run(new TrayAppContext());
        }
        catch (Exception exception)
        {
            LogException("启动异常", exception);
        }
    }

    private static void LogException(string kind, Exception exception)
    {
        try
        {
            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Glix");
            Directory.CreateDirectory(directory);
            File.AppendAllText(
                Path.Combine(directory, "error.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {kind}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // 日志写入失败不影响主流程。
        }
    }
}
