using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using System.Windows.Forms;

namespace Glix;

/// <summary>
/// 鼠标里程仪：顶部为实时仪表盘，下方为按日、按月、按年图表。
/// </summary>
internal sealed class MouseDistanceForm : Form
{
    #region 字段与常量

    private static readonly Color Accent = Color.FromArgb(0xE7, 0x1F, 0x7A);
    private static readonly Color ArcBase = Color.FromArgb(0x1A, 0x16, 0x2E);
    private static readonly Color ArcInner = Color.FromArgb(0x5B, 0x27, 0x8E);
    private static readonly Color Surface = Color.FromArgb(16, 12, 26);
    private static readonly Color GridColor = Color.FromArgb(0x2A, 0x1F, 0x3D);
    private static readonly Color MutedText = Color.FromArgb(110, 100, 125);
    private static readonly Color BrightText = Color.FromArgb(200, 180, 195);

    private static readonly Color[] GradientColors =
    {
        Accent,
        Color.FromArgb(0xB3, 0x18, 0x61),
        ArcInner
    };

    private static readonly Font SummaryFont = new Font("Segoe UI", 12f);
    private static readonly Font ButtonFont = new Font("Segoe UI", 10f);
    private static readonly Font AxisFont = new Font("Segoe UI", 9f);
    private static readonly Font ChartValueFont = new Font("Segoe UI", 10f, FontStyle.Bold);

    private readonly MouseDistanceTracker _tracker;
    private readonly DistanceChart _chart = new DistanceChart();
    private readonly DashboardPanel _dashboard;
    private readonly ChartHeaderPanel _chartHeader;
    private readonly System.Windows.Forms.Timer _refreshTimer = new System.Windows.Forms.Timer();
    private ViewMode _mode = ViewMode.Day;

    #endregion

    #region 构造函数

    /// <summary>
    /// 使用指定统计器创建界面。
    /// </summary>
    /// <param name="tracker">鼠标行程统计器。</param>
    public MouseDistanceForm(MouseDistanceTracker tracker)
    {
        _tracker = tracker;
        _dashboard = new DashboardPanel(tracker);
        _chartHeader = new ChartHeaderPanel(SetMode);

        Text = "鼠标里程仪";
        Icon = AppIcon.Create();
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1100, 760);
        BackColor = Surface;
        Font = new Font("Segoe UI", 9f);
        AutoScaleMode = AutoScaleMode.Dpi;
        SetStyle(ControlStyles.ResizeRedraw, true);
        MinimizeBox = false;
        MaximizeBox = false;

        _dashboard.Dock = DockStyle.Top;
        _dashboard.Height = 420;

        _chartHeader.Dock = DockStyle.Top;
        _chartHeader.Height = 48;
        _chartHeader.BackColor = Surface;

        _chart.Dock = DockStyle.Fill;

        Controls.Add(_chart);
        Controls.Add(_chartHeader);
        Controls.Add(_dashboard);

        _refreshTimer.Interval = 100;
        _refreshTimer.Tick += OnRefreshTick;
        _refreshTimer.Start();

