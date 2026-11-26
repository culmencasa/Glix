using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace Glix;

/// <summary>
/// 手势引擎：串联鼠标钩子、输入编排与触发源/动作的分发，使旋转、方位热区等触发方式可统一接入。
/// 保留原有的输入解耦（钩子线程只入队）、去抖动、上下文判定与硬屏蔽时序，行为与原实现一致。
/// </summary>
internal sealed class GestureEngine : IDisposable
{
    #region 字段

    private readonly object _sync = new object();
    private readonly GestureOptions _options;
    private readonly List<IGestureTrigger> _triggers = new List<IGestureTrigger>();
    private readonly List<IGestureAction> _actions = new List<IGestureAction>();

    private readonly MouseHook _hook = new MouseHook();
    private readonly ConcurrentQueue<MouseInput> _inputQueue = new ConcurrentQueue<MouseInput>();
    private readonly ManualResetEventSlim _inputReady = new ManualResetEventSlim(false);
    private readonly Thread _processorThread;
    private volatile bool _disposed;

    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private bool _paused;
    private bool _buttonDown;
    private long _lastInsideListMs;
    private long _lastGestureMs;
    private int _lastSampleX = int.MinValue;
    private int _lastSampleY = int.MinValue;
    private IntPtr _contextWindow = IntPtr.Zero;
    private volatile bool _signalFired;

    // 鼠标主方向跟踪：用于驱动托盘图标的方向箭头指示，与手势识别相互独立。
    private int _lastDirX = int.MinValue;
    private int _lastDirY = int.MinValue;
    private ScreenDirection _currentDirection = ScreenDirection.None;
    private const int DirectionThresholdPx = 12;

    #endregion

    #region 构造函数

    public GestureEngine()
        : this(null)
    {
    }

    /// <summary>
    /// 使用指定的手势选项构造引擎；传入 null 时使用默认配置。
    /// </summary>
    /// <param name="options">手势识别与编排选项（与触发器共享同一实例以运行时调整）。</param>
    public GestureEngine(GestureOptions? options)
    {
        _options = options ?? new GestureOptions();

        _processorThread = new Thread(ProcessLoop)
        {
            IsBackground = true,
            Name = "Glix.Processor"
        };
    }

    #endregion

    #region 属性

