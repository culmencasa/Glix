using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Glix;

internal enum HotZoneKind
{
    Switcher,
    TopMost
}

/// <summary>
/// 叠加在资源管理器标题栏上的常驻按钮，单击后请求弹出切换器或切换窗口置顶状态。
/// </summary>
internal sealed class HotZoneForm : Form
{
    private static readonly Color HoverBackdrop = Color.FromArgb(238, 242, 248);
    private static readonly Color Glyph = Color.FromArgb(20, 24, 32);
    private static readonly Color PinnedGlyph = Color.FromArgb(0, 122, 255);

    private readonly Action<Rectangle> _onClick;
    private readonly HotZoneKind _kind;
    private IntPtr _owner;
    private Rectangle _placement;
    private bool _hover;
    private bool _pinned;
    private bool _disposed;

    public HotZoneForm(Action<Rectangle> onClick, HotZoneKind kind)
    {
        _onClick = onClick;
        _kind = kind;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.Magenta;
        TransparencyKey = Color.Magenta;
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);

        MouseEnter += OnMouseEnter;
        MouseLeave += OnMouseLeave;
        Click += OnClick;
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams parameters = base.CreateParams;
            parameters.ExStyle |= NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TRANSPARENT;

            return parameters;
        }
    }

    /// <summary>
    /// 更新按钮的悬停高亮状态。
    /// </summary>
    /// <param name="hover">是否处于悬停状态。</param>
    public void SetHover(bool hover)
    {
        if (_hover == hover)
        {
            return;
        }

        _hover = hover;
        Invalidate();
    }

    /// <summary>
    /// 更新置顶按钮的激活状态。
    /// </summary>
    /// <param name="pinned">当前窗口是否置顶。</param>
    public void SetPinned(bool pinned)
    {
        if (_pinned == pinned)
        {
            return;
        }

        _pinned = pinned;
        Invalidate();
        PlaceAt(_placement.Location, _placement.Size);
    }

    /// <summary>
    /// 将按钮绑定到宿主窗口，使其 Z 序跟随宿主、并随宿主最小化而隐藏。
    /// </summary>
    /// <param name="owner">宿主资源管理器窗口句柄。</param>
    public void AttachTo(IntPtr owner)
    {
        _owner = owner;
        NativeMethods.SetWindowLongPtr(Handle, NativeMethods.GWLP_HWNDPARENT, owner);
    }

    /// <summary>
    /// 将按钮移动到指定屏幕位置。
    /// </summary>
    /// <param name="location">物理屏幕坐标。</param>
    /// <param name="size">物理尺寸。</param>
    public void PlaceAt(Point location, Size size)
    {
        bool ownerTopMost = _owner != IntPtr.Zero
            && (NativeMethods.GetWindowLongPtr(_owner, NativeMethods.GWL_EXSTYLE).ToInt64() & NativeMethods.WS_EX_TOPMOST) != 0;
        IntPtr insertAfter = ownerTopMost ? NativeMethods.HWND_TOPMOST : NativeMethods.HWND_TOP;
        NativeMethods.SetWindowPos(Handle, insertAfter, location.X, location.Y, size.Width, size.Height,
            NativeMethods.SWP_NOACTIVATE);
        _placement = new Rectangle(location, size);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeMethods.WM_MOUSEACTIVATE)
        {
            m.Result = new IntPtr(NativeMethods.MA_NOACTIVATE);

            return;
        }

        base.WndProc(ref m);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Color.Magenta);

        const int logicalSize = 28;
        using GraphicsPath path = new GraphicsPath();
        path.AddArc(0, 0, 8, 8, 180, 90);
        path.AddArc(logicalSize - 8, 0, 8, 8, 270, 90);
        path.AddArc(logicalSize - 8, logicalSize - 8, 8, 8, 0, 90);
        path.AddArc(0, logicalSize - 8, 8, 8, 90, 90);
        path.CloseFigure();

        if (_hover)
        {
            using SolidBrush brush = new SolidBrush(HoverBackdrop);
            e.Graphics.FillPath(brush, path);
        }

        Color glyphColor = _kind == HotZoneKind.TopMost && _pinned ? PinnedGlyph : Glyph;

        if (_kind == HotZoneKind.TopMost)
        {
            using Pen glyph = new Pen(glyphColor, 1.5f);
            e.Graphics.DrawRectangle(glyph, 6, 7, 16, 4);
            e.Graphics.DrawLine(glyph, 14, 11, 14, 19);
        }
        else
        {
            using Pen glyph = new Pen(glyphColor, 1.5f);
            e.Graphics.DrawRectangle(glyph, 7, 7, 12, 9);
            e.Graphics.DrawRectangle(glyph, 10, 12, 12, 9);
        }
    }

    private void OnMouseEnter(object? sender, EventArgs e)
    {
        SetHover(true);
    }

    private void OnMouseLeave(object? sender, EventArgs e)
    {
        SetHover(false);
    }

    private void OnClick(object? sender, EventArgs e)
    {
        _onClick(_placement);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
        }

        base.Dispose(disposing);
    }
}
