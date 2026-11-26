using System;
using System.Collections.Generic;

namespace Glix;

/// <summary>
/// 识别出的绕行方向。
/// </summary>
internal enum CircleDirection
{
    /// <summary>
    /// 尚未识别出方向。
    /// </summary>
    None,

    /// <summary>
    /// 顺时针（屏幕坐标系）。
    /// </summary>
    Clockwise,

    /// <summary>
    /// 逆时针（屏幕坐标系）。
    /// </summary>
    CounterClockwise
}

/// <summary>
/// 轨迹点。
/// </summary>
/// <param name="X">屏幕坐标 X。</param>
/// <param name="Y">屏幕坐标 Y。</param>
/// <param name="TimestampMs">时间戳（毫秒）。</param>
internal readonly record struct GesturePoint(int X, int Y, long TimestampMs);

/// <summary>
/// 累积鼠标轨迹并识别顺/逆时针画圈手势。
/// </summary>
internal sealed class CircleGestureTracker
{
    #region 字段

    private readonly GestureOptions _options;
    private readonly List<GesturePoint> _points = new List<GesturePoint>();
    private long _gestureStartMs;

    #endregion

    #region 构造函数

    public CircleGestureTracker()
        : this(new GestureOptions())
    {
    }

    public CircleGestureTracker(GestureOptions options)
    {
        _options = options;
    }

    #endregion

    #region 公共方法

    /// <summary>
    /// 追加一个鼠标位置，并判断是否已构成一次画圈手势。
    /// </summary>
    /// <returns>识别出的方向；未触发时为 <see cref="CircleDirection.None"/>。</returns>
    public CircleDirection TryDetect(int x, int y, long timestampMs)
    {
        ResetIfIdle(timestampMs);

        if (_points.Count == 0)
        {
            _gestureStartMs = timestampMs;
        }

        // 硬性时限：从起笔到当前超过配置时长即整段作废，必须重新起笔，避免慢动作被裁剪后仍触发。
        if (timestampMs - _gestureStartMs > _options.MaxGestureDurationMs)
        {
            Reset();
            return CircleDirection.None;
        }

        AppendPoint(x, y, timestampMs);

        return Evaluate();
    }

    public void Reset()
    {
        _points.Clear();
        _gestureStartMs = 0;
    }

    #endregion

    #region 私有方法

    private void ResetIfIdle(long timestampMs)
    {
        if (_points.Count == 0)
        {
            return;
        }

        long idleTime = timestampMs - _points[^1].TimestampMs;

        if (idleTime > _options.IdleResetMs || idleTime < 0)
        {
            _points.Clear();
        }
    }

    private void AppendPoint(int x, int y, long timestampMs)
    {
        if (_points.Count >= _options.MaxBufferedPoints)
        {
            _points.RemoveAt(0);
        }

        _points.Add(new GesturePoint(x, y, timestampMs));
    }

    private CircleDirection Evaluate()
    {
        if (_points.Count < 3)
        {
            return CircleDirection.None;
        }

        double totalAngle = 0d;
        double pathLength = 0d;
        double? previousDirectionX = null;
        double? previousDirectionY = null;
        int minX = _points[0].X;
        int minY = _points[0].Y;
        int maxX = _points[0].X;
        int maxY = _points[0].Y;

        for (int index = 1; index < _points.Count; index++)
        {
            GesturePoint previous = _points[index - 1];
            GesturePoint current = _points[index];
            double deltaX = current.X - previous.X;
            double deltaY = current.Y - previous.Y;
            double length = Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));

            if (length < _options.MinStrokeSegmentPx)
            {
                continue;
            }

            if (length > _options.MaxSegmentPx)
            {
                Reset();
                return CircleDirection.None;
            }

            pathLength += length;
            minX = Math.Min(minX, current.X);
            minY = Math.Min(minY, current.Y);
            maxX = Math.Max(maxX, current.X);
            maxY = Math.Max(maxY, current.Y);

            if (previousDirectionX.HasValue && previousDirectionY.HasValue)
            {
                double cross = (previousDirectionX.Value * deltaY) - (previousDirectionY.Value * deltaX);
                double dot = (previousDirectionX.Value * deltaX) + (previousDirectionY.Value * deltaY);
                totalAngle += (Math.Atan2(cross, dot) * 180d) / Math.PI;
            }

            previousDirectionX = deltaX / length;
            previousDirectionY = deltaY / length;
        }

        int spanX = maxX - minX;
        int spanY = maxY - minY;
        int span = Math.Max(spanX, spanY);
        int minorSpan = Math.Min(spanX, spanY);
        double aspectRatio = span <= 0 ? 0d : (double)minorSpan / span;
        double radiusVariation = ComputeRadiusVariation(minX, minY, maxX, maxY);

        if (pathLength < _options.MinPathLengthPx)
        {
            return CircleDirection.None;
        }

        if (Math.Abs(totalAngle) < _options.MinTurnDegrees)
        {
            return CircleDirection.None;
        }

        if (span < _options.MinSpanPx)
        {
            return CircleDirection.None;
        }

        if (span > _options.MaxSpanPx)
        {
            return CircleDirection.None;
        }

        if (aspectRatio < _options.MinAspectRatio)
        {
            return CircleDirection.None;
        }

        if (radiusVariation > _options.MaxRadiusVariation)
        {
            return CircleDirection.None;
        }

        // 放弃闭合判定：人快速画圈常只画到 200°~270° 就松手，转角达标即触发，降低延迟感。
        CircleDirection direction = totalAngle > 0 ? CircleDirection.Clockwise : CircleDirection.CounterClockwise;
        Reset();

        return direction;
    }

    /// <summary>
    /// 计算轨迹点到包围盒中心距离的变异系数；圆形轨迹接近 0，折线或来回摆动明显偏大。
    /// </summary>
    private double ComputeRadiusVariation(int minX, int minY, int maxX, int maxY)
    {
        double centerX = (minX + maxX) / 2d;
        double centerY = (minY + maxY) / 2d;
        double sum = 0d;
        double sumSquares = 0d;
        int count = 0;

        foreach (GesturePoint point in _points)
        {
            double offsetX = point.X - centerX;
            double offsetY = point.Y - centerY;
            double radius = Math.Sqrt((offsetX * offsetX) + (offsetY * offsetY));
            sum += radius;
            sumSquares += radius * radius;
            count++;
        }

        if (count == 0)
        {
            return 1d;
        }

        double mean = sum / count;

        if (mean <= 0d)
        {
            return 1d;
        }

        double variance = (sumSquares / count) - (mean * mean);

        return Math.Sqrt(Math.Max(0d, variance)) / mean;
    }

    #endregion
}
