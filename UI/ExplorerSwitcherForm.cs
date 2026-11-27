using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace Glix;

/// <summary>
/// 仿 Windows 11 任务切换器的资源管理器窗口切换器：网格卡片，支持键盘导航与滚动。
/// </summary>
internal sealed class ExplorerSwitcherForm : Form
{
    #region 字段与常量

    private const int OuterPadX = 22;
    private const int OuterPadY = 18;
    private const int HeaderHeight = 10;
    private const int FooterHeight = 78;
    private const int TileGap = 12;
    private const int TileWidth = 192;
    private const int TileHeight = 212;
    private const int PreviewTop = 36;
    private const int PreviewHeight = 148;
    private const int TileRadius = 10;

    private static readonly Color PanelColor = Color.FromArgb(21, 28, 44);
    private static readonly Color PanelBorderColor = Color.FromArgb(44, 55, 75);
    private static readonly Color TileColor = Color.FromArgb(27, 36, 54);
    private static readonly Color TileHoverColor = Color.FromArgb(34, 45, 68);
    private static readonly Color FocusColor = Color.FromArgb(76, 141, 255);
    private static readonly Color TextColor = Color.FromArgb(242, 244, 248);
    private static readonly Color SubTextColor = Color.FromArgb(138, 148, 166);
    private static readonly Color CloseTextColor = Color.FromArgb(205, 210, 220);
    private static readonly Color CloseHoverColor = Color.FromArgb(232, 17, 35);

    private readonly Font _tileFont = new Font("Segoe UI", 9f, FontStyle.Bold);
    private readonly Font _hintFont = new Font("Segoe UI", 8f);
    private readonly CancellationTokenSource _loadCancellation = new CancellationTokenSource();
    private readonly System.Windows.Forms.Timer _loadTimer = new System.Windows.Forms.Timer();
    private readonly List<SwitcherTile> _tiles = new List<SwitcherTile>();
    private readonly List<IntPtr> _thumbnails = new List<IntPtr>();
    private readonly Dictionary<IntPtr, Bitmap> _icons;
    private readonly Panel _tilePanel = new Panel();
    private IReadOnlyList<ExplorerWindow> _windows;
    private Bitmap _folderIcon;
    private Thread? _loadThread;
    private Dictionary<IntPtr, Bitmap>? _pendingIcons;
    private Bitmap? _pendingFolderIcon;
    private IReadOnlyList<ExplorerWindow>? _pendingWindows;
    private List<IntPtr>? _pendingThumbnails;
    private volatile bool _loadReady;
    private bool _disposed;
    private int _tileWidth = TileWidth;
    private int _columns = 1;
    private int _rows = 1;
    private int _selectedIndex = -1;
    private IntPtr _windowHandle;
    private IntPtr _previousForeground;

    #endregion

    #region 构造函数

    /// <summary>
    /// 使用已固定的窗口顺序创建切换器。
    /// </summary>
    /// <param name="windows">按展示顺序排列的资源管理器窗口。</param>
    public ExplorerSwitcherForm(IReadOnlyList<ExplorerWindow> windows)
    {
        _windows = windows;
        _folderIcon = CreateFallbackFolderIcon();
        _icons = CreatePlaceholderIcons(windows, _folderIcon);

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        BackColor = PanelColor;
        Opacity = 0.92;
        Text = string.Empty;
        Icon = AppIcon.Create();
        DoubleBuffered = true;
        KeyPreview = true;

        Click += OnBackdropClicked;
        _tilePanel.Click += OnBackdropClicked;
        _tilePanel.Scroll += OnTilePanelScroll;
        Controls.Add(_tilePanel);

        BuildLayout();

        // 先创建句柄，使首次显示无需等待外壳图标与 DWM 初始化。
        CreateControl();
        _windowHandle = Handle;

        _loadTimer.Interval = 30;
        _loadTimer.Tick += OnLoadTimerTick;
        _loadTimer.Start();

        StartIconLoad(windows);
    }

    #endregion

    #region 公共方法

