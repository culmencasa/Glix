using System;

namespace Glix;

/// <summary>
/// 基于 <see cref="CircleGestureTracker"/> 的旋转手势触发源，将顺/逆时针画圈转换为统一的 <see cref="GestureSignal"/>。
/// 仅负责几何识别，是否处于目标窗口、冷却等编排逻辑交由 <see cref="GestureEngine"/> 与动作处理。
/// </summary>
internal sealed class CircleGestureTrigger : IGestureTrigger
{
    #region 字段

    private readonly GestureOptions _options;
    private readonly CircleGestureTracker _tracker;
    private bool _disposed;

    #endregion

    #region 构造函数

    public CircleGestureTrigger()
        : this(new GestureOptions())
    {
    }

    public CircleGestureTrigger(GestureOptions options)
    {
        _options = options;
        _tracker = new CircleGestureTracker(options);
    }

    #endregion

    #region 接口实现

    /// <inheritdoc/>
    public event Action<GestureSignal>? Signaled;

    /// <inheritdoc/>
    public void Feed(MouseInput input, long timestampMs)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (input.Message != NativeMethods.WM_MOUSEMOVE)
        {
            return;
        }

        CircleDirection direction = _tracker.TryDetect(input.X, input.Y, timestampMs);

        if (direction == CircleDirection.None)
        {
            return;
        }

        GestureSignalKind kind = direction == CircleDirection.Clockwise
            ? GestureSignalKind.CircleClockwise
            : GestureSignalKind.CircleCounterClockwise;

        Signaled?.Invoke(new GestureSignal(kind, input.X, input.Y, timestampMs, IntPtr.Zero, 1d));
    }

    /// <inheritdoc/>
    public void Reset()
    {
        _tracker.Reset();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _disposed = true;
    }

    #endregion
}
