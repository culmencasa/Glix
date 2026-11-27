namespace Glix;

/// <summary>
/// 横向多次折返 glick（t-glick）的识别参数，默认以短距离、短时长抑制误触。
/// </summary>
internal sealed class TGlickOptions
{
    /// <summary>
    /// 是否启用 t-glick。
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 触发所需的有效折返次数。
    /// </summary>
    public int RequiredReversals { get; set; } = 3;

    /// <summary>
    /// 第一程最小位移（像素）。
    /// </summary>
    public int FirstLegMinPx { get; set; } = 60;

    /// <summary>
    /// 后续每程最小位移（像素）。
    /// </summary>
    public int ReturnLegMinPx { get; set; } = 50;

    /// <summary>
    /// 全程纵向最大偏移（像素），超出即丢弃本次轨迹。
    /// </summary>
    public int MaxVerticalOffsetPx { get; set; } = 30;

    /// <summary>
    /// 最小瞬时峰值速度（像素/秒）。
    /// </summary>
    public int MinPeakSpeedPxPerSecond { get; set; } = 1000;

    /// <summary>
    /// 从起笔到完成全部折返的最大时长（毫秒）。
    /// </summary>
    public int MaxDurationMs { get; set; } = 320;

    /// <summary>
    /// 触发后的独立冷却时长（毫秒）。
    /// </summary>
    public int CooldownMs { get; set; } = 700;

    /// <summary>
    /// 抖动死区（像素），小于该位移的采样不参与统计。
    /// </summary>
    public int JitterDeadZonePx { get; set; } = 4;

    /// <summary>
    /// 是否输出调试日志。
    /// </summary>
    public bool DebugLogging { get; set; }

    /// <summary>
    /// 创建当前参数的独立副本。
    /// </summary>
    /// <returns>参数副本。</returns>
    public TGlickOptions Clone()
    {
        return new TGlickOptions
        {
            Enabled = Enabled,
            RequiredReversals = RequiredReversals,
            FirstLegMinPx = FirstLegMinPx,
            ReturnLegMinPx = ReturnLegMinPx,
            MaxVerticalOffsetPx = MaxVerticalOffsetPx,
            MinPeakSpeedPxPerSecond = MinPeakSpeedPxPerSecond,
            MaxDurationMs = MaxDurationMs,
            CooldownMs = CooldownMs,
            JitterDeadZonePx = JitterDeadZonePx,
            DebugLogging = DebugLogging
        };
    }

    /// <summary>
    /// 用另一组参数覆盖当前实例，供设置窗口取消时回滚实时预览。
    /// </summary>
    /// <param name="source">参数来源。</param>
    public void ApplyFrom(TGlickOptions source)
    {
        Enabled = source.Enabled;
        RequiredReversals = source.RequiredReversals;
        FirstLegMinPx = source.FirstLegMinPx;
        ReturnLegMinPx = source.ReturnLegMinPx;
        MaxVerticalOffsetPx = source.MaxVerticalOffsetPx;
        MinPeakSpeedPxPerSecond = source.MinPeakSpeedPxPerSecond;
        MaxDurationMs = source.MaxDurationMs;
        CooldownMs = source.CooldownMs;
        JitterDeadZonePx = source.JitterDeadZonePx;
        DebugLogging = source.DebugLogging;
    }
}
