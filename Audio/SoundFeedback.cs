using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Microsoft.Win32;

namespace Glix;

/// <summary>
/// 音效来源模式。
/// </summary>
internal enum SoundMode
{
    /// <summary>
    /// 交给 Windows 系统自身在导航时播放，程序不再重复发声。
    /// </summary>
    System,

    /// <summary>
    /// 使用程序内置的方波提示音（老式 PC 蜂鸣器音色），同时把系统导航音静音以避免重复。
    /// 宿主默认启用此模式。
    /// </summary>
    Custom
}

/// <summary>
/// 一次提示音：频率（Hz）、持续时长（毫秒）与播放后的间隔（毫秒）。
/// </summary>
/// <param name="FrequencyHz">方波频率。</param>
/// <param name="DurationMs">持续时长。</param>
/// <param name="GapMs">播放完成后的间隔。</param>
internal readonly record struct BeepNote(uint FrequencyHz, uint DurationMs, int GapMs = 0);

/// <summary>
/// 方波提示音播放器。所有声音均由同一套 SDL 音频振荡器产生并通过声卡播放，
/// 音源唯一、音量稳定；专用线程控制「开始 → 持续 → 停止」的时序，串行播放保证每次触发都完整发声。
/// </summary>
internal sealed class SoundFeedback : IDisposable
{
    #region 字段与常量

    // 后退（顺时针）：低音方波；前进（逆时针）：高音方波。8-bit 音色，靠音高区分方向。
    private static readonly BeepNote BackwardNote = new BeepNote(523, 100);
    private static readonly BeepNote ForwardNote = new BeepNote(784, 100);

    // 窗口切换反馈音：与前进/后退音调明显区分的独立高音。
    private static readonly BeepNote SwitchNote = new BeepNote(1047, 130);

    // 启动节奏音：经典游戏机上行的方波音阶，短音渐高后以稍长音收尾。
    private static readonly BeepNote[] StartupNotes =
    {
        new BeepNote(523, 70, 55),
        new BeepNote(659, 70, 55),
        new BeepNote(784, 70, 55),
        new BeepNote(1047, 70, 55),
        new BeepNote(1319, 160, 0)
    };

    private readonly object _sync = new object();
    private readonly object _muteSync = new object();

