using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace Pointy
{
    /// <summary>Hand-drawn ink: one polyline per drag, smoothed and faded out after a delay.</summary>
    public sealed class InkLayer
    {
        private sealed class Stroke
        {
            public readonly List<Point> Points = new();
            public Color Color;
            public double Width;
            public DateTime EndedAt = DateTime.MaxValue;
        }

        private const double FadeOutSeconds = 0.6;

        private readonly List<Stroke> _strokes = new();
        private Stroke? _current;

        public bool IsDrawing => _current is not null;
        public int Count => _strokes.Count;

        public bool HasFading => _strokes.Any(s => s.EndedAt != DateTime.MaxValue);

        public void Begin(Point point, double width, Color color)
        {
            _current = new Stroke { Color = color, Width = Math.Max(1, width) };
            _current.Points.Add(point);
            _strokes.Add(_current);
        }

        public void Extend(Point point)
        {
            if (_current is null) return;
            var last = _current.Points[^1];
            if ((point - last).Length < 1.5) return;
            _current.Points.Add(point);
        }

        public void End()
        {
            if (_current is null) return;
            _current.EndedAt = DateTime.UtcNow;
            _current = null;
        }

        public void Clear()
        {
            _current = null;
            _strokes.Clear();
        }

        /// <summary>Drop strokes that have finished fading. Returns true when something was removed.</summary>
        public bool Tick(DateTime now, double fadeSeconds)
        {
            if (fadeSeconds <= 0) return false;
            return _strokes.RemoveAll(s =>
                s.EndedAt != DateTime.MaxValue && now > s.EndedAt.AddSeconds(fadeSeconds + FadeOutSeconds)) > 0;
        }

        public void Draw(DrawingContext dc, DateTime now, double fadeSeconds)
        {
            foreach (var stroke in _strokes)
            {
                if (stroke.Points.Count == 0) continue;

                double opacity = 1;
                if (fadeSeconds > 0 && stroke.EndedAt != DateTime.MaxValue)
                {
                    double age = (now - stroke.EndedAt).TotalSeconds;
                    if (age > fadeSeconds)
                    {
                        opacity = 1 - (age - fadeSeconds) / FadeOutSeconds;
                    }
                }
                if (opacity <= 0.01) continue;

                var brush = new SolidColorBrush(stroke.Color) { Opacity = Math.Clamp(opacity, 0, 1) };
                brush.Freeze();
                var pen = new Pen(brush, stroke.Width)
                {
                    StartLineCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Round,
                    LineJoin = PenLineJoin.Round,
                };
                pen.Freeze();

                dc.DrawGeometry(null, pen, BuildGeometry(stroke.Points));
            }
        }

        private static Geometry BuildGeometry(List<Point> points)
        {
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(points[0], isFilled: false, isClosed: false);
                if (points.Count == 1)
                {
                    ctx.LineTo(points[0], isStroked: true, isSmoothJoin: false);
                }
                else
                {
                    for (int i = 1; i < points.Count; i++)
                    {
                        var prev = points[i - 1];
                        var current = points[i];
                        var mid = new Point((prev.X + current.X) / 2, (prev.Y + current.Y) / 2);
                        ctx.QuadraticBezierTo(prev, mid, isStroked: true, isSmoothJoin: true);
                    }
                    ctx.LineTo(points[^1], isStroked: true, isSmoothJoin: true);
                }
            }
            geometry.Freeze();
            return geometry;
        }
    }
}
