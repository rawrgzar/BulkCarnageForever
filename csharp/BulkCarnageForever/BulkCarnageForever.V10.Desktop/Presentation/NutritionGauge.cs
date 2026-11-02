using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BulkCarnageForever.V10.Desktop.Presentation;

public sealed class NutritionGauge : Control
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(double), typeof(NutritionGauge), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty TargetProperty = DependencyProperty.Register(nameof(Target), typeof(double), typeof(NutritionGauge), new FrameworkPropertyMetadata(1d, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(nameof(Accent), typeof(Brush), typeof(NutritionGauge), new FrameworkPropertyMetadata(Brushes.Crimson, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty TrackProperty = DependencyProperty.Register(nameof(Track), typeof(Brush), typeof(NutritionGauge), new FrameworkPropertyMetadata(Brushes.DimGray, FrameworkPropertyMetadataOptions.AffectsRender));
    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Target { get => (double)GetValue(TargetProperty); set => SetValue(TargetProperty, value); }
    public Brush Accent { get => (Brush)GetValue(AccentProperty); set => SetValue(AccentProperty, value); }
    public Brush Track { get => (Brush)GetValue(TrackProperty); set => SetValue(TrackProperty, value); }
    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        double radius = Math.Max(1, Math.Min(ActualWidth, ActualHeight) / 2 - 14);
        drawing.DrawEllipse(null, new Pen(Track, 12), center, radius, radius);
        double fraction = Target > 0 && double.IsFinite(Value) ? Math.Clamp(Value / Target, 0, 1) : 0;
        if (fraction > 0)
        {
            double angle = Math.Min(fraction, .999999) * Math.PI * 2;
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                context.BeginFigure(new Point(center.X, center.Y - radius), false, false);
                context.ArcTo(new Point(center.X + Math.Sin(angle) * radius, center.Y - Math.Cos(angle) * radius), new Size(radius, radius), 0, fraction > .5, SweepDirection.Clockwise, true, false);
            }
            drawing.DrawGeometry(null, new Pen(Accent, 12) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, geometry);
        }
        void Text(string value, double size, double y, Brush brush)
        {
            var text = new FormattedText(value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, size >= 24 ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal), size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            drawing.DrawText(text, new Point(center.X - text.Width / 2, center.Y + y));
        }
        Text($"{Value:N0}", 33, -38, Foreground);
        Text("kcal logged", 12, 7, Foreground);
        Text(Target > 0 ? $"{Value / Target:P0} of target" : "Set your target", 11, 30, Foreground);
    }
}
