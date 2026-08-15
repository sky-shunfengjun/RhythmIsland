using Avalonia;
using Avalonia.Media;
using RhythmIsland.Models;

namespace RhythmIsland.Controls.Components;

internal sealed class SpectrumCurveGeometryCache
{
    private Point[] _upperPoints = [];
    private Point[] _lowerPoints = [];
    private MutableCurvePath? _upperPath;
    private MutableCurvePath? _lowerPath;
    private MutableCurvePath? _bottomFillPath;
    private MutableCurvePath? _centeredFillPath;

    internal int PointCapacity => _upperPoints.Length;
    internal int GeometryGeneration { get; private set; }
    internal Rect DrawingBounds { get; private set; }
    internal PathGeometry UpperGeometry => _upperPath?.Geometry ?? EmptyGeometry;
    internal PathGeometry LowerGeometry => _lowerPath?.Geometry ?? EmptyGeometry;
    internal PathGeometry BottomFillGeometry => _bottomFillPath?.Geometry ?? EmptyGeometry;
    internal PathGeometry CenteredFillGeometry => _centeredFillPath?.Geometry ?? EmptyGeometry;
    private static PathGeometry EmptyGeometry { get; } = new();

    internal bool Update(
        Size size,
        IReadOnlyList<float> bands,
        SpectrumDisplayMode mode,
        Thickness padding,
        bool includeFill)
    {
        if (bands.Count == 0) return false;
        EnsureCapacity(bands.Count);
        if (!SpectrumCurveLayout.CalculateInto(
                size, bands, mode, padding, _upperPoints, _lowerPoints, out var bounds))
            return false;

        DrawingBounds = bounds;
        UpdateCurve(_upperPath!, _upperPoints, reverse: false);
        if (mode == SpectrumDisplayMode.Centered)
            UpdateCurve(_lowerPath!, _lowerPoints, reverse: false);

        if (includeFill)
        {
            if (mode == SpectrumDisplayMode.Centered)
                UpdateCenteredFill(_centeredFillPath!, _upperPoints, _lowerPoints);
            else
                UpdateBottomFill(_bottomFillPath!, _upperPoints, bounds);
        }
        return true;
    }

    private void EnsureCapacity(int count)
    {
        if (_upperPoints.Length == count) return;
        _upperPoints = new Point[count];
        _lowerPoints = new Point[count];
        _upperPath = CreateOpenPath(count);
        _lowerPath = CreateOpenPath(count);
        _bottomFillPath = CreateBottomFillPath(count);
        _centeredFillPath = CreateCenteredFillPath(count);
        GeometryGeneration++;
    }

    private static MutableCurvePath CreateOpenPath(int count)
    {
        var figure = CreateFigure(isClosed: false, isFilled: false);
        var first = AddCurveSegments(figure.Segments!, count);
        return new MutableCurvePath(CreateGeometry(figure), figure, first.Quadratics, first.Single);
    }

    private static MutableCurvePath CreateBottomFillPath(int count)
    {
        var figure = CreateFigure(isClosed: true, isFilled: true);
        var first = AddCurveSegments(figure.Segments!, count);
        var rightBottom = new LineSegment();
        var leftBottom = new LineSegment();
        figure.Segments!.Add(rightBottom);
        figure.Segments.Add(leftBottom);
        return new MutableCurvePath(
            CreateGeometry(figure), figure, first.Quadratics, first.Single,
            rightBottom, leftBottom);
    }

    private static MutableCurvePath CreateCenteredFillPath(int count)
    {
        var figure = CreateFigure(isClosed: true, isFilled: true);
        var first = AddCurveSegments(figure.Segments!, count);
        var connector = new LineSegment();
        figure.Segments!.Add(connector);
        var second = AddCurveSegments(figure.Segments, count);
        return new MutableCurvePath(
            CreateGeometry(figure), figure, first.Quadratics, first.Single,
            connector, null, second.Quadratics, second.Single);
    }

    private static PathFigure CreateFigure(bool isClosed, bool isFilled) => new()
    {
        IsClosed = isClosed,
        IsFilled = isFilled,
        Segments = new PathSegments()
    };

    private static PathGeometry CreateGeometry(PathFigure figure) => new()
    {
        Figures = new PathFigures { figure }
    };

    private static (QuadraticBezierSegment[] Quadratics, LineSegment? Single) AddCurveSegments(
        PathSegments segments,
        int count)
    {
        if (count == 1)
        {
            var line = new LineSegment();
            segments.Add(line);
            return ([], line);
        }

        var quadratics = new QuadraticBezierSegment[count - 1];
        for (var index = 0; index < quadratics.Length; index++)
        {
            quadratics[index] = new QuadraticBezierSegment();
            segments.Add(quadratics[index]);
        }
        return (quadratics, null);
    }

    private static void UpdateCurve(MutableCurvePath path, IReadOnlyList<Point> points, bool reverse)
    {
        path.Figure.StartPoint = At(points, 0, reverse);
        UpdateCurveSegments(path.FirstQuadratics, path.FirstSingle, points, reverse);
    }

    private static void UpdateBottomFill(MutableCurvePath path, IReadOnlyList<Point> upper, Rect bounds)
    {
        path.Figure.StartPoint = upper[0];
        UpdateCurveSegments(path.FirstQuadratics, path.FirstSingle, upper, reverse: false);
        path.Connector!.Point = new Point(bounds.Right, bounds.Bottom);
        path.SecondConnector!.Point = new Point(bounds.Left, bounds.Bottom);
    }

    private static void UpdateCenteredFill(
        MutableCurvePath path,
        IReadOnlyList<Point> upper,
        IReadOnlyList<Point> lower)
    {
        path.Figure.StartPoint = upper[0];
        UpdateCurveSegments(path.FirstQuadratics, path.FirstSingle, upper, reverse: false);
        path.Connector!.Point = lower[^1];
        UpdateCurveSegments(path.SecondQuadratics, path.SecondSingle, lower, reverse: true);
    }

    private static void UpdateCurveSegments(
        IReadOnlyList<QuadraticBezierSegment> quadratics,
        LineSegment? single,
        IReadOnlyList<Point> points,
        bool reverse)
    {
        if (single is not null)
        {
            single.Point = At(points, 0, reverse);
            return;
        }

        for (var index = 1; index < points.Count - 1; index++)
        {
            var control = At(points, index, reverse);
            var next = At(points, index + 1, reverse);
            quadratics[index - 1].Point1 = control;
            quadratics[index - 1].Point2 = new Point(
                (control.X + next.X) / 2,
                (control.Y + next.Y) / 2);
        }

        var last = At(points, points.Count - 1, reverse);
        quadratics[^1].Point1 = last;
        quadratics[^1].Point2 = last;
    }

    private static Point At(IReadOnlyList<Point> points, int index, bool reverse) =>
        reverse ? points[points.Count - 1 - index] : points[index];

    private sealed record MutableCurvePath(
        PathGeometry Geometry,
        PathFigure Figure,
        QuadraticBezierSegment[] FirstQuadratics,
        LineSegment? FirstSingle,
        LineSegment? Connector = null,
        LineSegment? SecondConnector = null,
        QuadraticBezierSegment[]? SecondQuadraticsValue = null,
        LineSegment? SecondSingle = null)
    {
        internal QuadraticBezierSegment[] SecondQuadratics { get; } = SecondQuadraticsValue ?? [];
    }
}
