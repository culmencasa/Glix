using Microsoft.Win32;

namespace Glix;

/// <summary>
/// 当前用户级别的开机启动管理：读写 HKCU 的 Run 注册表项，不需要管理员权限。
/// </summary>
internal static class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string EntryName = "Glix";

    /// <summary>
    /// 读取开机启动是否已启用。
    /// </summary>
    /// <returns>已启用返回 true，否则返回 false。</returns>
    public static bool IsEnabled()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath);

            return key?.GetValue(EntryName) is string value && !string.IsNullOrWhiteSpace(value);
        }
        catch (Exception)
        {
            // 读取失败时按未启用处理。
            return false;
        }
    }

    /// <summary>
    /// 设置开机启动状态，启用时写入当前程序路径。
    /// </summary>
    /// <param name="enabled">是否启用开机启动。</param>
    public static void SetEnabled(bool enabled)
    {
        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath);

            if (!enabled)
            {
                key.DeleteValue(EntryName, throwOnMissingValue: false);
                return;
            }

            string? executablePath = Environment.ProcessPath;

            if (string.IsNullOrWhiteSpace(executablePath))
            {
                return;
            }

            key.SetValue(EntryName, $"\"{executablePath}\"");
        }
        catch (Exception)
        {
            // 写入失败时保持原有状态，不影响程序运行。
        }
    }
}
