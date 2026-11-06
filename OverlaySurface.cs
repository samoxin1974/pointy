using System;
using System.Windows;
using System.Windows.Media;

namespace Pointy
{
    /// <summary>Mutable per-frame state shared between the session and the renderer.</summary>
    public sealed class PointerState
    {
        public Point Cursor;
        public bool Held;
        public double Press;
        public bool HideHand;
        public PointerStyle Style = Pointers.Hands[0];
        public double GripX;
    }

    /// <summary>
    /// Renders the pointer. All geometry lives in a local frame whose origin is the grip
    /// (below the bottom edge of the screen) and whose -Y axis runs up the rod. The whole
    /// frame is rotated so the finger tip lands on the cursor.
    /// </summary>
    public sealed class OverlaySurface : FrameworkElement
    {
        private const double GripLeanFrac = 0.30;
        private const double GripDepthFrac = 0.12;
        private const double MinRodFrac = 0.05;
        private const double LiftFrac = 0.06;

        private readonly InkLayer _ink = new();
        private HandAsset? _asset;
        private string _materialKey = "";

        private Brush? _rodBrush;
        private Brush? _handBrush;
        private Brush? _rodShadow;
        private Brush? _handShadow;

        private static readonly Brush LaserGlow = CreateGlow();
        private static readonly Brush LaserCore = CreateCore();
        private static readonly Brush LaserDot = CreateDot();

        public PointerState State { get; } = new();
        public InkLayer Ink => _ink;

        private static Brush CreateGlow()
        {
            var brush = new RadialGradientBrush();
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(140, 255, 45, 35), 0.0));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(60, 255, 45, 35), 0.45));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 45, 35), 1.0));
            brush.Freeze();
            return brush;
        }

        private static Brush CreateCore()
        {
            var brush = new SolidColorBrush(Color.FromArgb(240, 255, 219, 209));
            brush.Freeze();
            return brush;
        }

        private static Brush CreateDot()
        {
            var brush = new SolidColorBrush(Pointers.Red);
            brush.Freeze();
            return brush;
        }

        /// <summary>Call when the selected pointer changes so brushes are rebuilt.</summary>
        public void InvalidateMaterial()
        {
            _materialKey = "";
            _rodBrush = _handBrush = _rodShadow = _handShadow = null;
            _asset = null;
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            double w = double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width;
            double h = double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height;
            return new Size(w, h);
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth;
            double h = ActualHeight;
            if (w < 2 || h < 2) return;

            // Keep the surface fully transparent everywhere we draw nothing.
            var snapshot = DateTime.UtcNow;
            _ink.Draw(dc, snapshot, Prefs.Current.FadeSeconds);

            if (State.HideHand) return;

            if (State.Style.IsLaser || !EnsureMaterial())
            {
                DrawLaser(dc, h);
                return;
            }

            DrawArm(dc, w, h);
        }

        private bool EnsureMaterial()
        {
            var style = State.Style;
            if (_materialKey == style.Id && _handBrush is not null) return true;

            _materialKey = style.Id;
            _asset = style.AssetName is null ? null : HandAsset.Load(style.AssetName);
            if (_asset is null)
            {
                _rodBrush = _handBrush = _rodShadow = _handShadow = null;
                return false;
            }

            _rodBrush = SliceBrush(_asset.Image, _asset.Wrist, _asset.RodEnd, 1.0);
            _handBrush = SliceBrush(_asset.Image, _asset.HandTop, _asset.Wrist, 1.0);
            _rodShadow = SliceBrush(_asset.Shadow, _asset.Wrist, _asset.RodEnd, 0.30);
            _handShadow = SliceBrush(_asset.Shadow, _asset.HandTop, _asset.Wrist, 0.30);
            return true;
        }

        private static Brush SliceBrush(ImageSource image, double top, double bottom, double opacity)
        {
            var brush = new ImageBrush(image)
            {
                Viewbox = new Rect(0, top, 1, Math.Max(0.0001, bottom - top)),
                ViewboxUnits = BrushMappingMode.RelativeToBoundingBox,
                Stretch = Stretch.Fill,
                AlignmentX = AlignmentX.Left,
                AlignmentY = AlignmentY.Top,
                Opacity = opacity,
            };
            brush.Freeze();
            return brush;
        }

        private void DrawArm(DrawingContext dc, double w, double h)
        {
            var a = _asset;
            if (a is null || _handBrush is null || _rodBrush is null) return;

            double press = State.Press;
            double handLenFrac = Math.Clamp(Prefs.Current.PointerSize, 0.05, 0.30);

            double gripX = State.GripX;
            double gripY = h * (1 + GripDepthFrac);

            double dx = State.Cursor.X - gripX;
            double dy = State.Cursor.Y - gripY;
            double reach = Math.Sqrt(dx * dx + dy * dy);
            if (reach < 1) return;

            double handPts = handLenFrac * h * (1 - 0.06 * press);
            double span = Math.Max(0.05, a.Wrist - a.HandTop);
            double scale = handPts / span;
            double widthPts = Math.Max(1, scale * a.Aspect);

            double handTipPts = (a.Wrist - a.Tip) * scale;
            double tipOffX = (a.TipX - a.RodX) * widthPts;

            double along = Math.Sqrt(Math.Max(0, reach * reach - tipOffX * tipOffX));
            double rodLen = Math.Max(MinRodFrac * h, along - handTipPts);

            // Angle of the tip inside the local frame; rotating by the aim angle minus this
            // puts the finger tip exactly on the cursor.
            double tipAngle = Math.Atan2(rodLen + handTipPts, tipOffX);
            double phi = Math.Atan2(dy, dx);
            double degrees = (phi + tipAngle) * 180.0 / Math.PI;

            var matrix = Matrix.Identity;
            matrix.Rotate(degrees);
            matrix.Translate(gripX, gripY);

            double x0 = -a.RodX * widthPts;
            var rodRect = new Rect(x0, -rodLen, widthPts, rodLen);
            var handRect = new Rect(x0, -(rodLen + handPts), widthPts, handPts);

            dc.PushTransform(new MatrixTransform(matrix));

            if (Prefs.Current.ShowShadow && _rodShadow is not null && _handShadow is not null)
            {
                double lift = (LiftFrac * h * (1 - 0.8 * press)) * 0.10 + 3;
                dc.PushTransform(new TranslateTransform(0, lift));
                dc.DrawRectangle(_rodShadow, null, rodRect);
                dc.DrawRectangle(_handShadow, null, handRect);
                dc.Pop();
            }

            dc.DrawRectangle(_rodBrush, null, rodRect);
            dc.DrawRectangle(_handBrush, null, handRect);

            dc.Pop();
        }

        private void DrawLaser(DrawingContext dc, double h)
        {
            double r = Math.Max(4, h * 0.0038 * Math.Clamp(Prefs.Current.PointerSize, 0.05, 0.30) / 0.13);
            r *= 1 + 0.2 * State.Press;

            dc.DrawEllipse(LaserGlow, null, State.Cursor, r * 2.4, r * 2.4);
            dc.DrawEllipse(LaserDot, null, State.Cursor, r, r);
            dc.DrawEllipse(LaserCore, null, State.Cursor, r * 0.45, r * 0.45);
        }
    }
}
