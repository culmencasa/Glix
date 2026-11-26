using System;

namespace Glix;

/// <summary>
/// 向资源管理器窗口发送后退/前进导航命令。
/// </summary>
internal sealed class NavigationInvoker
{
    #region 公共方法

    /// <summary>
    /// 执行后退。
    /// </summary>
    /// <param name="explorerWindow">资源管理器顶层窗口句柄。</param>
    public void GoBack(IntPtr explorerWindow)
    {
        PostAppCommand(explorerWindow, NativeMethods.APPCOMMAND_BROWSER_BACKWARD);
    }

    /// <summary>
    /// 执行前进。
    /// </summary>
    /// <param name="explorerWindow">资源管理器顶层窗口句柄。</param>
    public void GoForward(IntPtr explorerWindow)
    {
        PostAppCommand(explorerWindow, NativeMethods.APPCOMMAND_BROWSER_FORWARD);
    }

    #endregion

    #region 私有方法

    private static void PostAppCommand(IntPtr explorerWindow, int appCommand)
    {
        if (explorerWindow == IntPtr.Zero)
        {
            return;
        }

        IntPtr lParam = (IntPtr)(appCommand << 16);
        NativeMethods.PostMessage(explorerWindow, NativeMethods.WM_APPCOMMAND, explorerWindow, lParam);
    }

    #endregion
}
