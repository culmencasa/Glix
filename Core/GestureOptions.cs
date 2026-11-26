namespace Glix;

/// <summary>
/// 圆形手势识别的阈值配置。
/// </summary>
internal sealed class GestureOptions
{
    /// <summary>
    /// 触发所需的最小轨迹长度（像素）。
    /// </summary>
    public int MinPathLengthPx { get; set; } = 120;

    /// <summary>
    /// 触发所需的累计转角绝对值（度）。放宽后滑过半圆多一点即可识别，降低"需停顿等待闭合"的延迟感。
    /// </summary>
    public int MinTurnDegrees { get; set; } = 240;

    /// <summary>
    /// 轨迹包围盒长边的最小长度（像素）。
    /// </summary>
    public int MinSpanPx { get; set; } = 120;

    /// <summary>
    /// 轨迹包围盒长边的最大长度（像素），超过则视为幅度过大而不触发。
    /// </summary>
    public int MaxSpanPx { get; init; } = 420;

    /// <summary>
    /// 包围盒短边与长边的最小比值，用于排除横向来回等扁长轨迹。
    /// </summary>
    public double MinAspectRatio { get; init; } = 0.45;

    /// <summary>
    /// 轨迹点到包围盒中心的距离的最大变异系数（标准差/均值），用于排除折线、S 形等非圆形轨迹。
    /// </summary>
    public double MaxRadiusVariation { get; init; } = 0.35;

    /// <summary>
    /// 坐标级抖动过滤阈值（像素）。位移低于该值的输入直接忽略，不喂给识别器。
    /// </summary>
    public int JitterPx { get; init; } = 2;

    /// <summary>
    /// 参与识别的单段最小位移（像素）。不足该距离的段不纳入转角与路径计算。
    /// </summary>
    public int MinStrokeSegmentPx { get; init; } = 10;

    /// <summary>
    /// 允许的最大单段位移（像素），超过则视为光标跳跃并重置手势。
    /// </summary>
    public int MaxSegmentPx { get; init; } = 600;

    /// <summary>
    /// 手势允许的最长持续时间（毫秒）。超过该时长才画完的动作无效，必须重新起笔。
    /// </summary>
    public int MaxGestureDurationMs { get; init; } = 600;

    /// <summary>
    /// 鼠标静止超过该时长（毫秒）即重置手势。
    /// </summary>
    public int IdleResetMs { get; init; } = 200;

    /// <summary>
    /// 光标离开文件列表区域的宽限时长（毫秒）。窗口切换或目录加载会造成判定瞬时断开，
    /// 在宽限期内不重置轨迹，避免正在进行的手势被打断。
    /// </summary>
    public int OffListGraceMs { get; init; } = 800;

    /// <summary>
    /// 识别到一次手势后，进入短暂的硬屏蔽期（毫秒）。屏蔽期内完全忽略鼠标位移、不记录新点，
    /// 用于丢弃画圈尾段、手抖等余动，彻底杜绝一次完整动作被反复识别成多个手势。
    /// </summary>
    public int GestureSuppressionMs { get; init; } = 250;

    /// <summary>
    /// 最多缓存的轨迹点数量。
    /// </summary>
    public int MaxBufferedPoints { get; init; } = 256;
}
