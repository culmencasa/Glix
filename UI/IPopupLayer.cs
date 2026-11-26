using System;

namespace Glix;

/// <summary>
/// 弹出层视图：在屏幕指定位置显示短暂或常驻的浮层内容（菜单、面板、启动器等）。
/// 复刻 PopMouse 的「呼出快捷窗口」时，各种弹层均为本接口的实现。
/// </summary>
internal interface IPopupLayer : IDisposable
{
    /// <summary>
    /// 在指定的屏幕坐标处展示弹出层。
    /// </summary>
    /// <param name="x">屏幕坐标 X。</param>
    /// <param name="y">屏幕坐标 Y。</param>
    void ShowAt(int x, int y);

    /// <summary>
    /// 立即隐藏并解散弹出层。
    /// </summary>
    void Dismiss();
}
