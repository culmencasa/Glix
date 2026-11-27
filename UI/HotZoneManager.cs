using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Glix;

internal readonly record struct HotZoneHit(IntPtr WindowHandle, Rectangle Placement, HotZoneKind Kind);

/// <summary>
/// 为每个可见的资源管理器窗口维护一对跟随标题栏的常驻按钮：置顶与切换器。
/// </summary>
internal sealed class HotZoneManager : IDisposable
{
    private const int ZoneLogicalSize = 28;
    private const int ZoneGapLogical = 2;
    private const int TitleBarLogicalHeight = 34;
    private const int CaptionButtonLogicalWidth = 46;
    private const int CaptionButtonCount = 3;
    private const int RightMarginLogical = 8;
    private const int TopMarginLogical = 1;

    private readonly Dictionary<IntPtr, HotZoneForm> _zones = new Dictionary<IntPtr, HotZoneForm>();
    private readonly Dictionary<IntPtr, HotZoneForm> _pinZones = new Dictionary<IntPtr, HotZoneForm>();
    private readonly Dictionary<IntPtr, Rectangle> _placements = new Dictionary<IntPtr, Rectangle>();
    private readonly Dictionary<IntPtr, Rectangle> _pinPlacements = new Dictionary<IntPtr, Rectangle>();
    private readonly object _placementSync = new object();
    private readonly System.Windows.Forms.Timer _timer = new System.Windows.Forms.Timer();
    private readonly Action<Rectangle> _onClick;
    private readonly uint _appProcessId;
    private IntPtr _activeHandle;
    private bool _disposed;

    public HotZoneManager(Action<Rectangle> onClick)
    {
        _onClick = onClick;
        _appProcessId = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;

        _timer.Interval = 30;
        _timer.Tick += OnTick;
        _timer.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        List<IntPtr> live = new List<IntPtr>();
        IntPtr foreground = NativeMethods.GetForegroundWindow();
        NativeMethods.GetWindowThreadProcessId(foreground, out uint foregroundPid);

        if (foregroundPid == _appProcessId)
        {
            // 切换器或按钮自身获得焦点时，保留上次激活的资源管理器窗口。
        }
        else
        {
            IntPtr root = NativeMethods.GetAncestor(foreground, NativeMethods.GA_ROOT);
            string rootClass = WindowInspector.GetClassName(root);
            _activeHandle = rootClass is "CabinetWClass" or "ExplorerWClass" ? root : IntPtr.Zero;
        }

        foreach (ExplorerWindow window in ExplorerWindowEnumerator.GetWindows())
        {
            if (!NativeMethods.IsWindow(window.Handle) || NativeMethods.IsIconic(window.Handle))
            {
                continue;
            }

            if (!NativeMethods.GetWindowRect(window.Handle, out NativeMethods.RECT rect))
            {
                continue;
            }

            live.Add(window.Handle);

            if (!_zones.TryGetValue(window.Handle, out HotZoneForm? zone))
            {
                zone = new HotZoneForm(_onClick, HotZoneKind.Switcher);
                zone.AttachTo(window.Handle);
                _zones.Add(window.Handle, zone);
            }

            if (!_pinZones.TryGetValue(window.Handle, out HotZoneForm? pinZone))
            {
                pinZone = new HotZoneForm(_onClick, HotZoneKind.TopMost);
                pinZone.AttachTo(window.Handle);
                _pinZones.Add(window.Handle, pinZone);
            }

            float scale = NativeMethods.GetDpiForWindow(window.Handle) / 96f;
            int size = (int)Math.Round(ZoneLogicalSize * scale);
            int caption = (int)Math.Round(TitleBarLogicalHeight * scale);
            int x = rect.Right - (int)Math.Round((CaptionButtonLogicalWidth * CaptionButtonCount + RightMarginLogical) * scale) - size;
            int pinX = x - (int)Math.Round(ZoneGapLogical * scale) - size;
            int titleTop = rect.Top;

            if (NativeMethods.IsZoomed(window.Handle))
            {
                // 最大化时窗口外框延伸到屏幕外，标题栏实际从工作区顶部开始。
                titleTop = Screen.FromHandle(window.Handle).WorkingArea.Top;
            }

            int y = titleTop + (caption - size) / 2 + (int)Math.Round(TopMarginLogical * scale);
            Rectangle placement = new Rectangle(x, y, size, size);
            Rectangle pinPlacement = new Rectangle(pinX, y, size, size);

            bool needsPlace;
            bool needsPinPlace;
            lock (_placementSync)
            {
                needsPlace = !_placements.TryGetValue(window.Handle, out Rectangle previous) || previous != placement;
                needsPinPlace = !_pinPlacements.TryGetValue(window.Handle, out Rectangle previousPin) || previousPin != pinPlacement;
            }

            if (needsPlace)
            {
                zone.PlaceAt(placement.Location, placement.Size);

                lock (_placementSync)
                {
                    _placements[window.Handle] = placement;
                }
            }

            if (needsPinPlace)
            {
                pinZone.PlaceAt(pinPlacement.Location, pinPlacement.Size);

                lock (_placementSync)
                {
                    _pinPlacements[window.Handle] = pinPlacement;
                }
            }

            bool active = _activeHandle == window.Handle;

            if (active && !zone.Visible)
            {
                zone.Show();
            }
            else if (!active && zone.Visible)
            {
                zone.Hide();
            }

            if (active && !pinZone.Visible)
            {
                pinZone.Show();
            }
            else if (!active && pinZone.Visible)
            {
                pinZone.Hide();
            }

            bool pinned = (NativeMethods.GetWindowLongPtr(window.Handle, NativeMethods.GWL_EXSTYLE).ToInt64()
                & NativeMethods.WS_EX_TOPMOST) != 0;
            pinZone.SetPinned(pinned);
        }

        List<IntPtr> stale = new List<IntPtr>();

        foreach (IntPtr handle in _zones.Keys)
        {
            if (!live.Contains(handle))
            {
                stale.Add(handle);
            }
        }

        foreach (IntPtr handle in stale)
        {
            _zones[handle].Close();
            _zones[handle].Dispose();
            _zones.Remove(handle);
            _pinZones[handle].Close();
            _pinZones[handle].Dispose();
            _pinZones.Remove(handle);

            lock (_placementSync)
            {
                _placements.Remove(handle);
                _pinPlacements.Remove(handle);
            }
        }

        UpdateHover();
    }

