using System.Windows;
using System.Windows.Media;

namespace GameBooster.Controls;

public partial class SparklineControl : System.Windows.Controls.UserControl
{
    public static readonly DependencyProperty SamplesProperty =
        DependencyProperty.Register(nameof(Samples), typeof(IReadOnlyList<double>), typeof(SparklineControl), new PropertyMetadata(null, OnChanged));

    public static readonly DependencyProperty StrokeProperty =
        DependencyProperty.Register(nameof(Stroke), typeof(Brush), typeof(SparklineControl), new PropertyMetadata(null, OnChanged));

    public SparklineControl()
    {
        InitializeComponent();
        SizeChanged += (_, _) => Redraw();
    }

    public IReadOnlyList<double>? Samples
    {
        get => (IReadOnlyList<double>?)GetValue(SamplesProperty);
        set => SetValue(SamplesProperty, value);
    }

    public Brush Stroke
    {
        get => (Brush)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((SparklineControl)d).Redraw();

    private void Redraw()
    {
        if (Line == null || Fill == null)
            return;
        var samples = Samples;
        if (Stroke != null)
        {
            Line.Stroke = Stroke;
            Fill.Fill = Stroke;
        }
        if (samples == null || samples.Count == 0 || ActualWidth < 2 || ActualHeight < 2)
        {
            Line.Data = Geometry.Empty;
            Fill.Data = Geometry.Empty;
            return;
        }
        var width = ActualWidth;
        var height = ActualHeight;
        var step = samples.Count == 1 ? 0 : width / (samples.Count - 1);
        var line = new StreamGeometry();
        var area = new StreamGeometry();
        using (var lc = line.Open())
        using (var ac = area.Open())
        {
            for (var i = 0; i < samples.Count; i++)
            {
                var x = i * step;
                var y = height - (Math.Clamp(samples[i], 0, 100) / 100d) * (height - 4) - 2;
                var point = new Point(x, y);
                if (i == 0)
                {
                    lc.BeginFigure(point, false, false);
                    ac.BeginFigure(new Point(x, height), true, true);
                    ac.LineTo(point, true, true);
                }
                else
                {
                    lc.LineTo(point, true, true);
                    ac.LineTo(point, true, true);
                }
            }
            ac.LineTo(new Point(Math.Max(0, (samples.Count - 1) * step), height), true, true);
        }
        line.Freeze();
        area.Freeze();
        Line.Data = line;
        Fill.Data = area;
    }
}