    /// <summary>
    /// 刷新窗口列表并重建卡片。
    /// </summary>
    /// <param name="windows">新的资源管理器窗口列表。</param>
    public void Update(IReadOnlyList<ExplorerWindow> windows)
    {
        UnregisterThumbnails();
        _windows = windows;

        foreach (SwitcherTile tile in _tiles)
        {
            _tilePanel.Controls.Remove(tile);
            tile.Dispose();
        }

        _tiles.Clear();

        foreach (Bitmap icon in _icons.Values)
        {
            icon.Dispose();
        }

        _icons.Clear();

        Dictionary<IntPtr, Bitmap> icons = ExplorerIconResolver.Resolve(windows, _folderIcon);

        foreach (KeyValuePair<IntPtr, Bitmap> pair in icons)
        {
            _icons[pair.Key] = pair.Value;
        }

        BuildLayout();
        RegisterThumbnails();
    }

    /// <summary>
    /// 将切换器显示在触发按钮下方并显示。
    /// </summary>
    /// <param name="anchor">按钮的屏幕矩形，为空时居中显示。</param>
    public void Present(Rectangle anchor)
    {
        IntPtr foreground = NativeMethods.GetForegroundWindow();
        if (foreground != _windowHandle)
        {
            _previousForeground = foreground;
        }

        PositionBelowAnchor(anchor);

        if (!Visible)
        {
            Show();
        }

        ForceForeground(_windowHandle);

        NativeMethods.SetWindowPos(Handle, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);

        if (_thumbnails.Count > 0 && _thumbnails.Count != _windows.Count)
        {
            RegisterThumbnails();
        }

        UpdateThumbnailProperties();
    }

    /// <summary>
    /// 将选中项移动到下一个卡片，循环回绕。供 Ctrl+Tab 导航使用。
    /// </summary>
    public void MoveNext()
    {
        SelectTile(_selectedIndex + 1);
    }

    /// <summary>
    /// 激活当前选中项对应的资源管理器窗口并关闭切换器。供松开 Ctrl 时提交使用。
    /// </summary>
    public void ActivateSelected()
    {
        if (_selectedIndex < 0 || _selectedIndex >= _windows.Count)
        {
            return;
        }

        ActivateWindow(_windows[_selectedIndex].Handle);
    }

    /// <summary>
    /// 切换器隐藏时通知宿主，便于暂停手势触发。
    /// </summary>
    public event Action? Hidden;

    /// <summary>
    /// 判断物理屏幕坐标是否落在切换器窗口内。
    /// </summary>
    /// <param name="point">物理屏幕坐标。</param>
    /// <returns>命中切换器返回 <c>true</c>。</returns>
    public bool ContainsScreenPoint(Point point)
    {
        if (_windowHandle == IntPtr.Zero || !NativeMethods.GetWindowRect(_windowHandle, out NativeMethods.RECT rect))
        {
            return false;
        }

        return point.X >= rect.Left && point.X < rect.Right && point.Y >= rect.Top && point.Y < rect.Bottom;
    }

    /// <summary>
    /// 隐藏切换器，缩略图随同一窗口一并隐藏。
    /// </summary>
    public void HideAll()
    {
        HideAll(true);
    }

    private void HideAll(bool restoreForeground)
    {
        Hide();

        if (restoreForeground && _previousForeground != IntPtr.Zero && NativeMethods.IsWindow(_previousForeground))
        {
            NativeMethods.SetForegroundWindow(_previousForeground);
        }

        Hidden?.Invoke();
    }

    #endregion

    #region 私有方法

    private void RegisterThumbnails()
    {
        UnregisterThumbnails();

        if (!IsHandleCreated)
        {
            return;
        }

        foreach (ExplorerWindow window in _windows)
        {
            IntPtr thumbnail = IntPtr.Zero;

            if (NativeMethods.IsWindow(window.Handle) &&
                NativeMethods.DwmRegisterThumbnail(Handle, window.Handle, out thumbnail) == 0)
            {
                _thumbnails.Add(thumbnail);
            }
            else
            {
                _thumbnails.Add(IntPtr.Zero);
            }
        }

        UpdateThumbnailProperties();
    }

    private void UnregisterThumbnails()
    {
        foreach (IntPtr thumbnail in _thumbnails)
        {
            if (thumbnail != IntPtr.Zero)
            {
                NativeMethods.DwmUnregisterThumbnail(thumbnail);
            }
        }

        _thumbnails.Clear();
    }

