namespace Glix;

/// <summary>
/// 导航动作闸门的时序配置。
/// </summary>
internal sealed class NavigationGateOptions
{
    /// <summary>
    /// 两次导航之间的最短冷却时长（毫秒）。处于冷却中时识别到的新手势会被丢弃而不是排队。
    /// </summary>
    public int CooldownMs { get; init; } = 350;
}
