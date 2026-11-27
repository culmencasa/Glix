using System;

namespace Glix;

/// <summary>
/// 收到 t-glick 信号后，播放独立提示音并请求在 UI 线程弹出资源管理器切换器。
/// </summary>
internal sealed class ExplorerSwitcherAction : IGestureAction
{
    #region 字段

    private readonly Action _playFeedback;
    private readonly Action _showSwitcher;
    private readonly Func<bool> _isSwitcherVisible;

    #endregion

    #region 构造函数

    /// <summary>
    /// 使用宿主提供的反馈与显示回调构造动作。
    /// </summary>
    /// <param name="playFeedback">播放独立提示音的回调。</param>
    /// <param name="showSwitcher">线程安全的显示回调。</param>
    /// <param name="isSwitcherVisible">查询切换器当前是否显示。</param>
    public ExplorerSwitcherAction(Action playFeedback, Action showSwitcher, Func<bool> isSwitcherVisible)
    {
        _playFeedback = playFeedback;
        _showSwitcher = showSwitcher;
        _isSwitcherVisible = isSwitcherVisible;
    }

    #endregion

    #region 接口实现

    /// <inheritdoc/>
    public bool CanHandle(GestureSignal signal)
    {
        return signal.Kind == GestureSignalKind.TGlick && !_isSwitcherVisible();
    }

    /// <inheritdoc/>
    public void Execute(GestureSignal signal)
    {
        _playFeedback();
        _showSwitcher();
    }

    #endregion
}
