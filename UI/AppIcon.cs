using System;
using System.Drawing;
using System.IO;

namespace Glix;

/// <summary>
/// 加载应用程序图标供各窗体共用。
/// </summary>
internal static class AppIcon
{
    private static Icon? _cached;

    /// <summary>
    /// 创建应用程序图标副本，调用方负责释放。
    /// </summary>
    /// <returns>图标副本；文件缺失时返回系统默认图标。</returns>
    public static Icon Create()
    {
        if (_cached is null)
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Resources", "glix.ico");

            if (File.Exists(path))
            {
                _cached = new Icon(path);
            }
            else
            {
                _cached = SystemIcons.Application;
            }
        }

        return (Icon)_cached.Clone();
    }
}
