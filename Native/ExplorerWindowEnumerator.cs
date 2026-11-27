using System;
using System.Collections.Generic;
using System.Text;

namespace Glix;

/// <summary>
/// 资源管理器顶层窗口信息。
/// </summary>
/// <param name="Handle">顶层窗口句柄。</param>
/// <param name="Title">窗口标题。</param>
internal readonly record struct ExplorerWindow(IntPtr Handle, string Title);

/// <summary>
/// 枚举当前所有可见的资源管理器顶层窗口，供窗口切换器展示。
/// </summary>
internal static class ExplorerWindowEnumerator
{
    #region 字段

    private static readonly string[] ExplorerRootClasses = { "CabinetWClass", "ExplorerWClass" };
    private static readonly List<IntPtr> _stableOrder = new List<IntPtr>();

    #endregion

    #region 公共方法

    /// <summary>
    /// 获取所有可见资源管理器窗口的句柄与标题。
    /// </summary>
    /// <returns>窗口列表。</returns>
    public static IReadOnlyList<ExplorerWindow> GetWindows()
    {
        List<ExplorerWindow> windows = new List<ExplorerWindow>();

        NativeMethods.EnumWindowsProc callback = (handle, _) =>
        {
            if (!NativeMethods.IsWindowVisible(handle))
            {
                return true;
            }

            if (Array.IndexOf(ExplorerRootClasses, WindowInspector.GetClassName(handle)) < 0)
            {
                return true;
            }

            string title = ReadTitle(handle);

            if (title.Length == 0)
            {
                return true;
            }

            windows.Add(new ExplorerWindow(handle, title));

            return true;
        };

        NativeMethods.EnumWindows(callback, IntPtr.Zero);

        ApplyStableOrder(windows);

        return windows;
    }

    #endregion

    #region 私有方法

    /// <summary>
    /// 按首次发现的顺序排列窗口，避免激活导致窗口跳到首位。
    /// </summary>
    /// <param name="windows">待排序的窗口列表。</param>
    private static void ApplyStableOrder(List<ExplorerWindow> windows)
    {
        for (int index = _stableOrder.Count - 1; index >= 0; index--)
        {
            if (!ContainsHandle(windows, _stableOrder[index]))
            {
                _stableOrder.RemoveAt(index);
            }
        }

        foreach (ExplorerWindow window in windows)
        {
            if (!_stableOrder.Contains(window.Handle))
            {
                _stableOrder.Add(window.Handle);
            }
        }

        windows.Sort((left, right) => _stableOrder.IndexOf(left.Handle).CompareTo(_stableOrder.IndexOf(right.Handle)));
    }

    private static bool ContainsHandle(List<ExplorerWindow> windows, IntPtr handle)
    {
        foreach (ExplorerWindow window in windows)
        {
            if (window.Handle == handle)
            {
                return true;
            }
        }

        return false;
    }

    private static string ReadTitle(IntPtr handle)
    {
        int length = NativeMethods.GetWindowTextLength(handle);

        if (length <= 0)
        {
            return string.Empty;
        }

        StringBuilder builder = new StringBuilder(length + 1);
        int copied = NativeMethods.GetWindowText(handle, builder, builder.Capacity);

        return copied == 0 ? string.Empty : builder.ToString();
    }

    #endregion
}
