using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace Glix;

/// <summary>
/// 生成并缓存跟随鼠标方向的托盘箭头图标（蓝底白箭头，复刻 PopMouse 观感），
/// 以及「暂停 / 睡眠」状态的 Zzz 动画图标。图标按方向实时生成，调用方负责释放上一代图标。
/// </summary>
internal sealed class TrayDirectionIndicator : IDisposable
{
    #region 字段

    private bool _disposed;

    #endregion

    #region 公共方法

    /// <summary>
    /// 按指定方向创建一个独立拥有的托盘图标，调用方负责 <see cref="Icon.Dispose"/>。
    /// </summary>
    /// <param name="direction">目标方向。</param>
    /// <returns>新建的图标实例。</returns>
    public Icon CreateIcon(ScreenDirection direction)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        float angle = direction switch
        {
            ScreenDirection.Up => 0f,
            ScreenDirection.Right => 90f,
            ScreenDirection.Down => 180f,
            ScreenDirection.Left => 270f,
            _ => 0f
        };

        using (Bitmap bitmap = BuildArrowBitmap(angle))
        {
            return ToOwnedIcon(bitmap);
        }
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

        const int size = 32;

        using (Bitmap bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb))
        {
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.TextRenderingHint = TextRenderingHint.AntiAlias;
                graphics.Clear(Color.Transparent);

                DrawRoundedBackground(graphics, size);

                int count = (frame % 3) + 1;

                using (SolidBrush brush = new SolidBrush(Color.White))
                {
                    // 由下往上、由小变大依次叠出 z，模拟睡意飘散。
                    for (int i = 0; i < count; i++)
                    {
                        float fontPx = 10f + (i * 4f);

                        using (Font font = new Font("Segoe UI", fontPx, FontStyle.Bold))
                        {
                            graphics.DrawString("z", font, brush, 6f + (i * 6f), 18f - (i * 7f));
                        }
                    }
                }
            }

            return ToOwnedIcon(bitmap);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _disposed = true;
    }

    #endregion

    #region 私有方法

    private static Icon ToOwnedIcon(Bitmap bitmap)
    {
        IntPtr handle = bitmap.GetHicon();

        Icon owned;

        using (Icon fromHandle = Icon.FromHandle(handle))
        {
            owned = (Icon)fromHandle.Clone();
        }

        NativeMethods.DestroyIcon(handle);

        return owned;
    }

    private static Bitmap BuildArrowBitmap(float angleDegrees)
    {
        const int size = 32;

        Bitmap bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);

        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);

            DrawRoundedBackground(graphics, size);

            // 白色箭头：以「向上」为基准绘制后整体旋转到目标方向。
            graphics.TranslateTransform(size / 2f, size / 2f);
            graphics.RotateTransform(angleDegrees);

            float pad = 2f;
            float usable = size - (pad * 2f) - 4f;
            float headHalf = usable * 0.32f;
            float headHeight = usable * 0.40f;
            float barHalf = usable * 0.22f;
            float barHeight = usable * 0.34f;
            float top = -usable / 2f;

            using (GraphicsPath arrowPath = new GraphicsPath())
            {
                arrowPath.AddPolygon(new[]
                {
                    new PointF(0f, top),
                    new PointF(-headHalf, top + headHeight),
                    new PointF(headHalf, top + headHeight)
                });
                arrowPath.AddRectangle(new RectangleF(-barHalf, top + headHeight, barHalf * 2f, barHeight));

                using (SolidBrush brush = new SolidBrush(Color.White))
                {
                    graphics.FillPath(brush, arrowPath);
                }
            }
        }

        return bitmap;
    }

    private static void DrawRoundedBackground(Graphics graphics, int size)
    {
        float pad = 2f;
        float radius = 7f;
        RectangleF backgroundRect = new RectangleF(pad, pad, size - (pad * 2f), size - (pad * 2f));

        using (GraphicsPath backgroundPath = RoundedRectangle(backgroundRect, radius))
        using (SolidBrush backgroundBrush = new SolidBrush(Color.FromArgb(45, 125, 210)))
        {
            graphics.FillPath(backgroundBrush, backgroundPath);
        }
    }

    private static GraphicsPath RoundedRectangle(RectangleF rect, float radius)
    {
        GraphicsPath path = new GraphicsPath();
        float diameter = radius * 2f;

        path.AddArc(rect.X, rect.Y, diameter, diameter, 180f, 90f);
        path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270f, 90f);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0f, 90f);
        path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90f, 90f);
        path.CloseFigure();

        return path;
    }

    #endregion
}
