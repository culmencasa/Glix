using System;

namespace Glix;

/// <summary>
/// 导航动作闸门：以固定的冷却时间防止同一动作被重复执行或排队。
/// </summary>
/// <remarks>
/// 不依赖资源管理器窗口标题是否变化（Win11 标签页等场景下标题更新不可靠且可能延迟），
/// 仅用绝对冷却窗口拦截过密的手势，配合轨迹抑制即可避免一次画圈被识别成多次导航。
/// </remarks>
internal sealed class NavigationGate
{
    #region 字段

    private readonly NavigationGateOptions _options;
    private readonly object _sync = new object();
    private long _nextAllowedMs;

    #endregion

    #region 构造函数

    /// <summary>
    /// 使用指定配置创建闸门。
    /// </summary>
    /// <param name="options">时序配置。</param>
    public NavigationGate(NavigationGateOptions options)
    {
        _options = options;
    }

    #endregion

    #region 公共方法

    /// <summary>
    /// 尝试占用闸门以执行一个动作。
    /// </summary>
    /// <param name="targetWindow">资源管理器顶层窗口句柄。</param>
    /// <returns>占用成功返回 <c>true</c>；仍处于冷却中时返回 <c>false</c>，调用方应丢弃本次手势。</returns>
    public bool TryBegin(IntPtr targetWindow)
    {
        long now = Environment.TickCount64;

        lock (_sync)
        {
            if (now < _nextAllowedMs)
            {
                return false;
            }

            _nextAllowedMs = now + _options.CooldownMs;

            return true;
        }
    }

    #endregion
}