    private void UpdateThumbnailProperties()
    {
        if (!IsHandleCreated)
        {
            return;
        }

        for (int index = 0; index < _thumbnails.Count && index < _tiles.Count; index++)
        {
            IntPtr thumbnail = _thumbnails[index];

            if (thumbnail == IntPtr.Zero)
            {
                continue;
            }

            SwitcherTile tile = _tiles[index];
            Point origin = PointToClient(tile.PointToScreen(new Point(8, PreviewTop)));
            int width = tile.Width - 16;
            bool visible = origin.X < ClientSize.Width && origin.Y < ClientSize.Height &&
                           origin.X + width > 0 && origin.Y + PreviewHeight > 0;

            NativeMethods.DWM_THUMBNAIL_PROPERTIES properties = new NativeMethods.DWM_THUMBNAIL_PROPERTIES
            {
                DwFlags = NativeMethods.DWM_TNP_RECTDESTINATION | NativeMethods.DWM_TNP_VISIBLE,
                RcDestination = new NativeMethods.RECT
                {
                    Left = origin.X,
                    Top = origin.Y,
                    Right = origin.X + width,
                    Bottom = origin.Y + PreviewHeight
                },
                Opacity = 255,
                FVisible = visible,
                FSourceClientAreaOnly = false
            };
            NativeMethods.DwmUpdateThumbnailProperties(thumbnail, ref properties);
        }
    }

    private void OnTilePanelScroll(object? sender, ScrollEventArgs e)
    {
        UpdateThumbnailProperties();
    }

    private void BuildLayout()
    {
        SuspendLayout();

        Rectangle area = Screen.FromPoint(Cursor.Position).WorkingArea;
        int count = _windows.Count;
        _columns = count <= 2 ? count : count <= 4 ? 2 : count <= 9 ? 3 : 4;
        _rows = (count + _columns - 1) / _columns;
        int visibleRows = Math.Min(_rows, 4);

        int scrollbarWidth = _rows > visibleRows ? SystemInformation.VerticalScrollBarWidth : 0;
        int maxWidth = area.Width - 40;
        int naturalWidth = OuterPadX * 2 + _columns * TileWidth + (_columns - 1) * TileGap + scrollbarWidth;

        if (naturalWidth > maxWidth)
        {
            _tileWidth = Math.Max(120, (maxWidth - OuterPadX * 2 - (_columns - 1) * TileGap - scrollbarWidth) / _columns);
        }

        int width = OuterPadX * 2 + _columns * _tileWidth + (_columns - 1) * TileGap + scrollbarWidth;
        width = Math.Max(width, 560);

        ClientSize = new Size(
            width,
            OuterPadY * 2 + HeaderHeight + visibleRows * TileHeight + (visibleRows - 1) * TileGap + FooterHeight);

        _tilePanel.Location = new Point(0, OuterPadY + HeaderHeight);
        _tilePanel.Size = new Size(ClientSize.Width - scrollbarWidth, visibleRows * TileHeight + (visibleRows - 1) * TileGap);
        _tilePanel.BackColor = PanelColor;
        _tilePanel.AutoScroll = true;

        for (int index = 0; index < count; index++)
        {
            int column = index % _columns;
            int row = index / _columns;
            int x = OuterPadX + column * (_tileWidth + TileGap);
            int y = row * (TileHeight + TileGap);

            SwitcherTile tile = new SwitcherTile(this, _windows[index], index)
            {
                Location = new Point(x, y),
                Size = new Size(_tileWidth, TileHeight)
            };
            _tiles.Add(tile);
            _tilePanel.Controls.Add(tile);
        }

        ResumeLayout(false);
        SelectTile(0);
    }

    private void SelectTile(int index)
    {
        if (_tiles.Count == 0)
        {
            return;
        }

        if (index < 0)
        {
            index = _tiles.Count - 1;
        }
        else if (index >= _tiles.Count)
        {
            index = 0;
        }

        if (index == _selectedIndex)
        {
            return;
        }

        if (_selectedIndex >= 0)
        {
            _tiles[_selectedIndex].IsSelected = false;
            _tiles[_selectedIndex].Invalidate();
        }

        _selectedIndex = index;
        _tiles[_selectedIndex].IsSelected = true;
        _tiles[_selectedIndex].Invalidate();
    }