    private static readonly string[] NavigationSoundEvents = { "GoForward", "GoBackward", "GoBack", "Navigating" };
    private const string SoundSchemeRoot = @"Software\AppEvents\Schemes\Apps\Explorer";
    private static readonly string MuteStateFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Glix",
        "sound-mute-state.txt");

    private readonly ConcurrentQueue<BeepNote> _playQueue = new ConcurrentQueue<BeepNote>();
    private readonly ManualResetEventSlim _playReady = new ManualResetEventSlim(false);
    private readonly Thread _playThread;
    private volatile bool _stopPlayback;

    // 持续方波蜂鸣器；SDL 音频初始化失败（如无可用音频设备）时为 null，播放时静默降级。
    private readonly PitSpeakerBeeper? _beeper;

    private Dictionary<string, (object Value, RegistryValueKind Kind)>? _mutedOriginals;
    private bool _enabled = true;
    private bool _disposed;
    // 宿主（TrayAppContext）会在启动时显式设置为 Custom，从而触发系统导航音静音，避免与内置音双响。
    private SoundMode _mode = SoundMode.System;

    #endregion

    #region 构造与初始化

    public SoundFeedback()
    {
        try
        {
            _beeper = new PitSpeakerBeeper();
        }
        catch (Exception)
        {
            // SDL 音频不可用时静默降级为不发声，不因此中断手势导航主流程。
            _beeper = null;
        }

        _playThread = new Thread(PlaybackLoop)
        {
            IsBackground = true,
            Name = "Glix.Sound"
        };
        _playThread.Start();

        RestoreMutedStateIfAny();
    }

    #endregion

    #region 属性

    /// <summary>
    /// 是否播放提示音。
    /// </summary>
    public bool Enabled
    {
        get
        {
            lock (_sync)
            {
                return _enabled;
            }
        }

        set
        {
            lock (_sync)
            {
                _enabled = value;
            }
        }
    }

    /// <summary>
    /// 音效来源模式。
    /// </summary>
    public SoundMode Mode
    {
        get
        {
            lock (_sync)
            {
                return _mode;
            }
        }

        set
        {
            SoundMode previous;
            lock (_sync)
            {
                previous = _mode;
                _mode = value;
            }

            if (previous == value)
            {
                return;
            }

            if (value == SoundMode.Custom)
            {
                MuteSystemNavigationSounds();
            }
            else
            {
                UnmuteSystemNavigationSounds();
            }
        }
    }

    #endregion

    #region 播放入口

    /// <summary>
    /// 播放后退提示音（低音方波）。
    /// </summary>
    public void PlayBackward()
    {
        if (!Enabled)
        {
            return;
        }

        // System 模式交给资源管理器自身发声，程序不重复播放。
        if (_mode == SoundMode.System)
        {
            return;
        }

        Enqueue(BackwardNote);
    }

    /// <summary>
    /// 播放前进提示音（高音方波）。
    /// </summary>
    public void PlayForward()
    {
        if (!Enabled)
        {
            return;
        }

        // System 模式交给资源管理器自身发声，程序不重复播放。
        if (_mode == SoundMode.System)
        {
            return;
        }

        Enqueue(ForwardNote);
    }

    /// <summary>
    /// 播放窗口切换反馈音（独立高音，区别于前进/后退音调）。
    /// </summary>
    public void PlaySwitch()
    {
        if (!Enabled)
        {
            return;
        }

        Enqueue(SwitchNote);
    }

    /// <summary>
    /// 播放程序启动节奏音（经典游戏机开机风格的上行方波音阶）。仅受提示音开关约束。
    /// </summary>
    public void PlayStartupSequence()
    {
        if (!Enabled)
        {
            return;
        }

        foreach (BeepNote note in StartupNotes)
        {
            _playQueue.Enqueue(note);
        }

        _playReady.Set();
    }

    #endregion

    #region 系统导航音静音管理

    /// <summary>
    /// 取消勾选「使用系统导航音效」时，将系统导航声音在注册表中置为无声，避免系统自身也播放一遍。
    /// 仅处理确实已存在的事件项，并缓存原值到内存与本地文件以便还原（含崩溃恢复）。
    /// </summary>
    private void MuteSystemNavigationSounds()
    {
        Dictionary<string, (object Value, RegistryValueKind Kind)> originals = new Dictionary<string, (object Value, RegistryValueKind Kind)>();
        foreach (string eventName in NavigationSoundEvents)
        {
            using RegistryKey? key = OpenNavigationSoundKey(eventName, writable: true);
            if (key is null)
            {
                continue;
            }

            object? value = key.GetValue(null);
            if (value is null)
            {
                continue;
            }

            originals[eventName] = (value, key.GetValueKind(null));
            key.SetValue(null, string.Empty, RegistryValueKind.String);
        }

        lock (_muteSync)
        {
            _mutedOriginals = originals;
        }

        PersistMutedState(originals);
    }

    /// <summary>
    /// 还原被本程序静音的系统导航声音（重新勾选系统音效、程序退出或启动时恢复均调用）。
    /// </summary>
    private void UnmuteSystemNavigationSounds()
    {
        Dictionary<string, (object Value, RegistryValueKind Kind)>? originals;
        lock (_muteSync)
        {
            originals = _mutedOriginals;
            _mutedOriginals = null;
        }

        if (originals is null)
        {
            originals = LoadMutedState();
        }

        if (originals is null)
        {
            return;
        }

        foreach (KeyValuePair<string, (object Value, RegistryValueKind Kind)> entry in originals)
        {
            using RegistryKey? key = OpenNavigationSoundKey(entry.Key, writable: true);
            if (key is null)
            {
                continue;
            }

            key.SetValue(null, entry.Value.Value, entry.Value.Kind);
        }

        DeleteMutedStateFile();
    }

    /// <summary>
    /// 程序启动时调用：若上次（可能崩溃）遗留了被静音的系统导航声音记录，则还原之。
    /// </summary>
    private void RestoreMutedStateIfAny()
    {
        Dictionary<string, (object Value, RegistryValueKind Kind)>? originals = LoadMutedState();
        if (originals is null)
        {
            return;
        }

        foreach (KeyValuePair<string, (object Value, RegistryValueKind Kind)> entry in originals)
        {
            using RegistryKey? key = OpenNavigationSoundKey(entry.Key, writable: true);
            if (key is null)
            {
                continue;
            }

            key.SetValue(null, entry.Value.Value, entry.Value.Kind);
        }

        DeleteMutedStateFile();
    }

    private void PersistMutedState(Dictionary<string, (object Value, RegistryValueKind Kind)> originals)
    {
        if (originals.Count == 0)
        {
            DeleteMutedStateFile();
            return;
        }

        string directory = Path.GetDirectoryName(MuteStateFile)!;
        Directory.CreateDirectory(directory);

        using StreamWriter writer = new StreamWriter(MuteStateFile, append: false);
        foreach (KeyValuePair<string, (object Value, RegistryValueKind Kind)> entry in originals)
        {
            writer.WriteLine($"{entry.Key}|{(int)entry.Value.Kind}|{entry.Value.Value}");
        }
    }

    private static Dictionary<string, (object Value, RegistryValueKind Kind)>? LoadMutedState()
    {
        if (!File.Exists(MuteStateFile))
        {
            return null;
        }

        Dictionary<string, (object Value, RegistryValueKind Kind)> originals = new Dictionary<string, (object Value, RegistryValueKind Kind)>();
        foreach (string line in File.ReadAllLines(MuteStateFile))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            string[] parts = line.Split('|');
            if (parts.Length != 3 || !Enum.TryParse<RegistryValueKind>(parts[1], out RegistryValueKind kind))
            {
                continue;
            }

            originals[parts[0]] = (parts[2], kind);
        }

        return originals.Count == 0 ? null : originals;
    }

    private static void DeleteMutedStateFile()
    {
        try
        {
            File.Delete(MuteStateFile);
        }
        catch (Exception)
        {
            // 删除失败不影响主流程。
        }
    }

    private static RegistryKey? OpenNavigationSoundKey(string eventName, bool writable)
    {
        return Registry.CurrentUser.OpenSubKey($@"{SoundSchemeRoot}\{eventName}\.Current", writable);
    }

    #endregion

    #region 释放

    /// <summary>
    /// 释放播放线程与音频设备，并还原被静音的系统导航声音。
    /// </summary>
    public void Dispose()
    {
        UnmuteSystemNavigationSounds();

        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _stopPlayback = true;
        _playReady.Set();
        _playThread.Join(TimeSpan.FromSeconds(2));
        _playReady.Dispose();

        _beeper?.Dispose();
    }

    #endregion

    #region 播放线程

    /// <summary>
    /// 将一次提示音入队，由专用线程控制时序播放。
    /// 最新覆盖策略：新触发时丢弃尚未播放的旧音，只保留最新一次，
    /// 避免连续画圈造成一串提示音排队积压。
    /// </summary>
    private void Enqueue(BeepNote note)
    {
        while (_playQueue.TryDequeue(out _))
        {
        }

        _playQueue.Enqueue(note);
        _playReady.Set();
    }

    /// <summary>
    /// 专用播放线程：对每个音符执行「开始发声 → 持续 → 停止」的时序控制。
    /// 音源唯一、音量稳定；串行播放保证每段声音完整响完，不会被后续调用中断。
    /// 启动节奏音的音符间隔也在此线程中等待。
    /// </summary>
    private void PlaybackLoop()
    {
        while (!_stopPlayback)
        {
            _playReady.Wait(TimeSpan.FromMilliseconds(100));
            _playReady.Reset();

            while (_playQueue.TryDequeue(out BeepNote note))
            {
                if (_stopPlayback)
                {
                    return;
                }

                if (_beeper is not null)
                {
                    _beeper.Play(note.FrequencyHz);
                    Thread.Sleep((int)note.DurationMs);
                    _beeper.Stop();
                }

                if (note.GapMs > 0)
                {
                    Thread.Sleep(note.GapMs);
                }
            }
        }
    }

    #endregion
}