    private void UpdateHover()
    {
        if (!NativeMethods.GetCursorPos(out NativeMethods.POINT cursor))
        {
            return;
        }

        Point point = new Point(cursor.X, cursor.Y);

        foreach (KeyValuePair<IntPtr, HotZoneForm> entry in _zones)
        {
            bool hit = false;

            lock (_placementSync)
            {
                if (_placements.TryGetValue(entry.Key, out Rectangle placement))
                {
                    hit = placement.Contains(point);
                }
            }

            entry.Value.SetHover(hit);
        }

        foreach (KeyValuePair<IntPtr, HotZoneForm> entry in _pinZones)
        {
            bool hit = false;

            lock (_placementSync)
            {
                if (_pinPlacements.TryGetValue(entry.Key, out Rectangle placement))
                {
                    hit = placement.Contains(point);
                }
            }

            entry.Value.SetHover(hit);
        }
    }

    /// <summary>
    /// 判断屏幕坐标是否落在任一热区按钮内。
    /// </summary>
    /// <param name="point">屏幕坐标。</param>
    /// <returns>命中按钮返回 <c>true</c>。</returns>
    public bool Contains(Point point)
    {
        return TryHit(point, out _);
    }

    /// <summary>
    /// 判断屏幕坐标是否命中某个热区按钮，并返回按钮类型、所属窗口与物理矩形。
    /// </summary>
    /// <param name="point">物理屏幕坐标。</param>
    /// <param name="hit">命中的按钮信息。</param>
    /// <returns>命中返回 <c>true</c>。</returns>
    public bool TryHit(Point point, out HotZoneHit hit)
    {
        lock (_placementSync)
        {
            foreach (KeyValuePair<IntPtr, Rectangle> pair in _placements)
            {
                if (pair.Value.Contains(point))
                {
                    hit = new HotZoneHit(pair.Key, pair.Value, HotZoneKind.Switcher);

                    return true;
                }
            }

            foreach (KeyValuePair<IntPtr, Rectangle> pair in _pinPlacements)
            {
                if (pair.Value.Contains(point))
                {
                    hit = new HotZoneHit(pair.Key, pair.Value, HotZoneKind.TopMost);

                    return true;
                }
            }
        }

        hit = default;

        return false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Stop();
        _timer.Dispose();

        foreach (HotZoneForm zone in _zones.Values)
        {
            zone.Close();
            zone.Dispose();
        }

        foreach (HotZoneForm zone in _pinZones.Values)
        {
            zone.Close();
            zone.Dispose();
        }

        _zones.Clear();
        _pinZones.Clear();

        lock (_placementSync)
        {
            _placements.Clear();
            _pinPlacements.Clear();
        }
    }
}
