using System;
using System.Text;

namespace Glix;

/// <summary>
/// 提供窗口类名查询能力，用于判断光标下方的控件。
/// </summary>
internal static class WindowInspector
{
    #region 字段

    private const int MaxClassNameLength = 256;

    [ThreadStatic]
    private static StringBuilder? _classNameBuffer;

    #endregion

    #region 公共方法

    /// <summary>
    /// 获取指定窗口的类名。
    /// </summary>
    /// <remarks>
    /// 使用 <see cref="NativeMethods.GetClassName"/> API 直接读取窗口管理器中的类名，
    /// 不会向目标窗口线程发送任何消息，因此不会在低级鼠标钩子处理路径中引发跨进程死锁，
    /// 也不会被处于“未响应”状态的窗口拖垮。请勿改用 WM_GETCLASSNAME 消息式查询。
    /// </remarks>
    /// <param name="windowHandle">窗口句柄。</param>
    /// <returns>窗口类名；读取失败时返回空字符串。</returns>
    public static string GetClassName(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return string.Empty;
        }

        StringBuilder builder = _classNameBuffer ??= new StringBuilder(MaxClassNameLength);
        builder.Clear();

        int length = NativeMethods.GetClassName(windowHandle, builder, builder.Capacity);

        return length == 0 ? string.Empty : builder.ToString();
    }

    /// <summary>
    /// 获取光标所在位置最底层的窗口句柄。
    /// </summary>
    /// <param name="x">屏幕坐标 X。</param>
    /// <param name="y">屏幕坐标 Y。</param>
    /// <returns>窗口句柄。</returns>
    public static IntPtr WindowFromPoint(int x, int y)
    {
        NativeMethods.POINT point = new NativeMethods.POINT
        {
            X = x,
            Y = y
        };

        return NativeMethods.WindowFromPoint(point);
    }

    #endregion
}
