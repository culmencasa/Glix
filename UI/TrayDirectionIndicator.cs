using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace Glix;

/// <summary>
/// 生成无背景的描线风格托盘图标：方向箭头与暂停状态的 Zzz 动画。
/// 描边颜色根据系统托盘主题自动切换，兼容深色与浅色任务栏。
/// </summary>
internal sealed class TrayDirectionIndicator : IDisposable
{
    private static readonly int[] IconSizes = { 16, 20, 24, 32, 48 };

    private bool _disposed;

    /// <summary>
    /// 按指定角度创建一个独立拥有的托盘图标，调用方负责 <see cref="Icon.Dispose"/>。
    /// </summary>
    /// <param name="angleDegrees">箭头方向，从正上方顺时针计算的度数。</param>
    /// <returns>新建的图标实例。</returns>
    public Icon CreateIcon(float angleDegrees)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        Color stroke = GetStrokeColor();

        return BuildIcon((size, graphics) => DrawArrow(graphics, size, angleDegrees, stroke));
    }

    /// <summary>
    /// 创建「暂停 / 睡眠」状态图标的一帧：<paramref name="frame"/> 为 0、1、2，对应 z、zz、zzz 的飘动效果。
    /// 调用方负责 <see cref="Icon.Dispose"/>。
    /// </summary>
    /// <param name="frame">动画帧序号（0~2）。</param>
    /// <returns>新建的图标实例。</returns>
    public Icon CreateSleepIcon(int frame)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        Color stroke = GetStrokeColor();
        int count = (frame % 3) + 1;

        return BuildIcon((size, graphics) => DrawZzz(graphics, size, count, stroke));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _disposed = true;
    }

    private static Color GetStrokeColor()
    {
        const string keyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

        using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(keyPath))
        {
            if (key?.GetValue("SystemUsesLightTheme") is int lightTheme)
            {
                return lightTheme == 0 ? Color.White : Color.FromArgb(26, 26, 26);
            }
        }

        return Color.FromArgb(26, 26, 26);
    }

    private static Icon BuildIcon(Action<int, Graphics> draw)
    {
        List<byte[]> images = new List<byte[]>(IconSizes.Length);

        foreach (int size in IconSizes)
        {
            using (Bitmap bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb))
            {
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    graphics.Clear(Color.Transparent);
                    draw(size, graphics);
                }

                using (MemoryStream png = new MemoryStream())
                {
                    bitmap.Save(png, ImageFormat.Png);
                    images.Add(png.ToArray());
                }
            }
        }

        using (MemoryStream iconStream = new MemoryStream())
        {
            using (BinaryWriter writer = new BinaryWriter(iconStream, Encoding.UTF8, true))
            {
                writer.Write((ushort)0);
                writer.Write((ushort)1);
                writer.Write((ushort)images.Count);

                int offset = 6 + (16 * images.Count);

                for (int i = 0; i < images.Count; i++)
                {
                    byte dimension = (byte)IconSizes[i];

                    writer.Write(dimension);
                    writer.Write(dimension);
                    writer.Write((byte)0);
                    writer.Write((byte)0);
                    writer.Write((ushort)1);
                    writer.Write((ushort)32);
                    writer.Write((uint)images[i].Length);
                    writer.Write((uint)offset);
                    offset += images[i].Length;
                }

                foreach (byte[] image in images)
                {
                    writer.Write(image);
                }
            }

            iconStream.Position = 0;

            return new Icon(iconStream);
        }
    }

    private static void DrawArrow(Graphics graphics, int size, float angle, Color stroke)
    {
        float radius = size * 0.44f;
        float headHalf = radius * 0.80f;
        float stemHalf = radius * 0.30f;
        float headBaseY = radius * 0.32f;

        using (GraphicsPath path = new GraphicsPath())
        {
            PointF[] outline =
            {
                new PointF(0f, -radius),
                new PointF(headHalf, headBaseY),
                new PointF(stemHalf, headBaseY),
                new PointF(stemHalf, radius),
                new PointF(-stemHalf, radius),
                new PointF(-stemHalf, headBaseY),
                new PointF(-headHalf, headBaseY)
            };
            path.AddPolygon(outline);
            path.CloseFigure();

            using (Pen pen = CreatePen(stroke, Math.Max(1f, size * 0.085f)))
            {
                graphics.TranslateTransform(size / 2f, size / 2f);
                graphics.RotateTransform(angle);
                graphics.DrawPath(pen, path);
            }
        }
    }

    private static void DrawZzz(Graphics graphics, int size, int count, Color stroke)
    {
        using (Pen pen = CreatePen(stroke, Math.Max(0.8f, size * 0.055f)))
        {
            for (int i = 0; i < count; i++)
            {
                float extent = size * (0.21f + (i * 0.03f));
                float centerX = size * (0.19f + (i * 0.27f));
                float centerY = size * (0.86f - (i * 0.34f));
                float half = extent / 2f;

                PointF[] z =
                {
                    new PointF(centerX - half, centerY - half),
                    new PointF(centerX + half, centerY - half),
                    new PointF(centerX - half, centerY + half),
                    new PointF(centerX + half, centerY + half)
                };
                graphics.DrawLines(pen, z);
            }
        }
    }

    private static Pen CreatePen(Color stroke, float width)
    {
        Pen pen = new Pen(stroke, width)
        {
            LineJoin = LineJoin.Round,
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };

        return pen;
    }
}
