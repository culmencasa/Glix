using System;

namespace Glix;

/// <summary>
/// 精确判定光标是否位于资源管理器主文件列表视图内（图标视图 / 详情视图）。
/// </summary>
/// <remarks>
/// 不使用进程名判断：前台顶层窗口必须是资源管理器根窗口，光标下的最深层子窗口必须归属该窗口，
/// 且类名属于文件列表或其承载容器。导航窗格、地址栏、搜索框、工具栏、列标题、滚动条、预览窗格
/// 的类名均被排除。所有坐标直接来自低级鼠标钩子的屏幕原生像素坐标，不做任何逻辑坐标换算。
/// </remarks>
internal static class ExplorerFileViewHitTester
{
    #region 常量

    private static readonly string[] ExplorerRootClasses = { "CabinetWClass", "ExplorerWClass" };

    private static readonly string[] ExcludedClasses =
    {
        "Header",
        "ScrollBar",
        "ComboBox",
        "ComboBoxEx32",
        "Edit",
        "Button",
        "ToolbarWindow32",
        "SysTreeView32",
        "Static"
    };

    private const string SysListViewClass = "SysListView32";
    private const string ShellDefViewClass = "SHELLDLL_DefView";
    private const int MaxAncestorDepth = 3;

    #endregion

    #region 公共方法

    /// <summary>
    /// 判定指定屏幕坐标是否落在文件视图内，并输出可用于识别视图重建的句柄。
    /// </summary>
    /// <param name="x">屏幕原生坐标 X。</param>
    /// <param name="y">屏幕原生坐标 Y。</param>
    /// <param name="fileView">命中时输出文件视图或其承载容器句柄。</param>
    /// <returns>命中文件视图返回 <c>true</c>。</returns>
    public static bool IsOverFileView(int x, int y, out IntPtr fileView)
    {
        fileView = IntPtr.Zero;

        IntPtr foreground = NativeMethods.GetForegroundWindow();

        if (foreground == IntPtr.Zero || Array.IndexOf(ExplorerRootClasses, WindowInspector.GetClassName(foreground)) < 0)
        {
            return false;
        }

        IntPtr child = WindowInspector.WindowFromPoint(x, y);

        if (child == IntPtr.Zero || NativeMethods.GetAncestor(child, NativeMethods.GA_ROOT) != foreground)
        {
            return false;
        }

        string childClass = WindowInspector.GetClassName(child);

        if (Array.IndexOf(ExcludedClasses, childClass) >= 0)
        {
            return false;
        }

        if (childClass == SysListViewClass)
        {
            fileView = child;

            return true;
        }

        IntPtr current = child;

        for (int depth = 0; depth < MaxAncestorDepth && current != IntPtr.Zero; depth++)
        {
            current = NativeMethods.GetParent(current);

            if (current == IntPtr.Zero)
            {
                break;
            }

            string currentClass = WindowInspector.GetClassName(current);

            if (currentClass == SysListViewClass || currentClass == ShellDefViewClass)
            {
                fileView = current;

                return true;
            }
        }

        return false;
    }

    #endregion
}