    private static Region CreateRoundedRegion(int width, int height, float radius)
    {
        using GraphicsPath path = new GraphicsPath();
        path.AddArc(0, 0, radius, radius, 180, 90);
        path.AddArc(width - radius, 0, radius, radius, 270, 90);
        path.AddArc(width - radius, height - radius, radius, radius, 0, 90);
        path.AddArc(0, height - radius, radius, radius, 90, 90);
        path.CloseFigure();

        return new Region(path);
    }

    private static Dictionary<IntPtr, Bitmap> CreatePlaceholderIcons(IReadOnlyList<ExplorerWindow> windows, Bitmap fallback)
    {
        Dictionary<IntPtr, Bitmap> icons = new Dictionary<IntPtr, Bitmap>();

        foreach (ExplorerWindow window in windows)
        {
            icons[window.Handle] = new Bitmap(fallback);
        }

        return icons;
    }

    private void StartIconLoad(IReadOnlyList<ExplorerWindow> windows)
    {
        _loadThread = new Thread(() => LoadIcons(windows))
        {
            IsBackground = true,
            Name = "Glix.SwitcherIconLoad"
        };
        _loadThread.SetApartmentState(ApartmentState.STA);
        _loadThread.Start();
    }

    private void LoadIcons(IReadOnlyList<ExplorerWindow> windows)
    {
        Bitmap? folderIcon = null;
        Dictionary<IntPtr, Bitmap>? icons = null;
        List<IntPtr>? thumbnails = null;

        try
        {
            if (_loadCancellation.IsCancellationRequested)
            {
                return;
            }

            folderIcon = GetFolderIcon();

            if (_loadCancellation.IsCancellationRequested)
            {
                return;
            }

            icons = ExplorerIconResolver.Resolve(windows, folderIcon);

            if (_loadCancellation.IsCancellationRequested)
            {
                return;
            }

            thumbnails = RegisterThumbnailHandles(_windowHandle, windows);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // 外壳图标或缩略图注册失败时沿用占位内容，切换器仍可正常显示。
        }

        if (_loadCancellation.IsCancellationRequested)
        {
            DisposeIconSet(folderIcon, icons);
            ReleaseThumbnails(thumbnails);

            return;
        }

        _pendingFolderIcon = folderIcon;
        _pendingIcons = icons;
        _pendingWindows = windows;
        _pendingThumbnails = thumbnails;
        _loadReady = true;
    }

    private static List<IntPtr> RegisterThumbnailHandles(IntPtr destination, IReadOnlyList<ExplorerWindow> windows)
    {
        List<IntPtr> thumbnails = new List<IntPtr>();

        foreach (ExplorerWindow window in windows)
        {
            IntPtr thumbnail = IntPtr.Zero;

            if (NativeMethods.IsWindow(window.Handle) &&
                NativeMethods.DwmRegisterThumbnail(destination, window.Handle, out thumbnail) == 0)
            {
                thumbnails.Add(thumbnail);
            }
            else
            {
                thumbnails.Add(IntPtr.Zero);
            }
        }

        return thumbnails;
    }

    private static void ReleaseThumbnails(List<IntPtr>? thumbnails)
    {
        if (thumbnails is null)
        {
            return;
        }

        foreach (IntPtr thumbnail in thumbnails)
        {
            if (thumbnail != IntPtr.Zero)
            {
                NativeMethods.DwmUnregisterThumbnail(thumbnail);
            }
        }

        thumbnails.Clear();
    }

    private void OnLoadTimerTick(object? sender, EventArgs e)
    {
        if (!_loadReady)
        {
            return;
        }

        _loadReady = false;
        _loadTimer.Stop();

        Bitmap? folderIcon = _pendingFolderIcon;
        Dictionary<IntPtr, Bitmap>? icons = _pendingIcons;
        IReadOnlyList<ExplorerWindow>? windows = _pendingWindows;

        _pendingFolderIcon = null;
        _pendingIcons = null;
        _pendingWindows = null;

        ApplyLoadedIcons(folderIcon, icons, windows);
    }

