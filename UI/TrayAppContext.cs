using System;
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
    private readonly AppSettings _settings;
    private readonly GestureOptions _gestureOptions;
    private Icon? _currentTrayIcon;
    private float _lastAngle;
    private float _pendingAngle;
    private bool _angleUpdatePending;
    private readonly System.Windows.Forms.Timer _sleepTimer = new System.Windows.Forms.Timer();
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

        _engine = new GestureEngine(_gestureOptions);
        _engine.AddTrigger(new CircleGestureTrigger(_gestureOptions));
        _engine.AddAction(_navigation);
        _engine.MouseAngleChanged += OnMouseAngleChanged;
        _engine.Start();

        _sleepTimer.Interval = 600;
        _sleepTimer.Tick += OnSleepTick;

        _currentTrayIcon = _directionIcons.CreateIcon(0f);

        _pauseItem = new ToolStripMenuItem("暂停手势") { CheckOnClick = true };
        _soundItem = new ToolStripMenuItem("提示音") { CheckOnClick = true, Checked = true };
        _soundSourceItem = new ToolStripMenuItem("使用系统导航音效") { CheckOnClick = true, Checked = false };
        _settingsItem = new ToolStripMenuItem("设置...");

        _pauseItem.Click += OnPauseClicked;
        _soundItem.Click += OnSoundClicked;
        _soundSourceItem.Click += OnSoundSourceClicked;
        _settingsItem.Click += OnSettingsClicked;

        _notifyIcon = new NotifyIcon
        {
            Icon = _currentTrayIcon,
            Text = "Explorer 手势导航：顺时针后退 / 逆时针前进",
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };

        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
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
            _engine.Dispose();
            _sleepTimer.Dispose();
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
        using var form = new SettingsForm(_settings, _gestureOptions);
        form.ShowDialog();
    }

    private void OnExitClicked(object? sender, EventArgs e)
    {
        _notifyIcon.Visible = false;
        ExitThread();
    }

    #endregion
}
