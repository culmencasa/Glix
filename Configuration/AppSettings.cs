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
