using System;
using System.Collections.Generic;

namespace Glix;

/// <summary>
/// 判断光标是否位于资源管理器（File Explorer）的文件列表区域。
/// </summary>
/// <remarks>
/// 判定顺序为「由顶向下、自上而下」的快速短路：
/// 1. 先核验当前激活的前台窗口是否为资源管理器（毫秒级短路），不是则直接返回；
/// 2. 再核验光标下的子窗口确实属于该前台窗口，避免命中任务栏、其它程序或 OSD；
/// 3. 最后只向上查找 1~2 层父窗口，验证是否落在 SysListView32 / SHELLDLL_DefView 承载区域。
/// 窗口句柄会被系统回收复用，因此第三步的判定结果带有效期缓存，不能永久缓存。
/// </remarks>
internal static class ExplorerListHitTester
{
    #region 字段

    private static readonly string[] ExplorerRootClasses = { "CabinetWClass", "ExplorerWClass" };

    private const string ShellDefViewClass = "SHELLDLL_DefView";
    private const string SysListViewClass = "SysListView32";
    private const int MaxCachedWindows = 512;
    private const int CacheTtlMs = 500;
    private const int MaxShellDefViewDepth = 2;

    private static readonly Dictionary<IntPtr, CacheEntry> Cache = new Dictionary<IntPtr, CacheEntry>();
    private static readonly object CacheSync = new object();

    private readonly record struct CacheEntry(bool Result, long ExpiresAtMs);

    #endregion

    #region 公共方法

    /// <summary>
    /// 检测指定坐标是否落在资源管理器的文件列表区域内。
    /// </summary>
    /// <param name="x">屏幕坐标 X。</param>
    /// <param name="y">屏幕坐标 Y。</param>
    /// <param name="explorerWindow">命中时输出资源管理器顶层窗口句柄，否则为 <see cref="IntPtr.Zero"/>。</param>
    /// <returns>命中文件列表区域返回 <c>true</c>。</returns>
    public static bool IsOverFileList(int x, int y, out IntPtr explorerWindow)
    {
        explorerWindow = IntPtr.Zero;

        // 第一步（毫秒级短路）：当前激活窗口必须是资源管理器。
        // 若焦点在桌面、浏览器或其它程序上，直接返回，不消耗任何坐标判定与命中测试算力。
        IntPtr foregroundWindow = NativeMethods.GetForegroundWindow();

        if (foregroundWindow == IntPtr.Zero || !IsExplorerRoot(foregroundWindow))
        {
            return false;
        }

        // 第二步：光标下的子窗口必须属于该前台窗口本身。
        // 鼠标飘到其它后台窗口、任务栏甚至 OSD 上方时，直接拒绝。
        IntPtr childHandle = WindowInspector.WindowFromPoint(x, y);

        if (childHandle == IntPtr.Zero)
        {
            return false;
        }

        IntPtr rootWindow = NativeMethods.GetAncestor(childHandle, NativeMethods.GA_ROOT);

        if (rootWindow != foregroundWindow)
        {
            return false;
        }

        // 第三步：验证光标落在文件列表区域（SysListView32 或 SHELLDLL_DefView 承载容器）。
        // 该步涉及跨进程类名查询，带短效缓存。
        long now = Environment.TickCount64;

        if (TryReadCache(childHandle, now, out bool cachedResult))
        {
            explorerWindow = foregroundWindow;

            return cachedResult;
        }

        bool result = IsListViewArea(childHandle);
        WriteCache(childHandle, result, now);

        if (result)
        {
            explorerWindow = foregroundWindow;
        }

        return result;
    }

    #endregion

    #region 私有方法

    private static bool IsExplorerRoot(IntPtr windowHandle)
    {
        return Array.IndexOf(ExplorerRootClasses, WindowInspector.GetClassName(windowHandle)) >= 0;
    }

    private static bool IsListViewArea(IntPtr childHandle)
    {
        // 直接命中 ListView 控件。
        if (WindowInspector.GetClassName(childHandle) == SysListViewClass)
        {
            return true;
        }

        // 现代分组视图 / 新版标签页视图的承载容器为 SHELLDLL_DefView，
        // 仅向上查找 1~2 层父窗口即可，绝不无休止地向上遍历 64 层。
        IntPtr current = NativeMethods.GetParent(childHandle);

        for (int level = 1; level <= MaxShellDefViewDepth && current != IntPtr.Zero; level++)
        {
            if (WindowInspector.GetClassName(current) == ShellDefViewClass)
            {
                return true;
            }

            current = NativeMethods.GetParent(current);
        }

        return false;
    }

    private static bool TryReadCache(IntPtr windowHandle, long now, out bool result)
    {
        result = false;

        lock (CacheSync)
        {
            if (!Cache.TryGetValue(windowHandle, out CacheEntry entry))
            {
                return false;
            }

            if (entry.ExpiresAtMs <= now || !NativeMethods.IsWindow(windowHandle))
            {
                Cache.Remove(windowHandle);

                return false;
            }

            result = entry.Result;

            return true;
        }
    }

    private static void WriteCache(IntPtr windowHandle, bool result, long now)
    {
        lock (CacheSync)
        {
            if (Cache.Count >= MaxCachedWindows)
            {
                Cache.Clear();
            }

            Cache[windowHandle] = new CacheEntry(result, now + CacheTtlMs);
        }
    }

    #endregion
}