    /// <summary>
    /// 是否暂停所有手势处理。
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
        set
        {
            lock (_sync)
            {
                _paused = value;
            }

            if (value)
            {
                ResetTrackers();
            }
        }
    }

    #endregion

    #region 事件

    /// <summary>
    /// 鼠标主移动方向变化时触发，用于驱动托盘图标的方向指示。
    /// </summary>
    public event Action<ScreenDirection>? MouseDirectionChanged;

    #endregion

    #region 公共方法

    /// <summary>
    /// 注册一个手势触发源。
    /// </summary>
    /// <param name="trigger">待接入的触发源。</param>
    public void AddTrigger(IGestureTrigger trigger)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        trigger.Signaled += OnSignaled;
        _triggers.Add(trigger);
    }

    /// <summary>
    /// 注册一个手势动作。
    /// </summary>
    /// <param name="action">待接入的动作。</param>
    public void AddAction(IGestureAction action)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _actions.Add(action);
    }

    /// <summary>
    /// 安装钩子并启动处理线程。
    /// </summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _hook.MouseInputReceived += OnMouseInput;
        _processorThread.Start();
        _hook.Start();
    }

    /// <summary>
    /// 卸载钩子、停止线程并释放所有触发源与动作。
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _hook.MouseInputReceived -= OnMouseInput;
        _inputReady.Set();
        _processorThread.Join(TimeSpan.FromSeconds(2));
        _hook.Dispose();

        foreach (IGestureTrigger trigger in _triggers)
        {
            trigger.Signaled -= OnSignaled;
            trigger.Dispose();
        }

        foreach (IGestureAction action in _actions)
        {
            if (action is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    #endregion

    #region 私有方法

    private void OnMouseInput(MouseInput input)
    {
        _inputQueue.Enqueue(input);
        _inputReady.Set();
    }

    /// <summary>
    /// 识别工作线程主循环：从队列中取出原始输入并执行命中判定与手势识别。
    /// </summary>
    private void ProcessLoop()
    {
        while (!_disposed)
        {
            _inputReady.Wait(TimeSpan.FromMilliseconds(50));
            _inputReady.Reset();

            while (_inputQueue.TryDequeue(out MouseInput input))
            {
                ProcessInput(input);
            }
        }

        while (_inputQueue.TryDequeue(out _))
        {
        }
    }

    private void ProcessInput(MouseInput input)
    {
        if (IsButtonStateMessage(input.Message))
        {
            lock (_sync)
            {
                _buttonDown = IsButtonDownMessage(input.Message);
            }

            ResetTrackers();
            return;
        }

        if (input.Message != NativeMethods.WM_MOUSEMOVE)
        {
            return;
        }

        long now = _stopwatch.ElapsedMilliseconds;

        lock (_sync)
        {
            if (_paused || _buttonDown)
            {
                ResetTrackers();
                return;
            }
        }

        // 抖动过滤：按欧氏距离判定，避免斜向微小位移因分轴判断而被误杀。
        double jitterX = input.X - (double)_lastSampleX;
        double jitterY = input.Y - (double)_lastSampleY;
        double jitterSquared = (jitterX * jitterX) + (jitterY * jitterY);

        if (jitterSquared < _options.JitterPx * _options.JitterPx)
        {
            return;
        }

        _lastSampleX = input.X;
        _lastSampleY = input.Y;

        // 方向指示：跟踪鼠标主移动方向，变化时通知外部更新托盘图标。
        UpdateDirection(input.X, input.Y);

        if (!ExplorerListHitTester.IsOverFileList(input.X, input.Y, out IntPtr explorerWindow))
        {
            long outsideMs = now - _lastInsideListMs;

            // 窗口切换、目录加载期间区域判定会瞬时断开，短暂离开不应丢弃正在进行的轨迹。
            if (outsideMs > _options.OffListGraceMs)
            {
                ResetTrackers();
            }

            _contextWindow = IntPtr.Zero;
            return;
        }

        _lastInsideListMs = now;
        _contextWindow = explorerWindow;

        // 硬屏蔽期：上次手势后短时间内完全忽略鼠标位移，杜绝尾段/手抖被当成另一次手势。
        if (now - Volatile.Read(ref _lastGestureMs) < _options.GestureSuppressionMs)
        {
            ResetTrackers();
            return;
        }

        _signalFired = false;

        foreach (IGestureTrigger trigger in _triggers)
        {
            trigger.Feed(input, now);

            if (_signalFired)
            {
                return;
            }
        }
    }

    private void UpdateDirection(int x, int y)
    {
        if (_lastDirX == int.MinValue)
        {
            _lastDirX = x;
            _lastDirY = y;
            return;
        }

        int deltaX = x - _lastDirX;
        int deltaY = y - _lastDirY;

        if (Math.Abs(deltaX) < DirectionThresholdPx && Math.Abs(deltaY) < DirectionThresholdPx)
        {
            return;
        }

        ScreenDirection direction;

        if (Math.Abs(deltaX) > Math.Abs(deltaY))
        {
            direction = deltaX > 0 ? ScreenDirection.Right : ScreenDirection.Left;
        }
        else
        {
            direction = deltaY > 0 ? ScreenDirection.Down : ScreenDirection.Up;
        }

        if (direction != _currentDirection)
        {
            _currentDirection = direction;
            MouseDirectionChanged?.Invoke(direction);
        }

        _lastDirX = x;
        _lastDirY = y;
    }

    private void OnSignaled(GestureSignal signal)
    {
        // 命中即斩断：立即写入硬屏蔽时间戳，旧轨迹已被识别器清空，绝不允许污染下一个手势。
        _signalFired = true;
        GestureSignal dispatched = signal with { ContextWindow = _contextWindow };
        Volatile.Write(ref _lastGestureMs, signal.TimestampMs);
        ResetTrackers();

        foreach (IGestureAction action in _actions)
        {
            if (action.CanHandle(dispatched))
            {
                action.Execute(dispatched);
            }
        }
    }

    private void ResetTrackers()
    {
        foreach (IGestureTrigger trigger in _triggers)
        {
            trigger.Reset();
        }
    }

    private static bool IsButtonStateMessage(int message)
    {
        return message is NativeMethods.WM_LBUTTONDOWN
            or NativeMethods.WM_LBUTTONUP
            or NativeMethods.WM_RBUTTONDOWN
            or NativeMethods.WM_RBUTTONUP
            or NativeMethods.WM_MBUTTONDOWN
            or NativeMethods.WM_MBUTTONUP
            or NativeMethods.WM_XBUTTONDOWN
            or NativeMethods.WM_XBUTTONUP;
    }

    private static bool IsButtonDownMessage(int message)
    {
        return message is NativeMethods.WM_LBUTTONDOWN
            or NativeMethods.WM_RBUTTONDOWN
            or NativeMethods.WM_MBUTTONDOWN
            or NativeMethods.WM_XBUTTONDOWN;
    }

    #endregion
}
