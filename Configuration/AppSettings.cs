using System;
using System.IO;
using System.Text.Json;

namespace Glix;

/// <summary>
/// 用户可调的持久化设置，保存到本地应用数据目录下的 settings.json。
/// </summary>
internal sealed class AppSettings
{
    #region 属性与字段

    /// <summary>
    /// 转圈手势触发所需的最小圈直径（像素）。
    /// </summary>
    public int MinSpanPx { get; set; } = 120;

    /// <summary>
    /// 转圈手势触发所需的最小累计转角（度）。
    /// </summary>
    public int MinTurnDegrees { get; set; } = 240;

    /// <summary>
    /// 是否启用横向 glick 窗口切换（默认关闭）。
    /// </summary>
    public bool TGlickEnabled { get; set; }

    /// <summary>
    /// 横向 glick 触发所需的有效折返次数。
    /// </summary>
    public int TGlickRequiredReversals { get; set; } = 3;

    /// <summary>
    /// 横向 glick 第一程最小位移（像素）。
    /// </summary>
    public int TGlickFirstLegPx { get; set; } = 60;

    /// <summary>
    /// 横向 glick 后续每程最小位移（像素）。
    /// </summary>
    public int TGlickReturnLegPx { get; set; } = 50;

    /// <summary>
    /// 横向 glick 全程纵向最大偏移（像素）。
    /// </summary>
    public int TGlickMaxVerticalPx { get; set; } = 30;

    /// <summary>
    /// 横向 glick 最小峰值速度（像素/秒）。
    /// </summary>
    public int TGlickMinPeakSpeed { get; set; } = 1000;

    /// <summary>
    /// 横向 glick 最大时长（毫秒）。
    /// </summary>
    public int TGlickMaxDurationMs { get; set; } = 320;

    /// <summary>
    /// 横向 glick 触发后的冷却时长（毫秒）。
    /// </summary>
    public int TGlickCooldownMs { get; set; } = 700;

    /// <summary>
    /// 横向 glick 抖动死区（像素）。
    /// </summary>
    public int TGlickJitterPx { get; set; } = 4;

    /// <summary>
    /// 是否输出横向 glick 调试日志。
    /// </summary>
    public bool TGlickDebugLog { get; set; }

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Glix",
        "settings.json");

    #endregion

    #region 公共方法

    /// <summary>
    /// 读取已保存的设置；文件不存在或解析失败时回退到默认值。
    /// </summary>
    /// <returns>设置实例。</returns>
    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                string json = File.ReadAllText(FilePath);
                AppSettings? loaded = JsonSerializer.Deserialize<AppSettings>(json);

                if (loaded is not null)
                {
                    return loaded;
                }
            }
        }
        catch (Exception)
        {
            // 读取失败则回退到默认值。
        }

        return new AppSettings();
    }

    /// <summary>
    /// 将当前设置写入本地应用数据目录。
    /// </summary>
    public void Save()
    {
        try
        {
            string? directory = Path.GetDirectoryName(FilePath);

            if (directory is not null)
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(
                FilePath,
                JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception)
        {
            // 保存失败不影响运行。
        }
    }

    #endregion
}
