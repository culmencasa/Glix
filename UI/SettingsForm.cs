using System;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace Glix;

/// <summary>
/// 现代风格的设置窗口：常规页管理开机启动，调整页设置手势阈值，关于页展示版本与项目信息。
/// 调整页拖动滑块即实时生效（实时预览），取消时回滚到打开前的数值。
/// </summary>
internal sealed class SettingsForm : Form
{
    #region 字段

    private const string ProjectUrl = "https://github.com/culmencasa/Glix";

    private static readonly Color Brand = Color.FromArgb(45, 125, 210);
    private static readonly Color Surface = Color.FromArgb(245, 246, 248);
    private static readonly Color Card = Color.White;

    private readonly AppSettings _settings;
    private readonly GestureOptions _options;
    private readonly TGlickOptions _tglickOptions;
    private readonly TGlickOptions _originalTGlick;
    private readonly int _originalMinSpan;
    private readonly int _originalMinTurn;
    private readonly bool _originalStartupEnabled;

    private CheckBox _startupCheck = null!;
    private TrackBar _spanTrack = null!;
    private Label _spanValue = null!;
    private TrackBar _turnTrack = null!;
    private Label _turnValue = null!;
    private CheckBox _tglickCheck = null!;
    private CheckBox _tglickDebugCheck = null!;

    #endregion

    #region 构造函数

    public SettingsForm(AppSettings settings, GestureOptions options, TGlickOptions tglickOptions)
    {
        _settings = settings;
        _options = options;
        _tglickOptions = tglickOptions;
        _originalTGlick = tglickOptions.Clone();
        _originalMinSpan = options.MinSpanPx;
        _originalMinTurn = options.MinTurnDegrees;
        _originalStartupEnabled = StartupManager.IsEnabled();

        Text = "设置";
        Icon = AppIcon.Create();
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(560, 540);
        MinimumSize = new Size(520, 480);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = Surface;
        Font = new Font("Segoe UI", 10f);

        BuildLayout();
        LoadValues();
    }

    #endregion

    #region 私有方法

    private void BuildLayout()
    {


        var body = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Surface
        };
        var tabs = new TabControl
        {
            Dock = DockStyle.Fill,
            Padding = new Point(8, 4)
        };
        tabs.Controls.Add(CreateGeneralPage());
        tabs.Controls.Add(CreateTuningPage());
        _ = CreateTGlickPage();
        tabs.Controls.Add(CreateAboutPage());
        body.Controls.Add(tabs);
        Controls.Add(body);

