using Avalonia;
using Avalonia.Media;

namespace RhythmIsland.Controls.Components;

internal readonly record struct SpectrumQuadraticSegment(Point Control, Point End);

internal static class SpectrumCurveGeometryBuilder
{
    internal static IReadOnlyList<SpectrumQuadraticSegment> CalculateSmoothSegments(
        IReadOnlyList<Point> points,
        bool reverse = false)
    {
        if (points.Count <= 1) return [];

        Point At(int index) => reverse ? points[points.Count - 1 - index] : points[index];
        var segments = new SpectrumQuadraticSegment[points.Count - 1];
        for (var index = 1; index < points.Count - 1; index++)
        {
            var control = At(index);
            var next = At(index + 1);
            var midpoint = new Point((control.X + next.X) / 2, (control.Y + next.Y) / 2);
            segments[index - 1] = new SpectrumQuadraticSegment(control, midpoint);
        }

        var last = At(points.Count - 1);
        segments[^1] = new SpectrumQuadraticSegment(last, last);
        return segments;
    }

    internal static IReadOnlyList<Point> CalculateBottomFillBoundary(
        IReadOnlyList<Point> points,
        Rect bounds)
    {
        if (points.Count == 0) return [];
        var boundary = new Point[points.Count + 2];
        for (var index = 0; index < points.Count; index++) boundary[index] = points[index];
        boundary[^2] = new Point(bounds.Right, bounds.Bottom);
        boundary[^1] = new Point(bounds.Left, bounds.Bottom);
        return boundary;
    }

    internal static IReadOnlyList<Point> CalculateCenteredFillBoundary(
        IReadOnlyList<Point> upper,
        IReadOnlyList<Point> lower)
    {
        if (upper.Count == 0 || upper.Count != lower.Count) return [];
        var boundary = new Point[upper.Count + lower.Count];
        for (var index = 0; index < upper.Count; index++) boundary[index] = upper[index];
        for (var index = 0; index < lower.Count; index++) boundary[upper.Count + index] = lower[lower.Count - 1 - index];
        return boundary;
    }

    internal static StreamGeometry CreateOpenCurve(IReadOnlyList<Point> points)
    {
        var geometry = new StreamGeometry();
        if (points.Count == 0) return geometry;

        using var context = geometry.Open();
        context.BeginFigure(points[0], false);
        AppendSmoothSegments(context, points, false);
        context.EndFigure(false);
        return geometry;
    }

    internal static StreamGeometry CreateBottomFill(IReadOnlyList<Point> points, Rect bounds)
    {
        var geometry = new StreamGeometry();
        if (points.Count == 0) return geometry;

        using var context = geometry.Open();
        context.BeginFigure(points[0], true);
        AppendSmoothSegments(context, points, false);
        context.LineTo(new Point(bounds.Right, bounds.Bottom));
        context.LineTo(new Point(bounds.Left, bounds.Bottom));
        context.EndFigure(true);
        return geometry;
    }

    internal static StreamGeometry CreateCenteredFill(
        IReadOnlyList<Point> upper,
        IReadOnlyList<Point> lower)
    {
        var geometry = new StreamGeometry();
        if (upper.Count == 0 || upper.Count != lower.Count) return geometry;

        using var context = geometry.Open();
        context.BeginFigure(upper[0], true);
        AppendSmoothSegments(context, upper, false);
        context.LineTo(lower[^1]);
        AppendSmoothSegments(context, lower, true);
        context.EndFigure(true);
        return geometry;
    }

    private static void AppendSmoothSegments(
        StreamGeometryContext context,
        IReadOnlyList<Point> points,
        bool reverse)
    {
        if (points.Count == 1)
        {
            context.LineTo(points[0]);
            return;
        }

        foreach (var segment in CalculateSmoothSegments(points, reverse))
            context.QuadraticBezierTo(segment.Control, segment.End);
    }
}