    private void ApplyLoadedIcons(Bitmap? folderIcon, Dictionary<IntPtr, Bitmap>? icons, IReadOnlyList<ExplorerWindow>? windows)
    {
        List<IntPtr>? thumbnails = _pendingThumbnails;
        _pendingThumbnails = null;

        if (folderIcon is null || icons is null || !ReferenceEquals(_windows, windows))
        {
            DisposeIconSet(folderIcon, icons);
            ReleaseThumbnails(thumbnails);

            return;
        }

        _folderIcon.Dispose();
        _folderIcon = folderIcon;

        foreach (Bitmap icon in _icons.Values)
        {
            icon.Dispose();
        }

        _icons.Clear();

        foreach (KeyValuePair<IntPtr, Bitmap> pair in icons)
        {
            _icons[pair.Key] = pair.Value;
        }

        foreach (SwitcherTile tile in _tiles)
        {
            tile.Invalidate();
        }

        UnregisterThumbnails();

        if (thumbnails is not null)
        {
            _thumbnails.AddRange(thumbnails);
        }

        UpdateThumbnailProperties();
    }

    private static void DisposeIconSet(Bitmap? folderIcon, Dictionary<IntPtr, Bitmap>? icons)
    {
        folderIcon?.Dispose();

        if (icons is null)
        {
            return;
        }

        foreach (Bitmap icon in icons.Values)
        {
            icon.Dispose();
        }

        icons.Clear();
    }

    private void DisposePendingIcons()
    {
        Bitmap? folderIcon = _pendingFolderIcon;
        Dictionary<IntPtr, Bitmap>? icons = _pendingIcons;
        List<IntPtr>? thumbnails = _pendingThumbnails;

        _pendingFolderIcon = null;
        _pendingIcons = null;
        _pendingWindows = null;
        _pendingThumbnails = null;

        DisposeIconSet(folderIcon, icons);
        ReleaseThumbnails(thumbnails);
    }

    private static Bitmap GetFolderIcon()
    {
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Glix");
        Directory.CreateDirectory(path);
        NativeMethods.SHFILEINFO info = new NativeMethods.SHFILEINFO();
        IntPtr result = NativeMethods.SHGetFileInfo(
            path,
            NativeMethods.FILE_ATTRIBUTE_DIRECTORY,
            ref info,
            (uint)Marshal.SizeOf<NativeMethods.SHFILEINFO>(),
            NativeMethods.SHGFI_ICON | NativeMethods.SHGFI_SMALLICON);

        if (result != IntPtr.Zero && info.HIcon != IntPtr.Zero)
        {
            try
            {
                using Icon icon = Icon.FromHandle(info.HIcon);
                Bitmap bitmap = icon.ToBitmap();

                return new Bitmap(bitmap, new Size(28, 28));
            }
            finally
            {
                NativeMethods.DestroyIcon(info.HIcon);
            }
        }

        return CreateFallbackFolderIcon();
    }

    private static Bitmap CreateFallbackFolderIcon()
    {
        Bitmap bitmap = new Bitmap(28, 28, PixelFormat.Format32bppArgb);

        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Transparent);

            using Brush brush = new SolidBrush(Color.FromArgb(240, 190, 70));
            graphics.FillRectangle(brush, 2, 9, 24, 16);
            graphics.FillRectangle(brush, 2, 5, 10, 5);

