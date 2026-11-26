using System;
using System.Windows.Forms;

namespace Glix;

/// <summary>
/// 将顺/逆时针画圈手势映射为资源管理器的后退/前进导航，并附带音效与动作提示覆盖层。
/// 作为 <see cref="IGestureAction"/> 接入引擎，与触发源解耦。
/// </summary>
internal sealed class NavigationAction : IGestureAction, IDisposable
{
    #region 字段

    private readonly NavigationGateOptions _gateOptions;
    private readonly NavigationGate _gate;
    private readonly NavigationInvoker _navigator = new NavigationInvoker();
    private readonly SoundFeedback _sound = new SoundFeedback();
    private readonly GestureOverlay _overlay;
    private readonly object _sync = new object();

    private bool _overlayEnabled = true;
    private bool _disposed;

    #endregion

    #region 构造函数

    public NavigationAction(GestureOverlay overlay, NavigationGateOptions? gateOptions = null)
    {
        _overlay = overlay;
        _gateOptions = gateOptions ?? new NavigationGateOptions();
        _gate = new NavigationGate(_gateOptions);
    }

    #endregion

    #region 配置

    /// <summary>
    /// 是否显示动作提示覆盖层。
    /// </summary>
    public bool OverlayEnabled
    {
        get
        {
            lock (_sync)
            {
                return _overlayEnabled;
            }
        }
        set
        {
            lock (_sync)
            {
                _overlayEnabled = value;
            }

            if (!value)
            {
                _overlay.HideNow();
            }
        }
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

    #endregion

    #region 手势动作

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

        // 导航、音效与覆盖层均需在 UI 线程执行，派发到覆盖层所在的消息循环。
        try
        {
            _overlay.BeginInvoke(new Action(() => Perform(explorerWindow, signal.Kind)));
        }
        catch (InvalidOperationException)
        {
            // 程序正在退出，提示窗句柄已销毁，本次操作直接丢弃。
        }
    }

    #endregion

    #region 内部实现

    private void Perform(IntPtr explorerWindow, GestureSignalKind kind)
    {
        if (kind == GestureSignalKind.CircleClockwise)
        {
            _navigator.GoBack(explorerWindow);
            _sound.PlayBackward();
        }
        else
        {
            _navigator.GoForward(explorerWindow);
            _sound.PlayForward();
        }

        ShowOverlay(explorerWindow, kind);
    }

    private void ShowOverlay(IntPtr explorerWindow, GestureSignalKind kind)
    {
        bool enabled;

        lock (_sync)
        {
            enabled = _overlayEnabled;
        }

        if (!enabled)
        {
            return;
        }

        GestureActionKind actionKind = kind == GestureSignalKind.CircleClockwise
            ? GestureActionKind.Backward
            : GestureActionKind.Forward;

        _overlay.ShowAction(explorerWindow, actionKind);
    }

    #endregion

    #region 释放

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

    #endregion
}
