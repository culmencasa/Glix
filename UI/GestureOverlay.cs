using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace Glix;

/// <summary>
/// 已执行的导航动作类型。
/// </summary>
internal enum GestureActionKind
{
    /// <summary>
    /// 后退。
    /// </summary>
    Backward,

    /// <summary>
    /// 前进。
    /// </summary>
    Forward
}

/// <summary>
/// 悬浮提示窗：真正的像素风卡片。
/// 在小尺寸离屏位图上绘制（含点阵中文），再以最近邻放大到窗口，所有元素都呈像素格；
/// 「图标 + 文字」作为一组紧挨居中，组四周留白。鼠标穿透且不抢焦点。
/// </summary>
internal sealed class GestureOverlay : Form
{
    #region 字段

    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_TRANSPARENT = 0x00000020;

    private const int MarginFromEdge = 18;
    private const int ActionVisibleMs = 1500;
    private const int HideTimerIntervalMs = 60;

    // 逻辑像素画布尺寸与放大倍数：所有绘制在 64x26 的小画布上完成，再最近邻放大 PixelScale 倍。
    private const int LogicalWidth = 64;
    private const int LogicalHeight = 26;
    private const int PixelScale = 2;
    private const int BorderPx = 2;

    private static readonly Color PanelColor = Color.FromArgb(16, 18, 24);
    private static readonly Color TextColor = Color.FromArgb(245, 247, 250);
    private static readonly Color BackwardAccent = Color.FromArgb(255, 176, 60);
    private static readonly Color ForwardAccent = Color.FromArgb(96, 202, 250);

    private readonly System.Windows.Forms.Timer _hideTimer;
    private readonly System.Windows.Forms.Timer _followTimer;
    private readonly Font _textFont;
    private readonly Bitmap _surface;

    private Color _accent = BackwardAccent;
    private bool _arrowPointsLeft;
    private IntPtr _targetWindow;
    private DateTime _hideAt;
    private double _opacity = 0.88d;

    #endregion

    #region 构造函数

