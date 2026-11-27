using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace Glix;

/// <summary>
/// 键盘输入事件数据。
/// </summary>
/// <param name="Message">Windows 消息编号。</param>
/// <param name="VirtualKey">虚拟键码。</param>
internal readonly record struct KeyboardInput(int Message, int VirtualKey);

/// <summary>
/// 基于 WH_KEYBOARD_LL 的全局低级键盘钩子，运行在独立的 STA 消息循环线程上。
/// </summary>
internal sealed class KeyboardHook : IDisposable
{
    #region 字段

    private readonly NativeMethods.LowLevelKeyboardProc _callback;
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
    /// 键盘输入事件，在钩子线程上触发，处理逻辑必须保持轻量。
    /// </summary>
    public event Action<KeyboardInput>? KeyboardInputReceived;

    #endregion

    #region 属性

    /// <summary>
    /// 返回 true 时阻止该键盘消息继续传递给目标窗口。
    /// </summary>
    public Func<KeyboardInput, bool>? SuppressInput { get; set; }

    #endregion

    #region 构造函数

    public KeyboardHook()
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
                Name = "Glix.KeyboardHook"
            };
            _hookThread.SetApartmentState(ApartmentState.STA);
            _hookThread.Start();
        }

        _ready.Wait(TimeSpan.FromSeconds(2));

        if (_installErrorCode != 0)
        {
            throw new InvalidOperationException($"安装键盘钩子失败，Win32 错误码: {_installErrorCode}。");
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
        IntPtr hookHandle = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _callback, moduleHandle, 0);

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
        if (nCode >= 0 && KeyboardInputReceived is not null)
        {
            NativeMethods.KBDLLHOOKSTRUCT info = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
            int message = wParam.ToInt32();

            if (IsTrackedMessage(message))
            {
                KeyboardInput input = new KeyboardInput(message, (int)info.VkCode);
                KeyboardInputReceived.Invoke(input);

                if (SuppressInput?.Invoke(input) == true)
                {
                    return new IntPtr(1);
                }
            }
        }

        return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private static bool IsTrackedMessage(int message)
    {
        return message is NativeMethods.WM_KEYDOWN
            or NativeMethods.WM_KEYUP
            or NativeMethods.WM_SYSKEYDOWN
            or NativeMethods.WM_SYSKEYUP;
    }

    #endregion
}
