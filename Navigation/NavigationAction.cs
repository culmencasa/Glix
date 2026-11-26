using System;

namespace Glix;

/// <summary>
/// 将顺/逆时针画圈手势映射为资源管理器的后退/前进导航，并附带音效反馈。
/// 作为 <see cref="IGestureAction"/> 接入引擎，与触发源解耦。
/// </summary>
internal sealed class NavigationAction : IGestureAction, IDisposable
{
    private readonly NavigationGate _gate;
    private readonly NavigationInvoker _navigator = new NavigationInvoker();
    private readonly SoundFeedback _sound = new SoundFeedback();
    private bool _disposed;

    /// <summary>
    /// 使用指定的闸门配置创建导航动作。
    /// </summary>
    /// <param name="gateOptions">时序配置；传入 null 时使用默认配置。</param>
    public NavigationAction(NavigationGateOptions? gateOptions = null)
    {
        _gate = new NavigationGate(gateOptions ?? new NavigationGateOptions());
    }

    /// <summary>
    /// 是否启用音效反馈。
    /// </summary>
    public bool SoundEnabled
    {
        get => _sound.Enabled;
        set => _sound.Enabled = value;
    }

    /// <summary>
    /// 播放程序启动节奏音。
    /// </summary>
    public void PlayStartupSound()
    {
        _sound.PlayStartupSequence();
    }

    /// <summary>
    /// 音效来源模式。
    /// </summary>
    public SoundMode SoundMode
    {
        get => _sound.Mode;
        set => _sound.Mode = value;
    }

    /// <inheritdoc/>
    public bool CanHandle(GestureSignal signal)
    {
        return signal.Kind is GestureSignalKind.CircleClockwise or GestureSignalKind.CircleCounterClockwise;
    }

    /// <inheritdoc/>
    public void Execute(GestureSignal signal)
    {
        IntPtr explorerWindow = signal.ContextWindow;

        // 确定性冷却窗口：处于冷却中时直接丢弃本次手势，绝不在后台排队等待。
        if (!_gate.TryBegin(explorerWindow))
        {
            return;
        }

        if (signal.Kind == GestureSignalKind.CircleClockwise)
        {
            _navigator.GoBack(explorerWindow);
            _sound.PlayBackward();
        }
        else
        {
            _navigator.GoForward(explorerWindow);
            _sound.PlayForward();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _sound.Dispose();
    }
}