            using Pen pen = new Pen(Color.FromArgb(170, 130, 40));
            graphics.DrawRectangle(pen, 2, 9, 24, 16);
        }

        return bitmap;
    }

    private static void ForceForeground(IntPtr handle)
    {
        IntPtr foreground = NativeMethods.GetForegroundWindow();
        uint foregroundThread = NativeMethods.GetWindowThreadProcessId(foreground, out _);
        uint currentThread = NativeMethods.GetCurrentThreadId();
        bool attached = false;

        if (foregroundThread != currentThread)
        {
            attached = NativeMethods.AttachThreadInput(currentThread, foregroundThread, true);
        }

        try
        {
            NativeMethods.BringWindowToTop(handle);
            NativeMethods.SetForegroundWindow(handle);
        }
        finally
        {
            if (attached)
            {
                NativeMethods.AttachThreadInput(currentThread, foregroundThread, false);
            }
        }
    }

    private void ActivateWindow(IntPtr handle)
    {
        if (!NativeMethods.IsWindow(handle))
        {
            return;
        }

        if (NativeMethods.IsIconic(handle))
        {
            NativeMethods.ShowWindow(handle, NativeMethods.SW_RESTORE);
        }

        ForceForeground(handle);
        HideAll(false);
    }

    private void CloseWindow(IntPtr handle)
    {
        if (NativeMethods.IsWindow(handle))
        {
            NativeMethods.PostMessage(handle, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        }

        HideAll();
    }

    private void PositionBelowAnchor(Rectangle anchor)
    {
        Rectangle area = Screen.FromRectangle(anchor.IsEmpty ? new Rectangle(Cursor.Position, Size.Empty) : anchor).WorkingArea;

        if (anchor.IsEmpty)
        {
            Location = new Point(
                area.Left + (area.Width - Width) / 2,
                area.Top + (area.Height - Height) / 2);

            return;
        }

        const int gap = 8;
        int left = anchor.Left + (anchor.Width - Width) / 2;
        int top = anchor.Bottom + gap;

        if (top + Height > area.Bottom)
        {
            top = anchor.Top - Height - gap;
        }

        left = Math.Clamp(left, area.Left, Math.Max(area.Left, area.Right - Width));
        top = Math.Clamp(top, area.Top, Math.Max(area.Top, area.Bottom - Height));

        Location = new Point(left, top);
    }

    #endregion

    #region 嵌套类型

    private sealed class SwitcherTile : Panel
    {
        private readonly ExplorerSwitcherForm _owner;
        private readonly ExplorerWindow _window;
        private readonly int _index;
        private readonly CloseButton _close;

        public SwitcherTile(ExplorerSwitcherForm owner, ExplorerWindow window, int index)
        {
            _owner = owner;
            _window = window;
            _index = index;

            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);

            _close = new CloseButton
            {
                Location = new Point(Width - 28, 6),
                Size = new Size(20, 20),
                Visible = false
            };
            Controls.Add(_close);

            MouseEnter += (_, _) => _owner.SelectTile(_index);
            Click += (_, _) => _owner.ActivateWindow(_window.Handle);
            _close.Click += (_, _) => _owner.CloseWindow(_window.Handle);
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool IsSelected { get; set; }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            Region = CreateRoundedRegion(Width, Height, TileRadius);
            _close.Location = new Point(Width - 28, 6);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(IsSelected ? TileHoverColor : TileColor);

            using (Pen border = new Pen(IsSelected ? FocusColor : PanelBorderColor, 2f))
            {
                using GraphicsPath path = new GraphicsPath();
                path.AddArc(1, 1, TileRadius, TileRadius, 180, 90);
                path.AddArc(Width - TileRadius - 1, 1, TileRadius, TileRadius, 270, 90);
                path.AddArc(Width - TileRadius - 1, Height - TileRadius - 1, TileRadius, TileRadius, 0, 90);
                path.AddArc(1, Height - TileRadius - 1, TileRadius, TileRadius, 90, 90);
                path.CloseFigure();
                e.Graphics.DrawPath(border, path);
            }

            e.Graphics.DrawImage(_owner._icons[_window.Handle], new Rectangle(9, 9, 14, 14));

            using (Brush text = new SolidBrush(TextColor))
            {
                e.Graphics.DrawString(_window.Title, _owner._tileFont, text, new Rectangle(28, 7, Width - 56, 22));
            }

            _close.Visible = IsSelected;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            Invalidate();
        }
    }

    private sealed class CloseButton : Button
    {
        public CloseButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
            FlatStyle = FlatStyle.Flat;
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            BackColor = CloseHoverColor;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            BackColor = Color.Transparent;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);

            using Pen pen = new Pen(BackColor == CloseHoverColor ? Color.White : CloseTextColor, 1.5f);
            e.Graphics.DrawLine(pen, 6, 6, Width - 6, Height - 6);
            e.Graphics.DrawLine(pen, Width - 6, 6, 6, Height - 6);
        }
    }

    #endregion

    #region 事件重写

    private void OnBackdropClicked(object? sender, EventArgs e)
    {
        HideAll();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        using (Pen border = new Pen(PanelBorderColor, 1f))
        {
            e.Graphics.DrawRectangle(border, 1, 1, Width - 3, Height - 3);
        }

        DrawFooter(e.Graphics);
    }

    private void DrawFooter(Graphics graphics)
    {
        int top = Height - FooterHeight + 6;
        DrawFooterRow(graphics, top, ("Tab", true), ("切换焦点", false), ("shift+tab", true), ("反向切换", false), ("Enter", true), ("切换窗口", false));
        DrawFooterRow(graphics, top + 22, ("Esc", true), ("关闭", false));
        DrawFooterRow(graphics, top + 44, ("点击空白区域关闭", false));
    }

    private void DrawFooterRow(Graphics graphics, int y, params (string Text, bool IsKey)[] items)
    {
        int width = 0;

        foreach ((string text, bool isKey) in items)
        {
            width += (int)graphics.MeasureString(text, _hintFont).Width + (isKey ? 17 : 10);
        }

        int x = (Width - width) / 2;

        foreach ((string text, bool isKey) in items)
        {
            if (isKey)
            {
                DrawKey(graphics, ref x, y, text);
            }
            else
            {
                DrawHint(graphics, ref x, y, text);
            }
        }
    }

    private void DrawKey(Graphics graphics, ref int x, int y, string text)
    {
        SizeF size = graphics.MeasureString(text, _hintFont);
        int width = (int)size.Width + 12;

        using (Brush brush = new SolidBrush(PanelBorderColor))
        {
            graphics.FillRectangle(brush, x, y, width, 18);
        }

        using (Brush brush = new SolidBrush(TextColor))
        {
            graphics.DrawString(text, _hintFont, brush, x + 6, y + 3);
        }

        x += width + 5;
    }

    private void DrawHint(Graphics graphics, ref int x, int y, string text)
    {
        using Brush brush = new SolidBrush(SubTextColor);
        graphics.DrawString(text, _hintFont, brush, x, y + 3);
        x += (int)graphics.MeasureString(text, _hintFont).Width + 10;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // Ctrl+Tab 由全局键盘钩子统一处理，避免与导航会话重复移动选中项。
        if ((keyData & Keys.Control) != 0 && keyData == (Keys.Tab | Keys.Control))
        {
            return true;
        }

        if (keyData == Keys.Tab || keyData == Keys.Right)
        {
            SelectTile(_selectedIndex + 1);

            return true;
        }

        if (keyData == (Keys.Tab | Keys.Shift) || keyData == Keys.Left)
        {
            SelectTile(_selectedIndex - 1);

            return true;
        }

        if (keyData == Keys.Enter)
        {
            ActivateWindow(_windows[_selectedIndex].Handle);

            return true;
        }

        if (keyData == Keys.Escape)
        {
            HideAll();

            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    #endregion

    #region 释放

    /// <summary>
    /// 释放缩略图、图标与字体资源。
    /// </summary>
    /// <param name="disposing">是否释放托管资源。</param>
    protected override void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            _disposed = true;
            _loadCancellation.Cancel();
            _loadTimer.Stop();

            Thread? loadThread = _loadThread;

            if (loadThread is not null && loadThread.IsAlive && !ReferenceEquals(loadThread, Thread.CurrentThread))
            {
                if (!loadThread.Join(TimeSpan.FromSeconds(2)))
                {
                    // 超时后不释放后台线程仍可能访问的资源，交由进程退出回收。
                    base.Dispose(disposing);

                    return;
                }
            }

            _loadTimer.Dispose();
            _loadCancellation.Dispose();
            DisposePendingIcons();
            UnregisterThumbnails();

            foreach (Bitmap icon in _icons.Values)
            {
                icon.Dispose();
            }

            _icons.Clear();
            _folderIcon.Dispose();
            _tileFont.Dispose();
            _hintFont.Dispose();
        }

        base.Dispose(disposing);
    }

    #endregion
}
