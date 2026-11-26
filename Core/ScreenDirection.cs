namespace Glix;

/// <summary>
/// 屏幕主移动方向，用于驱动托盘图标的方向指示。
/// </summary>
internal enum ScreenDirection
{
    /// <summary>
    /// 尚未确定方向。
    /// </summary>
    None,

    /// <summary>
    /// 向上。
    /// </summary>
    Up,

    /// <summary>
    /// 向下。
    /// </summary>
    Down,

    /// <summary>
    /// 向左。
    /// </summary>
    Left,

    /// <summary>
    /// 向右。
    /// </summary>
    Right
}
