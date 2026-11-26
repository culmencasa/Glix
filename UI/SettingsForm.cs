using System;
using System.Drawing;
using System.Windows.Forms;

namespace Glix;

/// <summary>
/// 现代风格的设置窗口：调整转圈手势的触发阈值并持久化保存。
/// 拖动滑块即实时生效（实时预览），取消时回滚到打开前的数值。
/// </summary>
internal sealed class SettingsForm : Form
{
    #region 字段

    private static readonly Color Brand = Color.FromArgb(45, 125, 210);
    private static readonly Color Surface = Color.FromArgb(245, 246, 248);
    private static readonly Color Card = Color.White;

    private readonly AppSettings _settings;
    private readonly GestureOptions _options;
    private readonly int _originalMinSpan;
    private readonly int _originalMinTurn;

    private TrackBar _spanTrack = null!;
    private Label _spanValue = null!;
    private TrackBar _turnTrack = null!;
    private Label _turnValue = null!;

    #endregion

    #region 构造函数

    public SettingsForm(AppSettings settings, GestureOptions options)
    {
        _settings = settings;
        _options = options;
        _originalMinSpan = options.MinSpanPx;
        _originalMinTurn = options.MinTurnDegrees;

        Text = "设置";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(480, 440);
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
        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 56,
            BackColor = Brand
        };
        header.Controls.Add(new Label
        {
            Text = "Explorer 手势设置",
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 14f, FontStyle.Bold),
            Location = new Point(20, 14),
            AutoSize = true
        });
        Controls.Add(header);

        var body = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Surface,
            Padding = new Padding(24, 16, 24, 16)
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

        CreateCard(
            "转圈最小尺寸",
            "圈的直径小于该值时不会触发；调大可避免小幅晃动被误判为手势。",
            40, 360, 5,
            out _spanTrack, out _spanValue);
        _spanTrack.Scroll += (_, _) =>
        {
            _options.MinSpanPx = _spanTrack.Value;
            _spanValue.Text = $"{_spanTrack.Value} px";
        };

        CreateCard(
            "最小转角度数",
            "累计转角需达到该度数才会触发；调大可减少不完整小圈的误触发。",
            90, 360, 5,
            out _turnTrack, out _turnValue);
        _turnTrack.Scroll += (_, _) =>
        {
            _options.MinTurnDegrees = _turnTrack.Value;
            _turnValue.Text = $"{_turnTrack.Value}°";
        };

        table.Controls.Add(_spanTrack.Parent!, 0, 0);
        table.Controls.Add(_turnTrack.Parent!, 0, 1);
        body.Controls.Add(table);
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

    private static void CreateCard(
        string title,
        string description,
        int min,
        int max,
        int step,
        out TrackBar track,
        out Label valueLabel)
    {
        var card = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Card,
            Padding = new Padding(16, 12, 16, 12),
            Margin = new Padding(0, 0, 0, 12)
        };

        card.Controls.Add(new Label
        {
            Text = title,
            Font = new Font("Segoe UI", 11f, FontStyle.Bold),
            Location = new Point(16, 14),
            AutoSize = true
        });

        valueLabel = new Label
        {
            Text = "—",
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            ForeColor = Brand,
            Location = new Point(356, 14),
            AutoSize = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        card.Controls.Add(valueLabel);

        card.Controls.Add(new Label
        {
            Text = description,
            Font = new Font("Segoe UI", 9f),
            ForeColor = Color.FromArgb(110, 115, 120),
            Location = new Point(16, 40),
            Size = new Size(392, 34)
        });

        track = new TrackBar
        {
            Minimum = min,
            Maximum = max,
            TickFrequency = step * 4,
            Location = new Point(16, 84),
            Width = 392,
            Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top
        };
        card.Controls.Add(track);
    }

    private void LoadValues()
    {
        _spanTrack.Value = Math.Clamp(_options.MinSpanPx, _spanTrack.Minimum, _spanTrack.Maximum);
        _spanValue.Text = $"{_spanTrack.Value} px";

        _turnTrack.Value = Math.Clamp(_options.MinTurnDegrees, _turnTrack.Minimum, _turnTrack.Maximum);
        _turnValue.Text = $"{_turnTrack.Value}°";
    }

    private void OnOk(object? sender, EventArgs e)
    {
        _settings.MinSpanPx = _options.MinSpanPx;
        _settings.MinTurnDegrees = _options.MinTurnDegrees;
        _settings.Save();
        DialogResult = DialogResult.OK;
        Close();
    }

    private void OnCancel(object? sender, EventArgs e)
    {
        // 回滚实时预览期间对选项的修改。
        _options.MinSpanPx = _originalMinSpan;
        _options.MinTurnDegrees = _originalMinTurn;
        DialogResult = DialogResult.Cancel;
        Close();
    }

    #endregion
}
