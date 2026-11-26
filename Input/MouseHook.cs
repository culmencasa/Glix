using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace Glix;

/// <summary>
/// 鼠标输入事件数据。
/// </summary>
/// <param name="Message">Windows 消息编号。</param>
/// <param name="X">屏幕坐标 X。</param>
/// <param name="Y">屏幕坐标 Y。</param>
internal readonly record struct MouseInput(int Message, int X, int Y);

/// <summary>
/// 基于 WH_MOUSE_LL 的全局低级鼠标钩子，运行在独立的 STA 消息循环线程上。
/// </summary>
internal sealed class MouseHook : IDisposable
{
    #region 字段

    private readonly NativeMethods.LowLevelMouseProc _callback;
    private readonly ManualResetEventSlim _ready = new ManualResetEventSlim(false);
    private readonly object _sync = new object();

    private Thread? _hookThread;
    private ApplicationContext? _loopContext;
    private IntPtr _hookHandle;
    private int _installErrorCode;
    private bool _disposed;

    #endregion

    #region 事件

    /// <summary>
    /// 鼠标输入事件，在钩子线程上触发，处理逻辑必须保持轻量。
    /// </summary>
    public event Action<MouseInput>? MouseInputReceived;

    #endregion

    #region 构造函数

    public MouseHook()
    {
        _callback = HookCallback;
    }

    #endregion

    #region 公共方法

    /// <summary>
    /// 安装钩子并启动消息循环线程。
    /// </summary>
    /// <exception cref="InvalidOperationException">钩子安装失败时抛出。</exception>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_sync)
        {
            if (_hookThread is not null)
            {
                return;
            }

            _hookThread = new Thread(HookThreadProc)
            {
                IsBackground = true,
                Name = "Glix.MouseHook"
            };
            _hookThread.SetApartmentState(ApartmentState.STA);
            _hookThread.Start();
        }

        _ready.Wait(TimeSpan.FromSeconds(2));

        if (_installErrorCode != 0)
        {
            throw new InvalidOperationException($"安装鼠标钩子失败，Win32 错误码: {_installErrorCode}。");
        }
    }

    /// <summary>
    /// 卸载钩子并以有界等待结束消息循环线程。
    /// </summary>
    public void Dispose()
    {
        Thread? thread;
        ApplicationContext? loopContext;

        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            thread = _hookThread;
            loopContext = _loopContext;
        }

        if (thread is null)
        {
            _ready.Dispose();
            return;
        }

        _ready.Wait(TimeSpan.FromSeconds(2));
        loopContext?.ExitThread();

        if (!thread.Join(TimeSpan.FromSeconds(3)))
        {
            // 超时后不再释放任何托管资源，后台线程会随进程退出而结束。
            _ready.Dispose();
            return;
        }

        _ready.Dispose();
    }

    #endregion

    #region 私有方法

    private void HookThreadProc()
    {
        IntPtr moduleHandle = NativeMethods.GetModuleHandle(null);
        IntPtr hookHandle = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _callback, moduleHandle, 0);

        if (hookHandle == IntPtr.Zero)
        {
            _installErrorCode = Marshal.GetLastWin32Error();
            _ready.Set();
            return;
        }

        ApplicationContext loopContext = new ApplicationContext();

        lock (_sync)
        {
            _hookHandle = hookHandle;
            _loopContext = loopContext;
        }

        _ready.Set();
        Application.Run(loopContext);

        if (!NativeMethods.UnhookWindowsHookEx(hookHandle))
        {
            // 卸载失败通常意味着进程即将退出，此处不做额外处理。
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && MouseInputReceived is not null)
        {
            NativeMethods.MSLLHOOKSTRUCT info = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
            int message = wParam.ToInt32();

            if (IsTrackedMessage(message))
            {
                MouseInput input = new MouseInput(message, info.Pt.X, info.Pt.Y);
                MouseInputReceived.Invoke(input);
            }
        }

        return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private static bool IsTrackedMessage(int message)
    {
        return message is NativeMethods.WM_MOUSEMOVE
            or NativeMethods.WM_LBUTTONDOWN
            or NativeMethods.WM_LBUTTONUP
            or NativeMethods.WM_RBUTTONDOWN
            or NativeMethods.WM_RBUTTONUP
            or NativeMethods.WM_MBUTTONDOWN
            or NativeMethods.WM_MBUTTONUP
            or NativeMethods.WM_XBUTTONDOWN
            or NativeMethods.WM_XBUTTONUP;
    }

    #endregion
}