        var footer = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 56,
            BackColor = Surface
        };
        var cancel = new Button
        {
            Text = "取消",
            Size = new Size(84, 32),
            Location = new Point(Width - 24 - 96 - 12 - 84, 12),
            DialogResult = DialogResult.Cancel
        };
        var ok = new Button
        {
            Text = "确定",
            Size = new Size(96, 32),
            Location = new Point(Width - 24 - 96, 12),
            BackColor = Brand,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            DialogResult = DialogResult.OK
        };
        ok.FlatAppearance.BorderSize = 0;
        footer.Controls.Add(cancel);
        footer.Controls.Add(ok);
        Controls.Add(footer);

        AcceptButton = ok;
        CancelButton = cancel;
        ok.Click += OnOk;
        cancel.Click += OnCancel;
    }

    private TabPage CreateGeneralPage()
    {
        var page = new TabPage("常规")
        {
            BackColor = Surface,
            Padding = new Padding(16)
        };
        var card = new Panel
        {
            Dock = DockStyle.Top,
            Height = 88,
            BackColor = Card,
            Padding = new Padding(16, 12, 16, 12)
        };
        _startupCheck = new CheckBox
        {
            Text = "开机启动",
            Font = new Font("Segoe UI", 11f, FontStyle.Bold),
            Location = new Point(16, 18),
            AutoSize = true
        };
        card.Controls.Add(_startupCheck);
        page.Controls.Add(card);

        return page;
    }

    private TabPage CreateTuningPage()
    {
        var page = new TabPage("调整")
        {
            BackColor = Surface,
            Padding = new Padding(16)
        };
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Surface,
            Padding = new Padding(0)
        };
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));

        SliderCard span = CreateCard(new SliderCardOptions(
            "转圈最小尺寸",
            "圈的直径小于该值时不会触发；调大可避免小幅晃动被误判为手势。",
            40, 360, 5));
        _spanTrack = span.Track;
        _spanValue = span.ValueLabel;
        _spanTrack.Scroll += (_, _) =>
        {
            _options.MinSpanPx = _spanTrack.Value;
            _spanValue.Text = $"{_spanTrack.Value} px";
        };

        SliderCard turn = CreateCard(new SliderCardOptions(
            "最小转角度数",
            "累计转角需达到该度数才会触发；调大可减少不完整小圈的误触发。",
            90, 360, 5));
        _turnTrack = turn.Track;
        _turnValue = turn.ValueLabel;
        _turnTrack.Scroll += (_, _) =>
        {
            _options.MinTurnDegrees = _turnTrack.Value;
            _turnValue.Text = $"{_turnTrack.Value}°";
        };

        table.Controls.Add(span.Card, 0, 0);
        table.Controls.Add(turn.Card, 0, 1);
        page.Controls.Add(table);

        return page;
    }

    private TabPage CreateTGlickPage()
    {
        var page = new TabPage("窗口切换")
        {
            BackColor = Surface,
            Padding = new Padding(12)
        };
        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true
        };
        page.Controls.Add(flow);

        var header = new Panel
        {
            Size = new Size(416, 78),
            BackColor = Card,
            Margin = new Padding(0, 0, 0, 10)
        };
        _tglickCheck = new CheckBox
        {
            Text = "启用横向 Glick 切换窗口",
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            Location = new Point(12, 12),
            AutoSize = true
        };
        _tglickDebugCheck = new CheckBox
        {
            Text = "输出调试日志",
            Font = new Font("Segoe UI", 9f),
            Location = new Point(12, 42),
            AutoSize = true
        };
        _tglickCheck.CheckedChanged += (_, _) => _tglickOptions.Enabled = _tglickCheck.Checked;
        _tglickDebugCheck.CheckedChanged += (_, _) => _tglickOptions.DebugLogging = _tglickDebugCheck.Checked;
        header.Controls.Add(_tglickCheck);
        header.Controls.Add(_tglickDebugCheck);
        flow.Controls.Add(header);

        BindSlider(flow, new TGlickSliderOptions("折返次数", "需要完成的横向折返次数，越多越不易误触。", 1, 5,
            _tglickOptions.RequiredReversals, value => $"{value} 次", value => _tglickOptions.RequiredReversals = value));
        BindSlider(flow, new TGlickSliderOptions("第一程最小位移", "冲出距离低于该值时不判定折返。", 40, 240,
            _tglickOptions.FirstLegMinPx, value => $"{value} px", value => _tglickOptions.FirstLegMinPx = value));
        BindSlider(flow, new TGlickSliderOptions("回程最小位移", "折返距离低于该值时不触发切换。", 30, 200,
            _tglickOptions.ReturnLegMinPx, value => $"{value} px", value => _tglickOptions.ReturnLegMinPx = value));
        BindSlider(flow, new TGlickSliderOptions("纵向最大偏移", "全程上下偏移超过该值即丢弃轨迹。", 10, 120,
            _tglickOptions.MaxVerticalOffsetPx, value => $"{value} px", value => _tglickOptions.MaxVerticalOffsetPx = value));
        BindSlider(flow, new TGlickSliderOptions("最小峰值速度", "慢速来回移动不会触发，调低可提升灵敏度。", 400, 3000,
            _tglickOptions.MinPeakSpeedPxPerSecond, value => $"{value} px/s", value => _tglickOptions.MinPeakSpeedPxPerSecond = value));
        BindSlider(flow, new TGlickSliderOptions("最大时长", "整套折返动作必须在该时长内完成。", 200, 600,
            _tglickOptions.MaxDurationMs, value => $"{value} ms", value => _tglickOptions.MaxDurationMs = value));
        BindSlider(flow, new TGlickSliderOptions("触发冷却", "成功后暂停识别，避免连续误触发。", 400, 1200,
            _tglickOptions.CooldownMs, value => $"{value} ms", value => _tglickOptions.CooldownMs = value));
        BindSlider(flow, new TGlickSliderOptions("抖动死区", "小于该位移的采样不参与统计。", 2, 12,
            _tglickOptions.JitterDeadZonePx, value => $"{value} px", value => _tglickOptions.JitterDeadZonePx = value));

        return page;
    }

    private static void BindSlider(FlowLayoutPanel flow, TGlickSliderOptions options)
    {
        SliderCard card = CreateCard(new SliderCardOptions(options.Title, options.Description, options.Minimum, options.Maximum, 1));
        card.Card.Dock = DockStyle.None;
        card.Card.Size = new Size(416, 108);
        card.Card.Margin = new Padding(0, 0, 0, 10);

        int clamped = Math.Clamp(options.Value, options.Minimum, options.Maximum);
        card.Track.Value = clamped;
        card.ValueLabel.Text = options.Format(clamped);
        card.Track.Scroll += (_, _) =>
        {
            options.Apply(card.Track.Value);
            card.ValueLabel.Text = options.Format(card.Track.Value);
        };

        flow.Controls.Add(card.Card);
    }

    private static TabPage CreateAboutPage()
    {
        var page = new TabPage("关于")
        {
            BackColor = Surface,
            Padding = new Padding(16)
        };
        var card = new Panel
        {
            Dock = DockStyle.Top,
            Height = 104,
            BackColor = Card,
            Padding = new Padding(16, 12, 16, 12)
        };
        card.Controls.Add(new Label
        {
            Text = "版本",
            Font = new Font("Segoe UI", 9f),
            ForeColor = Color.FromArgb(110, 115, 120),
            Location = new Point(16, 18),
            AutoSize = true
        });
        card.Controls.Add(new Label
        {
            Text = GetDisplayVersion(),
            Font = new Font("Segoe UI", 11f, FontStyle.Bold),
            Location = new Point(96, 15),
            AutoSize = true
        });
        card.Controls.Add(new Label
        {
            Text = "项目地址",
            Font = new Font("Segoe UI", 9f),
            ForeColor = Color.FromArgb(110, 115, 120),
            Location = new Point(16, 54),
            AutoSize = true
        });
        var link = new LinkLabel
        {
            Text = ProjectUrl,
            Location = new Point(96, 51),
            AutoSize = true
        };
        link.LinkClicked += OnProjectLinkClicked;
        card.Controls.Add(link);
        page.Controls.Add(card);

        return page;
    }

    private static SliderCard CreateCard(SliderCardOptions options)
    {
        var card = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Card,
            Padding = new Padding(16, 12, 16, 12),
            Margin = new Padding(0, 0, 0, 12)
        };

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Card,
            Margin = new Padding(0)
        };
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        card.Controls.Add(table);

        var titleRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0)
        };
        titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var title = new Label
        {
            Text = options.Title,
            Font = new Font("Segoe UI", 11f, FontStyle.Bold),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };
        var valueLabel = new Label
        {
            Text = "—",
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            ForeColor = Brand,
            AutoSize = true,
            Anchor = AnchorStyles.Right,
            TextAlign = ContentAlignment.MiddleRight
        };
        titleRow.Controls.Add(title, 0, 0);
        titleRow.Controls.Add(valueLabel, 1, 0);

        var description = new Label
        {
            Text = options.Description,
            Font = new Font("Segoe UI", 9f),
            ForeColor = Color.FromArgb(110, 115, 120),
            Dock = DockStyle.Fill
        };

        var track = new TrackBar
        {
            Minimum = options.Minimum,
            Maximum = options.Maximum,
            TickFrequency = options.Step * 4,
            Dock = DockStyle.Fill,
            TickStyle = TickStyle.BottomRight
        };

        table.Controls.Add(titleRow, 0, 0);
        table.Controls.Add(description, 0, 1);
        table.Controls.Add(track, 0, 2);

        return new SliderCard(card, track, valueLabel);
    }

    private static string GetDisplayVersion()
    {
        string version = typeof(SettingsForm).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "1.0.0";
        int suffix = version.IndexOf('+');

        return suffix >= 0 ? version[..suffix] : version;
    }

    private static void OnProjectLinkClicked(object? sender, LinkLabelLinkClickedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(ProjectUrl) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // 打开浏览器失败不影响设置窗口。
        }
    }

    private void LoadValues()
    {
        _startupCheck.Checked = _originalStartupEnabled;
        _spanTrack.Value = Math.Clamp(_options.MinSpanPx, _spanTrack.Minimum, _spanTrack.Maximum);
        _spanValue.Text = $"{_spanTrack.Value} px";

        _turnTrack.Value = Math.Clamp(_options.MinTurnDegrees, _turnTrack.Minimum, _turnTrack.Maximum);
        _turnValue.Text = $"{_turnTrack.Value}°";

        _tglickCheck.Checked = _tglickOptions.Enabled;
        _tglickDebugCheck.Checked = _tglickOptions.DebugLogging;
    }

    private void OnOk(object? sender, EventArgs e)
    {
        _settings.MinSpanPx = _options.MinSpanPx;
        _settings.MinTurnDegrees = _options.MinTurnDegrees;
        _settings.TGlickEnabled = _tglickOptions.Enabled;
        _settings.TGlickRequiredReversals = _tglickOptions.RequiredReversals;
        _settings.TGlickFirstLegPx = _tglickOptions.FirstLegMinPx;
        _settings.TGlickReturnLegPx = _tglickOptions.ReturnLegMinPx;
        _settings.TGlickMaxVerticalPx = _tglickOptions.MaxVerticalOffsetPx;
        _settings.TGlickMinPeakSpeed = _tglickOptions.MinPeakSpeedPxPerSecond;
        _settings.TGlickMaxDurationMs = _tglickOptions.MaxDurationMs;
        _settings.TGlickCooldownMs = _tglickOptions.CooldownMs;
        _settings.TGlickJitterPx = _tglickOptions.JitterDeadZonePx;
        _settings.TGlickDebugLog = _tglickOptions.DebugLogging;
        _settings.Save();
        StartupManager.SetEnabled(_startupCheck.Checked);
        DialogResult = DialogResult.OK;
        Close();
    }

    private void OnCancel(object? sender, EventArgs e)
    {
        // 回滚实时预览期间对选项的修改；开机启动仅在确定时写入，无需回滚。
        _options.MinSpanPx = _originalMinSpan;
        _options.MinTurnDegrees = _originalMinTurn;
        _tglickOptions.ApplyFrom(_originalTGlick);
        DialogResult = DialogResult.Cancel;
        Close();
    }

    private sealed record SliderCardOptions(string Title, string Description, int Minimum, int Maximum, int Step);

    private sealed record TGlickSliderOptions(string Title, string Description, int Minimum, int Maximum, int Value, Func<int, string> Format, Action<int> Apply);

    private sealed record SliderCard(Panel Card, TrackBar Track, Label ValueLabel);

    #endregion
}
