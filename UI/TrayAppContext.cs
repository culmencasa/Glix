using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Glix;

/// <summary>
/// 托盘应用程序上下文：装配手势引擎并托管托盘菜单与生命周期，不再承担具体手势识别逻辑。
/// </summary>
internal sealed class TrayAppContext : ApplicationContext
{
    #region 字段

    private readonly GestureEngine _engine;
    private readonly NavigationAction _navigation;
    private readonly TrayDirectionIndicator _directionIcons = new TrayDirectionIndicator();
    private readonly Control _marshalControl = new Control();
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _pauseItem;
    private readonly ToolStripMenuItem _soundItem;
    private readonly ToolStripMenuItem _soundSourceItem;
    private readonly ToolStripMenuItem _settingsItem;
    private readonly ToolStripMenuItem _distanceItem;
    private readonly AppSettings _settings;
    private readonly GestureOptions _gestureOptions;
    private readonly TGlickOptions _tglickOptions;
    private readonly MouseDistanceTracker _distanceTracker;
    private readonly HotZoneManager _hotZones;
    private MouseDistanceForm? _distanceForm;
    private List<ExplorerWindow>? _switcherWindows;
    private ExplorerSwitcherForm? _switcher;
    private volatile bool _switcherVisible;
    private Icon? _currentTrayIcon;
    private float _lastAngle;
    private float _pendingAngle;
    private bool _angleUpdatePending;
    private readonly System.Windows.Forms.Timer _sleepTimer = new System.Windows.Forms.Timer();
    private readonly System.Windows.Forms.Timer _trayInfoTimer = new System.Windows.Forms.Timer();
    private int _sleepFrame;

    #endregion

    #region 构造函数

    public TrayAppContext()
    {
        // 在 UI 线程创建隐藏控件，用于把鼠标方向更新可靠地封送回 UI 线程。
        _marshalControl.CreateControl();

        _navigation = new NavigationAction()
        {
            SoundEnabled = true,
            SoundMode = SoundMode.Custom
        };
        _navigation.PlayStartupSound();

        _settings = AppSettings.Load();

        _gestureOptions = new GestureOptions
        {
            MinSpanPx = _settings.MinSpanPx,
            MinTurnDegrees = _settings.MinTurnDegrees
        };

        _tglickOptions = new TGlickOptions
        {
            Enabled = _settings.TGlickEnabled,
            RequiredReversals = _settings.TGlickRequiredReversals,
            FirstLegMinPx = _settings.TGlickFirstLegPx,
            ReturnLegMinPx = _settings.TGlickReturnLegPx,
            MaxVerticalOffsetPx = _settings.TGlickMaxVerticalPx,
            MinPeakSpeedPxPerSecond = _settings.TGlickMinPeakSpeed,
            MaxDurationMs = _settings.TGlickMaxDurationMs,
            CooldownMs = _settings.TGlickCooldownMs,
            JitterDeadZonePx = _settings.TGlickJitterPx,
            DebugLogging = _settings.TGlickDebugLog
        };

        _engine = new GestureEngine(_gestureOptions);
        _engine.AddTrigger(new CircleGestureTrigger(_gestureOptions));
        _engine.AddAction(_navigation);
        _engine.AddAction(new ExplorerSwitcherAction(
            _navigation.PlaySwitchSound,
            () => ShowSwitcher(Rectangle.Empty),
            () => _switcherVisible));
        _engine.MouseAngleChanged += OnMouseAngleChanged;
        _engine.MouseButtonDown += OnMouseButtonDown;
        _engine.Start();

        _hotZones = new HotZoneManager(ShowSwitcher);
        _engine.SuppressInput = ShouldSuppressMouseInput;

        _distanceTracker = new MouseDistanceTracker();
        _engine.RawMouseMoved += _distanceTracker.OnMouseMove;

        _sleepTimer.Interval = 600;
        _sleepTimer.Tick += OnSleepTick;

        _trayInfoTimer.Interval = 10000;
        _trayInfoTimer.Tick += OnTrayInfoTick;
        _trayInfoTimer.Start();

        _currentTrayIcon = _directionIcons.CreateIcon(0f);

        _pauseItem = new ToolStripMenuItem("暂停手势") { CheckOnClick = true };
        _soundItem = new ToolStripMenuItem("提示音") { CheckOnClick = true, Checked = true };
        _soundSourceItem = new ToolStripMenuItem("使用系统导航音效") { CheckOnClick = true, Checked = false };
        _settingsItem = new ToolStripMenuItem("设置...");
        _distanceItem = new ToolStripMenuItem("鼠标行程统计...");

        _pauseItem.Click += OnPauseClicked;
        _soundItem.Click += OnSoundClicked;
        _soundSourceItem.Click += OnSoundSourceClicked;
        _settingsItem.Click += OnSettingsClicked;
        _distanceItem.Click += OnDistanceClicked;

        _notifyIcon = new NotifyIcon
        {
            Icon = _currentTrayIcon,
            Text = "Explorer 手势导航：顺时针后退 / 逆时针前进",
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };

        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        UpdateTrayText();
    }

