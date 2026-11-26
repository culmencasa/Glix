using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Silk.NET.SDL;

namespace Glix;

/// <summary>
/// 基于 SDL 音频的持续方波蜂鸣器，模拟老式 PC 蜂鸣器（PC Speaker）音色。
/// 内部托管 SDL 音频设备、回调与振荡器状态；<see cref="Play(double)"/> / <see cref="Stop()"/>
/// 线程安全，外部可任意多线程调用。自带 Attack/Release 包络、全局连续相位与 RC 低通，
/// 起音干脆、收尾平滑无毛刺。不阻塞主线程，用完 <see cref="Dispose"/>。
/// </summary>
internal sealed class PitSpeakerBeeper : IDisposable
{
    private const int SampleRate = 44100;
    private const byte Channels = 1;
    private const short BitsPerSample = 16;
    private const ushort BufferSamples = 512;

    // SDL 音频格式：AUDIO_S16LSB（16 位有符号小端）。
    private const ushort FormatS16LSB = 0x8010;

    private const double AttackMs = 4d;
    private const double ReleaseMs = 12d;

    private readonly double _attackStep;
    private readonly double _releaseStep;
    private readonly object _lock = new object();

    // 振荡器状态（由 SDL 音频回调线程访问，通过 _lock 与主线程协调目标值）。
    private double _phase;
    private double _frequency;
    private double _amp;
    private double _targetAmp;
    private double _filterState;

    private Sdl? _sdl;
    private uint _audioDevice;
    private GCHandle _selfHandle;
    private bool _disposed;

    public unsafe PitSpeakerBeeper()
    {
        _attackStep = 1d / (AttackMs / 1000d * SampleRate);
        _releaseStep = 1d / (ReleaseMs / 1000d * SampleRate);

        Sdl sdl = Sdl.GetApi();
        if (sdl.Init(Sdl.InitAudio) < 0)
        {
            throw new InvalidOperationException($"SDL InitAudio failed: {sdl.GetErrorS()}");
        }

        // 通过 GCHandle 把 this 经 Userdata 传给音频回调，使函数指针回调能访问实例状态。
        _selfHandle = GCHandle.Alloc(this);

        AudioSpec want = new AudioSpec
        {
            Freq = SampleRate,
            Format = FormatS16LSB,
            Channels = Channels,
            Samples = BufferSamples,
            Callback = new PfnAudioCallback(&AudioCallback),
            Userdata = (void*)GCHandle.ToIntPtr(_selfHandle)
        };

        uint device = sdl.OpenAudioDevice((byte*)null, 0, &want, null, 0);
        if (device == 0)
        {
            string error = sdl.GetErrorS();
            _selfHandle.Free();
            sdl.Quit();
            throw new InvalidOperationException($"OpenAudioDevice failed: {error}");
        }

        _audioDevice = device;
        _sdl = sdl;

        // 启动音频流（pause_on = 0 表示开始播放）。
        sdl.PauseAudioDevice(_audioDevice, 0);
    }

    /// <summary>
    /// 开始蜂鸣，指定频率（Hz）；传入 0 或负数等价于 <see cref="Stop"/>。
    /// </summary>
    /// <param name="hz">方波频率。</param>
    public void Play(double hz)
    {
        if (_disposed)
        {
            return;
        }

        lock (_lock)
        {
            if (hz <= 0d)
            {
                _targetAmp = 0d;
                return;
            }

            _frequency = hz;
            _targetAmp = 0.85d;
        }
    }

    /// <summary>
    /// 停止蜂鸣（走 Release 衰减，无毛刺）。
    /// </summary>
    public void Stop()
    {
        Play(0d);
    }

    /// <summary>
    /// SDL 音频回调（C 调用约定函数指针）。从 Userdata 恢复实例后委托给实例渲染。
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static unsafe void AudioCallback(void* userdata, byte* stream, int len)
    {
        GCHandle handle = GCHandle.FromIntPtr((IntPtr)userdata);
        PitSpeakerBeeper self = (PitSpeakerBeeper)handle.Target!;
        self.RenderAudio(stream, len);
    }

    private unsafe void RenderAudio(byte* stream, int len)
    {
        int sampleCount = len / (BitsPerSample / 8);
        short* p = (short*)stream;

        double frequency;
        double targetAmp;
        lock (_lock)
        {
            frequency = _frequency;
            targetAmp = _targetAmp;
        }

        for (int i = 0; i < sampleCount; i++)
        {
            // Attack/Release 包络平滑。
            if (targetAmp > _amp)
            {
                _amp += _attackStep;
                if (_amp > targetAmp)
                {
                    _amp = targetAmp;
                }
            }
            else if (targetAmp < _amp)
            {
                _amp -= _releaseStep;
                if (_amp < 0d)
                {
                    _amp = 0d;
                }
            }

            if (_amp <= 0d)
            {
                *p++ = 0;
                continue;
            }

            // 原始方波，相位全局连续，避免换频瞬间相位跳变产生爆音。
            double phaseInc = frequency * 2d * Math.PI / SampleRate;
            double rawWave = _phase % (2d * Math.PI) < Math.PI ? 1d : -1d;
            _phase += phaseInc;

            // RC 低通，模拟老式 PC Speaker 的 RC 滤波，柔化硬方波。
            const double alpha = 0.025d;
            _filterState += alpha * (rawWave - _filterState);

            double output = _filterState * _amp * 28000d;
            *p++ = (short)Math.Clamp(output, short.MinValue, short.MaxValue);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
        System.Threading.Thread.Sleep((int)(ReleaseMs + 20d)); // 等尾音衰减完再关设备。

        if (_sdl is not null)
        {
            _sdl.PauseAudioDevice(_audioDevice, 1);
            _sdl.CloseAudioDevice(_audioDevice);
            _sdl.Quit();
        }

        _sdl = null;

        if (_selfHandle.IsAllocated)
        {
            _selfHandle.Free();
        }
    }
}