        Render();
    }

    #endregion

    #region 私有方法

    private void SetMode(ViewMode mode)
    {
        _mode = mode;
        Render();
    }

    private void Render()
    {
        _chartHeader.SelectedMode = _mode;

        IReadOnlyDictionary<string, double> data = _tracker.ReadAll();

        List<ChartItem> items = _mode switch
        {
            ViewMode.Month => BuildMonths(data),
            ViewMode.Year => BuildYears(data),
            _ => BuildDays(data)
        };

        _chart.Items = items;
        UpdateReadouts();
    }

    private static float GetEndFade(float ratio)
    {
        const float fadeRange = 0.12f;

        if (ratio < fadeRange)
        {
            return ratio / fadeRange;
        }

        if (ratio > 1f - fadeRange)
        {
            return (1f - ratio) / fadeRange;
        }

        return 1f;
    }

    private void OnRefreshTick(object? sender, EventArgs e)
    {
        UpdateReadouts();
        _dashboard.UpdateSnapshot(_tracker.GetSnapshot());
    }

    private void UpdateReadouts()
    {
        double total = _tracker.ReadAll().Values.Sum();
        double today = _tracker.TodayKm;
        _chartHeader.Summary = $"累计 {total:F2} 公里    今日 {today:F2} 公里";
    }

    private static List<ChartItem> BuildDays(IReadOnlyDictionary<string, double> data)
    {
        List<ChartItem> items = new List<ChartItem>();
        DateTime today = DateTime.Today;

        for (int offset = 29; offset >= 0; offset--)
        {
            DateTime date = today.AddDays(-offset);
            string key = date.ToString("yyyy-MM-dd");
            data.TryGetValue(key, out double value);
            items.Add(new ChartItem(date.ToString("MM-dd"), value));
        }

        return items;
    }

    private static List<ChartItem> BuildMonths(IReadOnlyDictionary<string, double> data)
    {
        List<ChartItem> items = new List<ChartItem>();
        DateTime today = DateTime.Today;

        for (int offset = 11; offset >= 0; offset--)
        {
            DateTime date = new DateTime(today.Year, today.Month, 1).AddMonths(-offset);
            string prefix = date.ToString("yyyy-MM");
            double value = data.Where(entry => entry.Key.StartsWith(prefix, StringComparison.Ordinal)).Sum(entry => entry.Value);
            items.Add(new ChartItem($"{date.Month}月", value));
        }

        return items;
    }

    private static List<ChartItem> BuildYears(IReadOnlyDictionary<string, double> data)
    {
        return data
            .GroupBy(entry => entry.Key.Substring(0, 4))
            .OrderBy(group => group.Key)
            .Select(group => new ChartItem(group.Key, group.Sum(entry => entry.Value)))
            .ToList();
    }

    #endregion

    #region 释放

    /// <summary>
    /// 停止实时刷新定时器。
    /// </summary>
    /// <param name="disposing">是否释放托管资源。</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _refreshTimer.Dispose();
        }

        base.Dispose(disposing);
    }

    #endregion

    #region 嵌套类型

    private enum ViewMode
    {
        Day,
        Month,
        Year
    }

    private sealed class ChartHeaderPanel : Panel
    {
        private readonly Action<ViewMode> _modeChanged;
        private RectangleF[] _buttonBounds = Array.Empty<RectangleF>();
        private int _hoverIndex = -1;
        private ViewMode _selectedMode = ViewMode.Day;
        private string _summary = string.Empty;

        public ChartHeaderPanel(Action<ViewMode> modeChanged)
        {
            _modeChanged = modeChanged;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            MouseMove += HandleMouseMove;
            MouseLeave += HandleMouseLeave;
            MouseClick += HandleMouseClick;
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public ViewMode SelectedMode
        {
            get
            {
                return _selectedMode;
            }
            set
            {
                _selectedMode = value;
                Invalidate();
            }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string Summary
        {
            get
            {
                return _summary;
            }
            set
            {
                _summary = value;
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = TextRenderingHint.AntiAlias;

            using (Brush background = new SolidBrush(Surface))
            {
                graphics.FillRectangle(background, ClientRectangle);
            }

            float scale = DeviceDpi / 96f;
            int buttonWidth = (int)(72 * scale);
            int buttonHeight = (int)(30 * scale);
            int gap = (int)(8 * scale);
            int startX = Width - (int)(16 * scale) - (buttonWidth * 3 + gap * 2);
            int y = (Height - buttonHeight) / 2;

            string[] labels = { "按日", "按月", "按年" };
            ViewMode[] modes = { ViewMode.Day, ViewMode.Month, ViewMode.Year };
            _buttonBounds = new RectangleF[3];

            for (int index = 0; index < labels.Length; index++)
            {
                RectangleF bounds = new RectangleF(startX + (index * (buttonWidth + gap)), y, buttonWidth, buttonHeight);
                _buttonBounds[index] = bounds;
                bool selected = modes[index] == _selectedMode;
                bool hover = index == _hoverIndex;
                Color background = selected ? Accent : hover ? Color.FromArgb(45, 32, 62) : Color.FromArgb(30, 22, 46);
                Color foreground = selected ? Color.White : hover ? BrightText : MutedText;

                using (GraphicsPath path = CreateRoundedPath(bounds, 6f * scale))
                using (Brush brush = new SolidBrush(background))
                {
                    graphics.FillPath(brush, path);
                }

                using (Brush textBrush = new SolidBrush(foreground))
                {
                    StringFormat center = new StringFormat
                    {
                        Alignment = StringAlignment.Center,
                        LineAlignment = StringAlignment.Center
                    };
                    graphics.DrawString(labels[index], ButtonFont, textBrush, bounds, center);
                }
            }

            using (Brush summaryBrush = new SolidBrush(BrightText))
            {
                StringFormat middle = new StringFormat { LineAlignment = StringAlignment.Center };
                graphics.DrawString(_summary, SummaryFont, summaryBrush, new RectangleF((int)(16 * scale), 0, startX - (int)(32 * scale), Height), middle);
            }

            using (Pen divider = new Pen(ArcInner))
            {
                graphics.DrawLine(divider, 0, Height - 1, Width, Height - 1);
            }
        }

        private static GraphicsPath CreateRoundedPath(RectangleF rect, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            path.AddArc(rect.X, rect.Y, radius * 2, radius * 2, 180, 90);
            path.AddArc(rect.Right - (radius * 2), rect.Y, radius * 2, radius * 2, 270, 90);
            path.AddArc(rect.Right - (radius * 2), rect.Bottom - (radius * 2), radius * 2, radius * 2, 0, 90);
            path.AddArc(rect.X, rect.Bottom - (radius * 2), radius * 2, radius * 2, 90, 90);
            path.CloseFigure();

            return path;
        }

        private void HandleMouseMove(object? sender, MouseEventArgs e)
        {
            int index = -1;

            for (int i = 0; i < _buttonBounds.Length; i++)
            {
                if (_buttonBounds[i].Contains(e.Location))
                {
                    index = i;
                    break;
                }
            }

            if (index != _hoverIndex)
            {
                _hoverIndex = index;
                Cursor = index >= 0 ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
        }

        private void HandleMouseLeave(object? sender, EventArgs e)
        {
            _hoverIndex = -1;
            Cursor = Cursors.Default;
            Invalidate();
        }

        private void HandleMouseClick(object? sender, MouseEventArgs e)
        {
            for (int index = 0; index < _buttonBounds.Length; index++)
            {
                if (_buttonBounds[index].Contains(e.Location))
                {
                    ViewMode[] modes = { ViewMode.Day, ViewMode.Month, ViewMode.Year };
                    _modeChanged(modes[index]);
                    break;
                }
            }
        }
    }

    private sealed class DashboardPanel : Panel
    {
        private const int MaxSpeedSamples = 60;
        private const float ArcStartAngle = 135f;
        private const float ArcSweepAngle = 270f;

        private readonly Font _captionFont = new Font("Segoe UI", 10f);
        private readonly Font _valueFont = new Font("Segoe UI", 15f, FontStyle.Bold);
        private readonly Font _mileageFont = CreateMileageFont();
        private readonly Font _unitFont = new Font("Segoe UI", 12f);
        private readonly Font _statusFont = new Font("Segoe UI", 16f, FontStyle.Bold);
        private readonly Font _badgeFont = new Font("Segoe UI", 17f, FontStyle.Bold);
        private readonly Font _noteFont = new Font("Segoe UI", 9f);

        private readonly MouseDistanceTracker _tracker;
        private readonly Queue<double> _speedHistory = new Queue<double>();
        private TrackerSnapshot _snapshot;
        private bool _showTotal;
        private RectangleF _centerBounds;

        public DashboardPanel(MouseDistanceTracker tracker)
        {
            _tracker = tracker;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            MouseMove += HandleMouseMove;
            MouseLeave += HandleMouseLeave;
            MouseClick += HandleMouseClick;
        }

        public void UpdateSnapshot(TrackerSnapshot snapshot)
        {
            _snapshot = snapshot;
            _speedHistory.Enqueue(snapshot.CurrentSpeed);

            while (_speedHistory.Count > MaxSpeedSamples)
            {
                _speedHistory.Dequeue();
            }

            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = TextRenderingHint.AntiAlias;

            using (Brush background = new SolidBrush(Color.FromArgb(16, 12, 26)))
            {
                graphics.FillRectangle(background, ClientRectangle);
            }

            float scale = DeviceDpi / 96f;
            int centerX = Width / 2;
            int thickness = (int)(14 * scale);
            int frameOffset = (thickness / 2) + (int)(12 * scale);
            int edgePadding = (int)(24 * scale);
            int maxRadius = ((Height / 2) - edgePadding) - frameOffset;
            int radius = Math.Max((int)(60 * scale), Math.Min((int)(148 * scale), maxRadius));
            int centerY = Height / 2;

            DrawMetalFrame(graphics, centerX, centerY, radius, thickness, scale);
            DrawRing(graphics, centerX, centerY, radius, thickness);
            DrawInnerEdge(graphics, centerX, centerY, radius, thickness, scale);
            DrawTickMarks(graphics, centerX, centerY, radius, thickness, scale);
            DrawCenter(graphics, centerX, centerY);
            DrawLeftStats(graphics, centerY, scale);
            DrawRightStats(graphics, centerY, scale);
        }

        private void DrawMetalFrame(Graphics graphics, int centerX, int centerY, int radius, int thickness, float scale)
        {
            float frameThickness = 11 * scale;
            float frameRadius = radius + (thickness / 2f) + (12 * scale);
            RectangleF rect = new RectangleF(centerX - frameRadius, centerY - frameRadius, frameRadius * 2, frameRadius * 2);

            const int segments = 120;
            float step = ArcSweepAngle / segments;

            for (int index = 0; index < segments; index++)
            {
                float angle = ArcStartAngle + (index * step);
                int alpha = (int)(200d * GetEndFactor(angle));

                using (Pen pen = new Pen(Color.FromArgb(alpha, 10, 8, 18), frameThickness + (4 * scale)))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    graphics.DrawArc(pen, rect, angle, step + 0.6f);
                }
            }

            for (int index = 0; index < segments; index++)
            {
                float angle = ArcStartAngle + (index * step);
                double radians = angle * Math.PI / 180d;
                double topFactor = (1d - Math.Sin(radians)) / 2d;
                int value = (int)(40d + (140d * topFactor));
                int alpha = (int)(255d * GetEndFactor(angle));
                Color color = Color.FromArgb(alpha, value, value, Math.Min(255, value + 10));

                using (Pen pen = new Pen(color, frameThickness))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    graphics.DrawArc(pen, rect, angle, step + 0.6f);
                }
            }

            float innerRadius = frameRadius - (frameThickness / 2f) + scale;
            RectangleF innerRect = new RectangleF(centerX - innerRadius, centerY - innerRadius, innerRadius * 2, innerRadius * 2);

            for (int index = 0; index < segments; index++)
            {
                float angle = ArcStartAngle + (index * step);
                double radians = angle * Math.PI / 180d;
                double topFactor = (1d - Math.Sin(radians)) / 2d;
                int value = (int)(180d + (75d * topFactor));
                int alpha = (int)(255d * GetEndFactor(angle));
                Color color = Color.FromArgb(alpha, value, value, Math.Min(255, value + 5));

                using (Pen pen = new Pen(color, Math.Max(1f, 1.2f * scale)))
                {
                    graphics.DrawArc(pen, innerRect, angle, step + 0.6f);
                }
            }
        }

        private static float GetEndFactor(float angle)
        {
            float ratio = (angle - ArcStartAngle) / ArcSweepAngle;
            const float fadeRange = 0.12f;

            if (ratio < fadeRange)
            {
                return ratio / fadeRange;
            }

            if (ratio > 1f - fadeRange)
            {
                return (1f - ratio) / fadeRange;
            }

            return 1f;
        }

        private void DrawRing(Graphics graphics, int centerX, int centerY, int radius, int thickness)
        {
            RectangleF rect = new RectangleF(centerX - radius, centerY - radius, radius * 2, radius * 2);
            float progress = Math.Max(0f, Math.Min(1f, _snapshot.Intensity / 100f));
            const int totalSegments = 120;
            float step = ArcSweepAngle / totalSegments;

            for (int index = 0; index < totalSegments; index++)
            {
                float angle = ArcStartAngle + (index * step);
                int alpha = (int)(255d * GetEndFactor(angle));

                using (Pen pen = new Pen(Color.FromArgb(alpha, ArcBase), thickness))
                {
                    pen.StartCap = LineCap.Flat;
                    pen.EndCap = LineCap.Flat;
                    graphics.DrawArc(pen, rect, angle, step + 0.6f);
                }
            }

            if (progress > 0.005f)
            {
                int filledSegments = Math.Max(2, (int)(totalSegments * progress));
                float filledSweep = ArcSweepAngle * progress;
                float filledStep = filledSweep / filledSegments;

                for (int index = 0; index < filledSegments; index++)
                {
                    float angle = ArcStartAngle + (index * filledStep);
                    float ratio = (index * filledStep) / ArcSweepAngle;
                    Color baseColor = InterpolateColor(GradientColors, ratio);
                    int alpha = (int)(255d * GetEndFactor(angle));

                    using (Pen pen = new Pen(Color.FromArgb(alpha, baseColor), thickness))
                    {
                        pen.StartCap = LineCap.Flat;
                        pen.EndCap = LineCap.Flat;
                        graphics.DrawArc(pen, rect, angle, filledStep + 0.6f);
                    }
                }
            }
        }

        private void DrawInnerEdge(Graphics graphics, int centerX, int centerY, int radius, int thickness, float scale)
        {
            float innerRadius = radius - (thickness / 2f) - (3 * scale);
            RectangleF rect = new RectangleF(centerX - innerRadius, centerY - innerRadius, innerRadius * 2, innerRadius * 2);
            const int segments = 120;
            float step = ArcSweepAngle / segments;

            for (int index = 0; index < segments; index++)
            {
                float angle = ArcStartAngle + (index * step);
                int alpha = (int)(255d * GetEndFactor(angle));

                using (Pen pen = new Pen(Color.FromArgb(alpha, ArcInner), 1.5f))
                {
                    graphics.DrawArc(pen, rect, angle, step + 0.6f);
                }
            }
        }

        private void DrawTickMarks(Graphics graphics, int centerX, int centerY, int radius, int thickness, float scale)
        {
            const int tickCount = 20;
            int activeTicks = (int)(_snapshot.Intensity / 5f);
            float innerEdge = radius - (thickness / 2f) - (5 * scale);

            for (int index = 0; index <= tickCount; index++)
            {
                float angle = ArcStartAngle + (ArcSweepAngle * index / tickCount);
                bool major = index % 5 == 0;
                float outer = innerEdge - (2 * scale);
                float inner = outer - ((major ? 12 : 8) * scale);
                double radians = angle * Math.PI / 180d;
                float cosine = (float)Math.Cos(radians);
                float sine = (float)Math.Sin(radians);
                PointF start = new PointF(centerX + (cosine * inner), centerY + (sine * inner));
                PointF end = new PointF(centerX + (cosine * outer), centerY + (sine * outer));
                Color color = index < activeTicks ? Accent : Color.FromArgb(70, 60, 90);

                using (Pen pen = new Pen(color, major ? 2f : 1f))
                {
                    graphics.DrawLine(pen, start, end);
                }
            }
        }

        private void DrawCenter(Graphics graphics, int centerX, int centerY)
        {
            StringFormat center = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };

            string caption = _showTotal ? "总里程" : "本次里程";

            _centerBounds = new RectangleF(centerX - 195, centerY - 104, 390, 190);

            using (Brush captionBrush = new SolidBrush(Color.FromArgb(150, 140, 165)))
            {
                graphics.DrawString(caption, _captionFont, captionBrush, new RectangleF(centerX - 120, centerY - 100, 240, 20), center);
            }

            double meters = _showTotal ? _snapshot.TotalKm * 1000d : _snapshot.SessionMeters;
            string digits = meters.ToString("F2");

            using (Brush digitBrush = new SolidBrush(Color.White))
            {
                DrawPathText(graphics, digits, _mileageFont, digitBrush, new RectangleF(centerX - 200, centerY - 62, 400, 92), center);
            }

            using (Brush unitBrush = new SolidBrush(Color.FromArgb(115, 110, 130)))
            {
                graphics.DrawString("METER", _unitFont, unitBrush, new RectangleF(centerX - 70, centerY + 34, 140, 24), center);
            }
        }

        private static Font CreateMileageFont()
        {
            using (InstalledFontCollection fonts = new InstalledFontCollection())
            {
                HashSet<string> installed = new HashSet<string>(
                    fonts.Families.Select(family => family.Name),
                    StringComparer.OrdinalIgnoreCase);

                if (installed.Contains("Bahnschrift"))
                {
                    return new Font("Bahnschrift", 64f, FontStyle.Regular);
                }
            }

            return new Font("Segoe UI", 64f, FontStyle.Bold);
        }

        private static void DrawPathText(Graphics graphics, string text, Font font, Brush brush, RectangleF bounds, StringFormat format)
        {
            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddString(text, font.FontFamily, (int)font.Style, font.SizeInPoints, bounds, format);
                graphics.FillPath(brush, path);
            }
        }

        private void DrawLeftStats(Graphics graphics, int centerY, float scale)
        {
            int x = (int)(24 * scale);
            int y = centerY - (int)(32 * scale);
            int width = (int)(230 * scale);

            using (Brush muted = new SolidBrush(Color.FromArgb(120, 124, 128)))
            {
                StringFormat center = new StringFormat { Alignment = StringAlignment.Center };
                graphics.DrawString("本次记录", _captionFont, muted, new RectangleF(x, y - 36, width, 20), center);
            }

            DrawStatRow(graphics, x, y, width, "记录时长", FormatDuration(_snapshot.SessionElapsed));
            DrawWaveform(graphics, x, y + 80, width, (int)(60 * scale));
        }

        private void DrawStatRow(Graphics graphics, int x, int y, int width, string label, string value)
        {
            using (Brush muted = new SolidBrush(Color.FromArgb(120, 124, 128)))
            using (Brush bright = new SolidBrush(Color.FromArgb(225, 228, 232)))
            {
                StringFormat center = new StringFormat { Alignment = StringAlignment.Center };
                graphics.DrawString(label, _noteFont, muted, new RectangleF(x, y, width, 18), center);
                graphics.DrawString(value, _valueFont, bright, new RectangleF(x, y + 20, width, 26), center);
            }
        }

        private void DrawWaveform(Graphics graphics, int x, int y, int width, int height)
        {
            RectangleF area = new RectangleF(x + 8, y, width - 16, height);

            using (Brush background = new SolidBrush(Color.FromArgb(22, 18, 38)))
            {
                graphics.FillRectangle(background, area);
            }

            string speedText = _snapshot.CurrentSpeed.ToString("F3") + " m/s";

            using (Brush speedBrush = new SolidBrush(Color.FromArgb(200, 180, 195)))
            {
                StringFormat rightAlign = new StringFormat { Alignment = StringAlignment.Far };
                graphics.DrawString(speedText, _noteFont, speedBrush, new RectangleF(area.X + 6, area.Y + 5, area.Width - 12, 16), rightAlign);
            }

            if (_speedHistory.Count < 2)
            {
                return;
            }

            double maximum = Math.Max(0.5d, _speedHistory.Max());
            int count = _speedHistory.Count;
            float stepX = area.Width / (MaxSpeedSamples - 1);
            PointF[] points = new PointF[count];
            int index = 0;

            foreach (double speed in _speedHistory)
            {
                float pointX = area.Right - ((count - 1 - index) * stepX);
                float pointY = area.Bottom - ((float)(speed / maximum) * area.Height * 0.85f) - (area.Height * 0.075f);
                points[index] = new PointF(pointX, pointY);
                index++;
            }

            using (GraphicsPath fillPath = new GraphicsPath())
            {
                fillPath.AddLines(points);
                fillPath.AddLine(points[count - 1], new PointF(points[count - 1].X, area.Bottom));
                fillPath.AddLine(new PointF(points[0].X, area.Bottom), points[0]);
                fillPath.CloseFigure();

                using (LinearGradientBrush fill = new LinearGradientBrush(area, Color.FromArgb(70, 0xE7, 0x1F, 0x7A), Color.FromArgb(10, 0x5B, 0x27, 0x8E), LinearGradientMode.Vertical))
                {
                    graphics.FillPath(fill, fillPath);
                }
            }

            using (LinearGradientBrush lineBrush = new LinearGradientBrush(area, ArcInner, Accent, LinearGradientMode.Horizontal))
            using (Pen pen = new Pen(lineBrush, 2f))
            {
                graphics.DrawLines(pen, points);
            }
        }

        private void DrawRightStats(Graphics graphics, int centerY, float scale)
        {
            int width = (int)(230 * scale);
            int x = Width - (int)(24 * scale) - width;
            int y = centerY - (int)(60 * scale);

            using (Brush muted = new SolidBrush(Color.FromArgb(120, 124, 128)))
            {
                StringFormat center = new StringFormat { Alignment = StringAlignment.Center };
                graphics.DrawString("实时状态", _captionFont, muted, new RectangleF(x, y, width, 20), center);
            }

            using (Brush muted = new SolidBrush(Color.FromArgb(120, 124, 128)))
            {
                graphics.DrawString("鼠力", _noteFont, muted, x + 8, y + 50);
            }

            string status = GetIntensityLabel(_snapshot.Intensity);
            Color statusColor = GetIntensityColor(_snapshot.Intensity);

            using (Brush brush = new SolidBrush(statusColor))
            {
                graphics.DrawString(status, _statusFont, brush, x + 8, y + 70);
            }

            RectangleF badge = new RectangleF(x + width - 70, y + 49, 54, 50);

            using (Brush badgeBackground = new SolidBrush(Color.FromArgb(28, 22, 44)))
            {
                graphics.FillEllipse(badgeBackground, badge);
            }

            using (Brush badgeText = new SolidBrush(Color.FromArgb(200, 195, 215)))
            {
                StringFormat center = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center
                };
                graphics.DrawString(_snapshot.Intensity.ToString(), _badgeFont, badgeText, badge, center);
            }
        }

        private static string GetIntensityLabel(int intensity)
        {
            if (intensity < 20)
            {
                return "龟息";
            }

            if (intensity < 40)
            {
                return "沉腕";
            }

            if (intensity < 60)
            {
                return "灵动";
            }

            if (intensity < 80)
            {
                return "游龙";
            }

            return "神行";
        }

        private static Color GetIntensityColor(int intensity)
        {
            if (intensity < 20)
            {
                return Color.FromArgb(120, 124, 128);
            }

            if (intensity < 40)
            {
                return Color.FromArgb(160, 118, 158);
            }

            if (intensity < 60)
            {
                return Color.FromArgb(205, 85, 150);
            }

            if (intensity < 80)
            {
                return Accent;
            }

            return Color.FromArgb(255, 90, 130);
        }

        private static Color InterpolateColor(Color[] colors, float ratio)
        {
            if (ratio <= 0.5f)
            {
                return colors[0];
            }

            float gradientRatio = (ratio - 0.5f) / 0.35f;

            if (gradientRatio >= 1f)
            {
                return colors[colors.Length - 1];
            }

            int index = (int)(gradientRatio * (colors.Length - 1));
            float local = (gradientRatio * (colors.Length - 1)) - index;
            Color from = colors[index];
            Color to = colors[Math.Min(index + 1, colors.Length - 1)];

            return Color.FromArgb(
                (int)(from.A + ((to.A - from.A) * local)),
                (int)(from.R + ((to.R - from.R) * local)),
                (int)(from.G + ((to.G - from.G) * local)),
                (int)(from.B + ((to.B - from.B) * local)));
        }

        private static string FormatDuration(TimeSpan value)
        {
            return $"{(int)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}";
        }

        private void HandleMouseMove(object? sender, MouseEventArgs e)
        {
            Cursor = _centerBounds.Contains(e.Location) ? Cursors.Hand : Cursors.Default;
        }

        private void HandleMouseLeave(object? sender, EventArgs e)
        {
            Cursor = Cursors.Default;
        }

        private void HandleMouseClick(object? sender, MouseEventArgs e)
        {
            if (_centerBounds.Contains(e.Location))
            {
                _showTotal = !_showTotal;
                Invalidate();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _captionFont.Dispose();
                _valueFont.Dispose();
                _mileageFont.Dispose();
                _unitFont.Dispose();
                _statusFont.Dispose();
                _badgeFont.Dispose();
                _noteFont.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    private sealed class DistanceChart : Panel
    {
        private List<ChartItem> _items = new List<ChartItem>();

        public DistanceChart()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Color.FromArgb(16, 12, 26);
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public List<ChartItem> Items
        {
            get
            {
                return _items;
            }
            set
            {
                _items = value;
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            Graphics graphics = e.Graphics;
            graphics.SetClip(ClientRectangle, CombineMode.Replace);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = TextRenderingHint.AntiAlias;

            using (Brush background = new SolidBrush(Surface))
            {
                graphics.FillRectangle(background, ClientRectangle);
            }

            float scale = DeviceDpi / 96f;
            int left = (int)(52 * scale);
            int right = (int)(16 * scale);
            int top = (int)(24 * scale);
            int bottom = (int)(44 * scale);

            if (_items.Count == 0 || Width <= left + right || Height <= top + bottom)
            {
                return;
            }

            DrawBackdropGlow(graphics, left, top, Width - right - left, Height - top - bottom);

            int chartHeight = Height - top - bottom;
            double maximum = _items.Max(item => item.Value);

            if (maximum <= 0)
            {
                maximum = 1;
            }

            string valueFormat = maximum < 0.01 ? "F4" : maximum < 0.1 ? "F3" : "F2";

            using (Brush axisBrush = new SolidBrush(MutedText))
            using (Brush valueBrush = new SolidBrush(BrightText))
            using (Pen grid = new Pen(Color.FromArgb(90, ArcInner)))
            {
                grid.DashStyle = DashStyle.Dash;

                for (int line = 0; line <= 4; line++)
                {
                    int y = top + chartHeight - (line * chartHeight / 4);
                    graphics.DrawLine(grid, left, y, Width - right, y);

                    string axisValue = (maximum * line / 4).ToString(valueFormat);
                    SizeF axisSize = graphics.MeasureString(axisValue, AxisFont);
                    graphics.DrawString(axisValue, AxisFont, axisBrush, left - axisSize.Width - 6, y - axisSize.Height / 2);
                }

                int slot = (Width - left - right) / _items.Count;
                int barWidth = Math.Max(3, Math.Min((int)(28 * scale), slot - 8));
                float radius = Math.Min(4f * scale, barWidth / 2f);

                int maxLabels = Math.Max(1, (Width - left - right) / (int)(62 * scale));
                int labelStep = (int)Math.Ceiling(_items.Count / (double)maxLabels);

                for (int index = 0; index < _items.Count; index++)
                {
                    ChartItem item = _items[index];
                    int height = (int)(item.Value / maximum * chartHeight);
                    int x = left + (index * slot) + ((slot - barWidth) / 2);
                    int y = top + chartHeight - height;
                    float ratio = _items.Count > 1 ? index / (float)(_items.Count - 1) : 1f;
                    float fade = GetEndFade(ratio);
                    int alpha = (int)(170d + (85d * fade));

                    if (height > 0)
                    {
                        RectangleF bar = new RectangleF(x, y, barWidth, height);

                        using (GraphicsPath path = CreateTopRoundedPath(bar, radius))
                        using (Brush brush = new SolidBrush(Color.FromArgb(alpha, Accent)))
                        {
                            graphics.FillPath(brush, path);
                        }
                    }

                    if (height > 18 && slot >= 30)
                    {
                        string barValue = item.Value.ToString(valueFormat);
                        SizeF valueSize = graphics.MeasureString(barValue, ChartValueFont);
                        graphics.DrawString(barValue, ChartValueFont, valueBrush, x + ((barWidth - valueSize.Width) / 2), y - valueSize.Height - 2);
                    }

                    if (index % labelStep == 0)
                    {
                        SizeF labelSize = graphics.MeasureString(item.Label, AxisFont);
                        graphics.DrawString(item.Label, AxisFont, axisBrush, x + ((barWidth - labelSize.Width) / 2), top + chartHeight + 8);
                    }
                }
            }
        }

        private static void DrawBackdropGlow(Graphics graphics, int x, int y, int width, int height)
        {
            if (width <= 0 || height <= 0)
            {
                return;
            }

            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddEllipse(x, y, width, height);

                using (PathGradientBrush glow = new PathGradientBrush(path))
                {
                    glow.CenterColor = Color.FromArgb(14, ArcInner.R, ArcInner.G, ArcInner.B);
                    glow.SurroundColors = new[] { Color.FromArgb(0, ArcInner.R, ArcInner.G, ArcInner.B) };
                    graphics.FillPath(glow, path);
                }
            }
        }

        private static GraphicsPath CreateTopRoundedPath(RectangleF rect, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            float diameter = radius * 2f;

            if (rect.Height <= radius)
            {
                path.AddRectangle(rect);
                return path;
            }

            path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
            path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
            path.AddLine(rect.Right, rect.Y + radius, rect.Right, rect.Bottom);
            path.AddLine(rect.Right, rect.Bottom, rect.X, rect.Bottom);
            path.AddLine(rect.X, rect.Bottom, rect.X, rect.Y + radius);
            path.CloseFigure();

            return path;
        }
    }

    private readonly record struct ChartItem(string Label, double Value);

    #endregion
}
