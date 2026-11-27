using System;
using System.IO;

namespace Glix;

/// <summary>
/// t-glick：在资源管理器文件视图内识别一次性横向折返，默认关闭。
/// </summary>
/// <remarks>
/// 风险边界：即使严格限定在文件视图内，横向微调光标、拖动列宽仍存在极低概率命中折返形态。
/// 本识别器依靠时间窗、双向位移门槛、纵向抑制、峰值速度与独立冷却压低误触，不追求 100% 零误触。
/// 关闭 <see cref="TGlickOptions.Enabled"/> 即可停止识别；整体移除本文件、<see cref="ISelfGatedTrigger"/>
/// 分支与相关设置即可撤掉该能力，不影响旋转手势。
/// </remarks>
internal sealed class TGlickTrigger : ISelfGatedTrigger
{
    #region 枚举与字段

    private enum GlickState
    {
        Idle,
        GoingFirst,
        ReturnBack
    }

    private readonly record struct Sample(long TimeMs, int X, int Y);

    private const int BufferCapacity = 96;
    private const int MessageGapTimeoutMs = 250;

    private readonly TGlickOptions _options;
    private readonly Sample[] _buffer = new Sample[BufferCapacity];
    private int _head;
    private int _count;

    private GlickState _state = GlickState.Idle;
    private int _currentDirection;
    private int _legStartX;
    private int _legDistance;
    private int _reversalCount;
    private int _lastX;
    private int _lastY;
    private int _minY = int.MaxValue;
    private int _maxY = int.MinValue;
    private double _peakSpeed;
    private long _startMs;
    private long _lastSampleMs;
    private long _cooldownUntilMs;
    private IntPtr _fileView = IntPtr.Zero;
    private bool _disposed;

    #endregion

    #region 构造函数

    /// <summary>
    /// 使用共享参数实例创建识别器，便于设置窗口实时调整。
    /// </summary>
    /// <param name="options">t-glick 参数。</param>
    public TGlickTrigger(TGlickOptions options)
    {
        _options = options;
    }

    #endregion

    #region 接口实现

    /// <inheritdoc/>
    public event Action<GestureSignal>? Signaled;

    /// <inheritdoc/>
    public void Feed(MouseInput input, long timestampMs)
    {
        // 本识别器只通过 FeedRaw 接收原始位移，避免被引擎的通用区域判定重复喂入。
    }

