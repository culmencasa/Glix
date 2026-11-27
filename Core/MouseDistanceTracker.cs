using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace Glix;

/// <summary>
/// 按天累计鼠标移动行程并换算为公里，同时维护本次会话的速度与时长统计。
/// </summary>
/// <remarks>
/// 换算使用系统 DPI：1 英寸 = 0.0254 米，像素按 DPI 折算为英寸。
/// 处理线程只做数值累加，落盘由定时器完成，不阻塞鼠标钩子。
/// </remarks>
internal sealed class MouseDistanceTracker : IDisposable
{
    #region 字段与常量

    private const double MetersPerInch = 0.0254d;
    private const double MinSegmentPx = 2d;
    private const double MaxSegmentPx = 2000d;
    private const int SpeedWindowMs = 400;
    private const int MinSpeedSpanMs = 60;
    private const double MaxIntensitySpeed = 1.6d;

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Glix",
        "mouse-distance.json");

    private readonly object _sync = new object();
    private readonly Dictionary<string, double> _days = new Dictionary<string, double>();
    private readonly double _metersPerPixel;
    private readonly double _kilometersPerPixel;
    private readonly System.Threading.Timer _flushTimer;
    private readonly Queue<SpeedSample> _speedSamples = new Queue<SpeedSample>();

    private string _todayKey;
    private double _todayPixels;
    private int _lastX = int.MinValue;
    private int _lastY = int.MinValue;
    private long _lastTickMs;
    private long _sessionActiveMs;
    private double _sessionMeters;
    private double _peakSpeed;
    private bool _paused;
    private bool _disposed;

    #endregion

    #region 构造函数

    /// <summary>
    /// 读取已有记录并启动定时落盘。
    /// </summary>
    public MouseDistanceTracker()
    {
        double dpi = NativeMethods.GetDpiForSystem();

        if (dpi < 48)
        {
            dpi = 96;
        }

        _metersPerPixel = MetersPerInch / dpi;
        _kilometersPerPixel = _metersPerPixel / 1000d;
        _todayKey = TodayKey;
        _lastTickMs = Environment.TickCount64;

        Load();

        _flushTimer = new System.Threading.Timer(_ => Flush(), null, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15));
    }

    #endregion

    #region 属性

    /// <summary>
    /// 今日行程（公里）。
    /// </summary>
    public double TodayKm
    {
        get
        {
            lock (_sync)
            {
                return _todayPixels * _kilometersPerPixel;
            }
        }
    }

    /// <summary>
    /// 当前是否暂停统计。
    /// </summary>
    public bool Paused
    {
        get
        {
            lock (_sync)
            {
                return _paused;
            }
        }
    }

    /// <summary>
    /// 累计总行程（公里）。
    /// </summary>
    public double TotalKm
    {
        get
        {
            lock (_sync)
            {
                return ComputeTotalKm();
            }
        }
    }

    #endregion

    #region 公共方法

    /// <summary>
    /// 接收一帧原始鼠标位移。
    /// </summary>
    /// <param name="input">鼠标输入事件。</param>
    public void OnMouseMove(MouseInput input)
    {
        if (input.Message != NativeMethods.WM_MOUSEMOVE)
        {
            return;
        }

        lock (_sync)
        {
            long now = Environment.TickCount64;
            UpdateActiveTime(now);

            if (_lastX != int.MinValue && !_paused)
            {
                double deltaX = input.X - (double)_lastX;
                double deltaY = input.Y - (double)_lastY;
                double distance = Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));

                if (distance >= MinSegmentPx && distance <= MaxSegmentPx)
                {
                    double meters = distance * _metersPerPixel;
                    _todayPixels += distance;
                    _sessionMeters += meters;

                    _speedSamples.Enqueue(new SpeedSample(now, meters));
                    double speed = ComputeCurrentSpeed(now);

                    if (speed > _peakSpeed)
                    {
                        _peakSpeed = speed;
                    }
                }
            }

            _lastX = input.X;
            _lastY = input.Y;

            string key = TodayKey;

            if (!string.Equals(key, _todayKey, StringComparison.Ordinal))
            {
                PersistToday();
                _todayKey = key;
                _todayPixels = 0d;
            }
        }
    }

    /// <summary>
    /// 设置暂停状态。
    /// </summary>
    /// <param name="paused">是否暂停。</param>
    public void SetPaused(bool paused)
    {
        lock (_sync)
        {
            if (_paused == paused)
            {
                return;
            }

            UpdateActiveTime(Environment.TickCount64);
            _paused = paused;

            if (paused)
            {
                _speedSamples.Clear();
            }
        }
    }

    /// <summary>
    /// 重置本次会话统计。
    /// </summary>
    public void ResetSession()
    {
        lock (_sync)
        {
            _sessionMeters = 0d;
            _sessionActiveMs = 0;
            _peakSpeed = 0d;
            _speedSamples.Clear();
            _lastTickMs = Environment.TickCount64;
        }
    }

    /// <summary>
    /// 读取仪表盘所需的当前快照。
    /// </summary>
    /// <returns>会话统计快照。</returns>
    public TrackerSnapshot GetSnapshot()
    {
        lock (_sync)
        {
            long now = Environment.TickCount64;
            UpdateActiveTime(now);

            double speed = _paused ? 0d : ComputeCurrentSpeed(now);
            int intensity = (int)Math.Min(100d, speed / MaxIntensitySpeed * 100d);

            return new TrackerSnapshot(
                _todayPixels * _kilometersPerPixel,
                ComputeTotalKm(),
                _sessionMeters,
                TimeSpan.FromMilliseconds(_sessionActiveMs),
                speed,
                _peakSpeed,
                intensity,
                _paused);
        }
    }

    /// <summary>
    /// 读取全部历史记录（公里），返回副本供界面使用。
    /// </summary>
    /// <returns>按日期索引的行程字典。</returns>
    public IReadOnlyDictionary<string, double> ReadAll()
    {
        lock (_sync)
        {
            return new Dictionary<string, double>(_days);
        }
    }

    /// <summary>
    /// 将当前累计值写入文件。
    /// </summary>
    public void Flush()
    {
        if (_disposed)
        {
            return;
        }

        lock (_sync)
        {
            PersistToday();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _flushTimer.Dispose();
        Flush();
        _disposed = true;
    }

    #endregion

    #region 私有方法

    private static string TodayKey
    {
        get
        {
            return DateTime.Now.ToString("yyyy-MM-dd");
        }
    }

    private void UpdateActiveTime(long now)
    {
        if (!_paused && _lastTickMs > 0)
        {
            long delta = now - _lastTickMs;

            if (delta > 0 && delta < 60000)
            {
                _sessionActiveMs += delta;
            }
        }

        _lastTickMs = now;
    }

    private double ComputeTotalKm()
    {
        double total = 0d;

        foreach (KeyValuePair<string, double> entry in _days)
        {
            total += entry.Value;
        }

        _days.TryGetValue(_todayKey, out double storedToday);
        total += (_todayPixels * _kilometersPerPixel) - storedToday;

        return total;
    }

    private double ComputeCurrentSpeed(long now)
    {
        while (_speedSamples.Count > 0 && now - _speedSamples.Peek().TimestampMs > SpeedWindowMs)
        {
            _speedSamples.Dequeue();
        }

        if (_speedSamples.Count < 2)
        {
            return 0d;
        }

        double totalMeters = 0d;

        foreach (SpeedSample sample in _speedSamples)
        {
            totalMeters += sample.Meters;
        }

        long spanMs = now - _speedSamples.Peek().TimestampMs;

        if (spanMs < MinSpeedSpanMs)
        {
            return 0d;
        }

        return totalMeters / (spanMs / 1000d);
    }

    private void PersistToday()
    {
        _days[_todayKey] = _todayPixels * _kilometersPerPixel;

        try
        {
            string? directory = Path.GetDirectoryName(FilePath);

            if (directory is not null)
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(FilePath, JsonSerializer.Serialize(new Store { Days = _days }));
        }
        catch (IOException)
        {
            // 写入失败不影响统计。
        }
        catch (UnauthorizedAccessException)
        {
            // 无写权限时静默跳过。
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return;
            }

            Store? store = JsonSerializer.Deserialize<Store>(File.ReadAllText(FilePath));

            if (store?.Days is null)
            {
                return;
            }

            foreach (KeyValuePair<string, double> entry in store.Days)
            {
                _days[entry.Key] = entry.Value;
            }

            if (_days.TryGetValue(_todayKey, out double todayKm))
            {
                _todayPixels = todayKm / _kilometersPerPixel;
            }
        }
        catch (Exception)
        {
            // 读取失败则从零开始。
        }
    }

    #endregion

    #region 嵌套类型

    private readonly record struct SpeedSample(long TimestampMs, double Meters);

    private sealed class Store
    {
        public Dictionary<string, double> Days { get; set; } = new Dictionary<string, double>();
    }

    #endregion
}

/// <summary>
/// 本次会话的统计快照。
/// </summary>
internal readonly record struct TrackerSnapshot(
    double TodayKm,
    double TotalKm,
    double SessionMeters,
    TimeSpan SessionElapsed,
    double CurrentSpeed,
    double PeakSpeed,
    int Intensity,
    bool Paused);