    public GestureOverlay()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = PanelColor;
        Text = "Glix.Overlay";
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);

        // 用较小的字号在逻辑画布上绘制点阵中文，放大后呈像素格。
        _textFont = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold);
        _surface = new Bitmap(LogicalWidth, LogicalHeight, PixelFormat.Format32bppArgb);

        _hideTimer = new System.Windows.Forms.Timer { Interval = HideTimerIntervalMs };
        _followTimer = new System.Windows.Forms.Timer { Interval = 100 };
        _hideTimer.Tick += OnHideTimerTick;
        _followTimer.Tick += OnFollowTimerTick;

        ApplySize();
    }

    #endregion

    #region 公共方法

    /// <summary>
    /// 显示一次已执行动作的像素风提示。
    /// </summary>
    /// <param name="targetWindow">资源管理器顶层窗口句柄。</param>
    /// <param name="kind">执行的动作类型。</param>
    public void ShowAction(IntPtr targetWindow, GestureActionKind kind)
    {
        _targetWindow = targetWindow;
        _accent = kind == GestureActionKind.Backward ? BackwardAccent : ForwardAccent;
        _arrowPointsLeft = kind == GestureActionKind.Backward;
        _hideAt = DateTime.UtcNow.AddMilliseconds(ActionVisibleMs);

        RenderSurface();
        Present();
    }

    /// <summary>
    /// 立即隐藏提示窗并停止跟随与淡出。
    /// </summary>
    public void HideNow()
    {
        HideOverlay();
    }

    #endregion

    #region 重写

    /// <summary>
    /// 释放字体、位图与定时器资源。
    /// </summary>
    /// <param name="disposing">是否释放托管资源。</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _hideTimer.Dispose();
            _followTimer.Dispose();
            _textFont.Dispose();
            _surface.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// 不获取焦点地显示窗体。
    /// </summary>
    protected override bool ShowWithoutActivation => true;

    /// <summary>
    /// 追加不激活、工具窗口与鼠标穿透的扩展样式。
    /// </summary>
    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams parameters = base.CreateParams;
            parameters.ExStyle |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT;

            return parameters;
        }
    }

    #endregion

    #region 绘制

    /// <summary>
    /// 在逻辑画布上绘制像素风卡片：粗边框 + 紧挨的「图标 + 文字」组，四周留白。
    /// </summary>
    private void RenderSurface()
    {
        using Graphics graphics = Graphics.FromImage(_surface);
        graphics.Clear(PanelColor);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
        graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit;

        // 像素风粗边框：四条纯色矩形，方角、无抗锯齿。
        using (SolidBrush borderBrush = new SolidBrush(_accent))
        {
            graphics.FillRectangle(borderBrush, 0, 0, LogicalWidth, BorderPx);
            graphics.FillRectangle(borderBrush, 0, LogicalHeight - BorderPx, LogicalWidth, BorderPx);
            graphics.FillRectangle(borderBrush, 0, 0, BorderPx, LogicalHeight);
            graphics.FillRectangle(borderBrush, LogicalWidth - BorderPx, 0, BorderPx, LogicalHeight);
        }

        string label = _arrowPointsLeft ? "后退" : "前进";
        SizeF textSize = graphics.MeasureString(label, _textFont);
        int textWidth = (int)Math.Ceiling(textSize.Width);
        int textHeight = (int)Math.Ceiling(textSize.Height);

        const int arrowWidth = 9;
        const int arrowHeight = 7;
        const int gap = 2;

        // 「图标 + 文字」作为一个整体组，水平垂直都居中，四周自然留白。
        int groupWidth = arrowWidth + gap + textWidth;
        int groupHeight = Math.Max(arrowHeight, textHeight);
        int groupX = (LogicalWidth - groupWidth) / 2;
        int groupY = (LogicalHeight - groupHeight) / 2;

        DrawPixelArrow(graphics, groupX, groupY);
        using SolidBrush textBrush = new SolidBrush(TextColor);
        graphics.DrawString(
            label,
            _textFont,
            textBrush,
            groupX + arrowWidth + gap,
            groupY + (groupHeight - textHeight) / 2);
    }

    /// <summary>
    /// 以 7x7 像素网格绘制箭头，紧邻文字左侧。
    /// </summary>
    private void DrawPixelArrow(Graphics graphics, int originX, int originY)
    {
        string[] pixels = _arrowPointsLeft
            ? new[] { "..##...", ".###...", "####...", "#####..", "####...", ".###...", "..##..." }
            : new[] { "...##..", "...###.", "...####", "..#####", "...####", "...###.", "...##.." };

        using SolidBrush brush = new SolidBrush(_accent);

        for (int row = 0; row < pixels.Length; row++)
        {
            for (int column = 0; column < pixels[row].Length; column++)
            {
                if (pixels[row][column] == '#')
                {
                    graphics.FillRectangle(brush, originX + column, originY + row, 1, 1);
                }
            }
        }
    }

    /// <summary>
    /// 最近邻放大逻辑画布到窗口，保留硬朗像素格。
    /// </summary>
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        Graphics graphics = e.Graphics;
        graphics.Clear(PanelColor);
        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
        graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
        graphics.DrawImage(_surface, new Rectangle(0, 0, Width, Height), 0, 0, LogicalWidth, LogicalHeight, GraphicsUnit.Pixel);
    }

    #endregion

    #region 布局与定时

    private void Present()
    {
        _opacity = 0.88d;
        Opacity = _opacity;
        Reposition();

        if (!Visible)
        {
            Show();
        }
        else
        {
            Invalidate();
        }

        _hideTimer.Start();
        _followTimer.Start();
    }

    private void Reposition()
    {
        int margin = (int)(MarginFromEdge * ScaleFactor);

        if (_targetWindow != IntPtr.Zero
            && NativeMethods.IsWindowVisible(_targetWindow)
            && !NativeMethods.IsIconic(_targetWindow)
            && NativeMethods.GetClientRect(_targetWindow, out NativeMethods.RECT clientRect))
        {
            NativeMethods.POINT bottomRight = new NativeMethods.POINT
            {
                X = clientRect.Right,
                Y = clientRect.Bottom
            };

            if (NativeMethods.ClientToScreen(_targetWindow, ref bottomRight))
            {
                Location = new Point(bottomRight.X - Width - margin, bottomRight.Y - Height - margin);
                return;
            }
        }

        if (!Visible && Location.IsEmpty && Screen.PrimaryScreen is not null)
        {
            Rectangle workArea = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(workArea.Right - Width - margin, workArea.Bottom - Height - margin);
        }
    }

    private void ApplySize()
    {
        int scale = Math.Max(1, (int)Math.Round(ScaleFactor));
        Size = new Size(LogicalWidth * PixelScale * scale, LogicalHeight * PixelScale * scale);
    }

    private float ScaleFactor
    {
        get
        {
            int dpi = DeviceDpi;

            return dpi <= 0 ? 1f : dpi / 96f;
        }
    }

    private void OnHideTimerTick(object? sender, EventArgs e)
    {
        if (DateTime.UtcNow < _hideAt)
        {
            return;
        }

        _opacity -= 0.12d;

        if (_opacity <= 0.06d)
        {
            HideOverlay();
            return;
        }

        Opacity = _opacity;
    }

    private void OnFollowTimerTick(object? sender, EventArgs e)
    {
        if (Visible)
        {
            Reposition();
        }
    }

    private void HideOverlay()
    {
        _hideTimer.Stop();
        _followTimer.Stop();
        Hide();
    }

    #endregion
}