    /// <inheritdoc/>
    public void FeedRaw(MouseInput input, long timestampMs)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_options.Enabled || input.Message != NativeMethods.WM_MOUSEMOVE)
        {
            return;
        }

        if (timestampMs < _cooldownUntilMs)
        {
            Log("cooldown");
            Reset();

            return;
        }

        if (!ExplorerFileViewHitTester.IsOverFileView(input.X, input.Y, out IntPtr fileView))
        {
            Log("outside");
            Reset();

            return;
        }

        if (_fileView != IntPtr.Zero && _fileView != fileView)
        {
            // 视图刷新：保留区域判定，仅清空轨迹，避免上一页残留点参与计算。
            Log("view-refresh");
            ClearTrack();
        }

        _fileView = fileView;

        if (_state != GlickState.Idle && timestampMs - _lastSampleMs > MessageGapTimeoutMs)
        {
            Log("msg-gap");
            Reset();
        }

        PruneExpired(timestampMs);

        if (_count == 0)
        {
            BeginTrack(input.X, input.Y, timestampMs);
            PushSample(new Sample(timestampMs, input.X, input.Y));
            _lastSampleMs = timestampMs;

            return;
        }

        Sample previous = LastSample();
        long dt = timestampMs - previous.TimeMs;

        if (dt <= 0)
        {
            return;
        }

        int dx = input.X - previous.X;
        int dy = input.Y - previous.Y;
        int deadZone = _options.JitterDeadZonePx;

        if ((dx * dx) + (dy * dy) < deadZone * deadZone)
        {
            Log("jitter");
            _lastSampleMs = timestampMs;

            return;
        }

        double speed = Math.Sqrt((dx * dx) + (dy * dy)) / dt;

        if (speed > _peakSpeed)
        {
            _peakSpeed = speed;
        }

        _minY = Math.Min(_minY, input.Y);
        _maxY = Math.Max(_maxY, input.Y);

        if (_maxY - _minY > _options.MaxVerticalOffsetPx)
        {
            Log("vertical-reject");
            Reset();

            return;
        }

        if (timestampMs - _startMs > _options.MaxDurationMs)
        {
            Log("timeout");
            Reset();

            return;
        }

        bool fired = ProcessAxis(input.X, dx, timestampMs);

        if (fired)
        {
            return;
        }

        PushSample(new Sample(timestampMs, input.X, input.Y));
        _lastSampleMs = timestampMs;
    }

    /// <inheritdoc/>
    public void Reset()
    {
        ClearTrack();
        _fileView = IntPtr.Zero;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _disposed = true;
    }

    #endregion

    #region 私有方法

    private void BeginTrack(int x, int y, long timestampMs)
    {
        _state = GlickState.Idle;
        _currentDirection = 0;
        _legStartX = x;
        _legDistance = 0;
        _reversalCount = 0;
        _lastX = x;
        _lastY = y;
        _minY = y;
        _maxY = y;
        _peakSpeed = 0d;
        _startMs = timestampMs;
    }

    private bool ProcessAxis(int x, int dx, long timestampMs)
    {
        int step = dx > 0 ? 1 : dx < 0 ? -1 : 0;
        _lastX = x;

        if (step == 0)
        {
            return false;
        }

        if (_state == GlickState.Idle)
        {
            if (Math.Abs(dx) < _options.JitterDeadZonePx)
            {
                return false;
            }

            _currentDirection = step;
            _legStartX = x - dx;
            _legDistance = Math.Abs(dx);
            _reversalCount = 0;
            _state = GlickState.GoingFirst;
            Log($"state=GoingFirst leg={_legDistance}");

            return false;
        }

        if (step == _currentDirection)
        {
            _legDistance = Math.Abs(x - _legStartX);

            return false;
        }

        int minimumLeg = _state == GlickState.GoingFirst
            ? _options.FirstLegMinPx
            : _options.ReturnLegMinPx;

        if (_legDistance < minimumLeg)
        {
            return false;
        }

        _reversalCount++;
        Log($"reversal={_reversalCount} leg={_legDistance}");

        if (_reversalCount >= _options.RequiredReversals)
        {
            Fire(timestampMs);

            return true;
        }

        _currentDirection = step;
        _legStartX = x;
        _legDistance = 0;
        _state = GlickState.ReturnBack;

        return false;
    }

    private void Fire(long timestampMs)
    {
        double peakPxPerSecond = _peakSpeed * 1000d;

        if (peakPxPerSecond < _options.MinPeakSpeedPxPerSecond)
        {
            Log($"speed-reject {peakPxPerSecond:F0}");
            Reset();

            return;
        }

        double ratio = _legDistance / (double)_options.ReturnLegMinPx;
        double confidence = Math.Clamp(1d + (ratio - 1d) * 0.25d, 1d, 2d);

        Log($"FIRE reversals={_reversalCount} leg={_legDistance} speed={peakPxPerSecond:F0}");

        _cooldownUntilMs = timestampMs + _options.CooldownMs;

        IntPtr contextWindow = _fileView;
        GestureSignal signal = new GestureSignal(
            GestureSignalKind.TGlick,
            _lastX,
            _lastY,
            timestampMs,
            contextWindow,
            confidence);

        Reset();
        Signaled?.Invoke(signal);
    }

    private void ClearTrack()
    {
        _count = 0;
        _head = 0;
        _state = GlickState.Idle;
        _currentDirection = 0;
        _legStartX = 0;
        _legDistance = 0;
        _reversalCount = 0;
        _minY = int.MaxValue;
        _maxY = int.MinValue;
        _peakSpeed = 0d;
        _startMs = 0;
        _lastSampleMs = 0;
    }

    private void PruneExpired(long now)
    {
        int windowMs = _options.MaxDurationMs + 80;

        while (_count > 0)
        {
            int oldest = (_head - _count + BufferCapacity) % BufferCapacity;

            if (now - _buffer[oldest].TimeMs <= windowMs)
            {
                break;
            }

            _count--;
        }
    }

    private void PushSample(Sample sample)
    {
        _buffer[_head] = sample;
        _head = (_head + 1) % BufferCapacity;

        if (_count < BufferCapacity)
        {
            _count++;
        }
    }

    private Sample LastSample()
    {
        int index = (_head - 1 + BufferCapacity) % BufferCapacity;

        return _buffer[index];
    }

    private void Log(string message)
    {
        if (!_options.DebugLogging)
        {
            return;
        }

        try
        {
            string path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Glix",
                "tglick.log");
            string directory = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(directory);
            File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
        }
        catch (IOException)
        {
            // 日志写入失败不影响手势识别。
        }
        catch (UnauthorizedAccessException)
        {
            // 无写权限时静默跳过日志。
        }
    }

    #endregion
}