    #endregion

    #region 释放

    /// <summary>
    /// 释放手势引擎、图标资源与托盘图标。
    /// </summary>
    /// <param name="disposing">是否释放托管资源。</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            _engine.MouseAngleChanged -= OnMouseAngleChanged;
            _engine.MouseButtonDown -= OnMouseButtonDown;
            _engine.RawMouseMoved -= _distanceTracker.OnMouseMove;
            _engine.Dispose();
            _distanceTracker.Dispose();
            _hotZones.Dispose();
            _distanceForm?.Close();
            _distanceForm?.Dispose();
            _distanceForm = null;
            _switcherVisible = false;
            if (_switcher is not null)
            {
                _switcher.Hidden -= OnSwitcherHidden;
                _switcher.Close();
                _switcher.Dispose();
                _switcher = null;
            }
            _sleepTimer.Dispose();
            _trayInfoTimer.Dispose();
            _currentTrayIcon?.Dispose();
            _directionIcons.Dispose();
            _marshalControl.Dispose();
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }

        base.Dispose(disposing);
    }

    #endregion

    #region 私有方法

    private ContextMenuStrip BuildMenu()
    {
        ToolStripMenuItem exitItem = new ToolStripMenuItem("退出");
        exitItem.Click += OnExitClicked;

        ContextMenuStrip menu = new ContextMenuStrip();
        menu.Items.Add(_pauseItem);
        menu.Items.Add(_soundItem);
        menu.Items.Add(_soundSourceItem);
        menu.Items.Add(_settingsItem);
        menu.Items.Add(_distanceItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);

        return menu;
    }

    private void OnMouseAngleChanged(float angle)
    {
        Volatile.Write(ref _pendingAngle, angle);

        if (Volatile.Read(ref _angleUpdatePending))
        {
            return;
        }

        Volatile.Write(ref _angleUpdatePending, true);

        if (_marshalControl.IsHandleCreated)
        {
            _marshalControl.BeginInvoke(new Action(ApplyPendingAngle));
        }
        else
        {
            ApplyPendingAngle();
        }
    }

    private void ApplyPendingAngle()
    {
        float angle = Volatile.Read(ref _pendingAngle);

        UpdateTrayIcon(angle);

        Volatile.Write(ref _angleUpdatePending, false);
    }

    private bool ShouldSuppressMouseInput(MouseInput input)
    {
        if (input.Message != NativeMethods.WM_LBUTTONDOWN && input.Message != NativeMethods.WM_LBUTTONUP)
        {
            return false;
        }

        return _hotZones.TryHit(new Point(input.X, input.Y), out _);
    }

    private static void ToggleTopMost(IntPtr handle)
    {
        if (!NativeMethods.IsWindow(handle))
        {
            return;
        }

        IntPtr exStyle = NativeMethods.GetWindowLongPtr(handle, NativeMethods.GWL_EXSTYLE);
        bool pinned = (exStyle.ToInt64() & NativeMethods.WS_EX_TOPMOST) != 0;
        NativeMethods.SetWindowPos(handle,
            pinned ? NativeMethods.HWND_NOTOPMOST : NativeMethods.HWND_TOPMOST,
            0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }

    private void OnMouseButtonDown(MouseInput input)
    {
        Point point = new Point(input.X, input.Y);

        if (_hotZones.TryHit(point, out HotZoneHit hit))
        {
            if (hit.Kind == HotZoneKind.Switcher)
            {
                ShowSwitcher(hit.Placement);
            }
            else
            {
                ToggleTopMost(hit.WindowHandle);
            }

            return;
        }

        if (!_switcherVisible || _switcher is null)
        {
            return;
        }

        if (_switcher.ContainsScreenPoint(point))
        {
            return;
        }

        if (_marshalControl.IsHandleCreated)
        {
            _marshalControl.BeginInvoke(new Action(HideSwitcherCore));
        }
        else
        {
            HideSwitcherCore();
        }
    }

    private void HideSwitcherCore()
    {
        if (_switcherVisible)
        {
            _switcher?.HideAll();
        }
    }

    private void ShowSwitcher(Rectangle anchor)
    {
        if (_switcherVisible)
        {
            HideSwitcher();

            return;
        }

        if (_marshalControl.IsHandleCreated)
        {
            _marshalControl.BeginInvoke(new Action<Rectangle>(ShowSwitcherCore), anchor);
        }
        else
        {
            ShowSwitcherCore(anchor);
        }
    }

    private void HideSwitcher()
    {
        if (_marshalControl.IsHandleCreated)
        {
            _marshalControl.BeginInvoke(new Action(HideSwitcherCore));
        }
        else
        {
            HideSwitcherCore();
        }
    }

    private void ShowSwitcherCore(Rectangle anchor)
    {
        if (_switcherVisible)
        {
            return;
        }

        _switcherWindows = new List<ExplorerWindow>(ExplorerWindowEnumerator.GetWindows());

        if (_switcherWindows.Count == 0)
        {
            return;
        }

        if (_switcher is null)
        {
            _switcher = new ExplorerSwitcherForm(_switcherWindows);
            _switcher.Hidden += OnSwitcherHidden;
        }
        else
        {
            _switcher.Update(_switcherWindows);
        }

        _switcher.Present(anchor);
        _switcherVisible = true;
    }

    private void OnSwitcherHidden()
    {
        _switcherVisible = false;
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (_marshalControl.IsHandleCreated)
        {
            _marshalControl.BeginInvoke(new Action(RefreshTrayIcon));
        }
        else
        {
            RefreshTrayIcon();
        }
    }

    private void RefreshTrayIcon()
    {
        if (_pauseItem.Checked)
        {
            UpdatePauseTrayIcon();
        }
        else
        {
            UpdateTrayIcon(_lastAngle);
        }
    }

    private void UpdateTrayIcon(float angle)
    {
        _lastAngle = angle;

        Icon newIcon = _directionIcons.CreateIcon(angle);
        _notifyIcon.Icon = newIcon;

        Icon? oldIcon = _currentTrayIcon;
        _currentTrayIcon = newIcon;
        oldIcon?.Dispose();
    }

    private void OnPauseClicked(object? sender, EventArgs e)
    {
        _engine.Paused = _pauseItem.Checked;

        if (_pauseItem.Checked)
        {
            _sleepFrame = 0;
            UpdatePauseTrayIcon();
            _sleepTimer.Start();
        }
        else
        {
            _sleepTimer.Stop();
            UpdateTrayIcon(0f);
        }
    }

    private void OnTrayInfoTick(object? sender, EventArgs e)
    {
        UpdateTrayText();
    }

    private void UpdateTrayText()
    {
        double total = _distanceTracker.ReadAll().Values.Sum();
        double today = _distanceTracker.TodayKm;
        _notifyIcon.Text = $"Explorer 手势导航：顺时针后退 / 逆时针前进\n今日 {today:F2} 公里  累计 {total:F2} 公里";
    }

    private void OnSleepTick(object? sender, EventArgs e)
    {
        _sleepFrame = (_sleepFrame + 1) % 3;
        UpdatePauseTrayIcon();
    }

    private void UpdatePauseTrayIcon()
    {
        Icon newIcon = _directionIcons.CreateSleepIcon(_sleepFrame);
        _notifyIcon.Icon = newIcon;

        Icon? oldIcon = _currentTrayIcon;
        _currentTrayIcon = newIcon;
        oldIcon?.Dispose();
    }

    private void OnSoundClicked(object? sender, EventArgs e)
    {
        _navigation.SoundEnabled = _soundItem.Checked;
    }

    private void OnSoundSourceClicked(object? sender, EventArgs e)
    {
        _navigation.SoundMode = _soundSourceItem.Checked ? SoundMode.System : SoundMode.Custom;
    }

    private void OnSettingsClicked(object? sender, EventArgs e)
    {
        using var form = new SettingsForm(_settings, _gestureOptions, _tglickOptions);
        form.ShowDialog();
    }

    private void OnDistanceClicked(object? sender, EventArgs e)
    {
        _distanceForm?.Close();
        _distanceForm = new MouseDistanceForm(_distanceTracker);
        _distanceForm.Show();
    }

    private void OnExitClicked(object? sender, EventArgs e)
    {
        _notifyIcon.Visible = false;
        ExitThread();
    }

    #endregion
}
