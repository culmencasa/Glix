using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;

namespace Glix;

/// <summary>
/// 通过 Shell 的窗口列表取得资源管理器当前文件夹路径，再读取该路径的真实系统图标。
/// </summary>
/// <remarks>
/// 外壳 COM 不可用、路径为空或图标读取失败时，统一回退到传入的文件夹图标。
/// </remarks>
internal static class ExplorerIconResolver
{
    #region 公共方法

    /// <summary>
    /// 为每个资源管理器窗口解析对应的图标。
    /// </summary>
    /// <param name="windows">窗口列表。</param>
    /// <param name="fallback">解析失败时使用的图标。</param>
    /// <returns>按窗口句柄索引的图标字典。</returns>
    public static Dictionary<IntPtr, Bitmap> Resolve(IReadOnlyList<ExplorerWindow> windows, Bitmap fallback)
    {
        (Dictionary<IntPtr, string> byHandle, Dictionary<string, string> byTitle) = ReadShellPaths();
        Dictionary<IntPtr, Bitmap> icons = new Dictionary<IntPtr, Bitmap>();

        foreach (ExplorerWindow window in windows)
        {
            Bitmap? icon = null;
            string? path = null;

            if (byHandle.TryGetValue(window.Handle, out string? handlePath))
            {
                path = handlePath;
            }
            else if (byTitle.TryGetValue(window.Title, out string? titlePath))
            {
                path = titlePath;
            }

            if (!string.IsNullOrEmpty(path))
            {
                icon = LoadIcon(path);
            }

            icons[window.Handle] = icon ?? new Bitmap(fallback);
        }

        return icons;
    }

    #endregion

    #region 私有方法

    private static (Dictionary<IntPtr, string> ByHandle, Dictionary<string, string> ByTitle) ReadShellPaths()
    {
        Dictionary<IntPtr, string> byHandle = new Dictionary<IntPtr, string>();
        Dictionary<string, string> byTitle = new Dictionary<string, string>();

        try
        {
            Type? shellType = Type.GetTypeFromProgID("Shell.Application");

            if (shellType is null)
            {
                return (byHandle, byTitle);
            }

            object? shellInstance = Activator.CreateInstance(shellType);

            if (shellInstance is null)
            {
                return (byHandle, byTitle);
            }

            dynamic shell = shellInstance;
            dynamic windows = shell.Windows();
            int count = Convert.ToInt32(windows.Count);

            for (int index = 0; index < count; index++)
            {
                dynamic? window = null;

                try
                {
                    window = windows.Item(index);

                    if (window is null)
                    {
                        continue;
                    }

                    object? handle = window.HWND;
                    object? title = window.LocationName;
                    dynamic? document = window.Document;
                    dynamic? folder = document?.Folder;
                    dynamic? item = folder?.Self;
                    object? path = item?.Path;

                    if (path is null)
                    {
                        continue;
                    }

                    string pathText = path.ToString() ?? string.Empty;

                    if (handle is not null)
                    {
                        byHandle[new IntPtr(Convert.ToInt64(handle))] = pathText;
                    }

                    if (title is not null && title.ToString() is string titleText && titleText.Length > 0)
                    {
                        byTitle[titleText] = pathText;
                    }
                }
                catch (Exception)
                {
                    // 单个窗口解析失败时跳过，不影响其它窗口。
                }
                finally
                {
                    if (window is not null)
                    {
                        Marshal.ReleaseComObject(window);
                    }
                }
            }

            Marshal.ReleaseComObject(shellInstance);
        }
        catch (Exception)
        {
            // 外壳 COM 不可用时静默回退到文件夹图标。
        }

        return (byHandle, byTitle);
    }

    private static Bitmap? LoadIcon(string path)
    {
        NativeMethods.SHFILEINFO info = new NativeMethods.SHFILEINFO();
        IntPtr result = NativeMethods.SHGetFileInfo(
            path,
            0,
            ref info,
            (uint)Marshal.SizeOf<NativeMethods.SHFILEINFO>(),
            NativeMethods.SHGFI_ICON | NativeMethods.SHGFI_SMALLICON);

        if (result == IntPtr.Zero || info.HIcon == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            using Icon icon = Icon.FromHandle(info.HIcon);

            return new Bitmap(icon.ToBitmap(), new Size(28, 28));
        }
        finally
        {
            NativeMethods.DestroyIcon(info.HIcon);
        }
    }

    #endregion
}
