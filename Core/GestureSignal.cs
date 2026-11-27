using System;

namespace Glix;

/// <summary>
/// 手势信号类型。新增触发方式（如方位热区、中心面板）时在此追加枚举值即可。
/// </summary>
internal enum GestureSignalKind
{
    /// <summary>
    /// 未定义。
    /// </summary>
    None,

    /// <summary>
    /// 顺时针画圈（屏幕坐标系）。
    /// </summary>
    CircleClockwise,

    /// <summary>
    /// 逆时针画圈（屏幕坐标系）。
    /// </summary>
    CircleCounterClockwise,

    /// <summary>
    /// 资源管理器文件视图内的横向往返 glick，请求打开窗口切换器。
    /// </summary>
    TGlick
}

/// <summary>
/// 由 <see cref="IGestureTrigger"/> 产出的统一手势信号，与具体的触发方式、动作实现完全解耦。
/// </summary>
/// <param name="Kind">信号类型。</param>
/// <param name="X">信号产生时的屏幕坐标 X。</param>
/// <param name="Y">信号产生时的屏幕坐标 Y。</param>
/// <param name="TimestampMs">信号时间戳（毫秒）。</param>
/// <param name="ContextWindow">信号产生时可用的上下文窗口句柄，例如当前资源管理器顶层窗口。</param>
/// <param name="Confidence">识别置信度；同一次移动中多个触发源同时命中时取置信度最高者。</param>
internal readonly record struct GestureSignal(
    GestureSignalKind Kind,
    int X,
    int Y,
    long TimestampMs,
    IntPtr ContextWindow,
    double Confidence = 0d);
