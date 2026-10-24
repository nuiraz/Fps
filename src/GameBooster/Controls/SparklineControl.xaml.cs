using System.Windows;
using System.Windows.Media;

namespace GameBooster.Controls;

public partial class SparklineControl : System.Windows.Controls.UserControl
{
    public static readonly DependencyProperty SamplesProperty =
        DependencyProperty.Register(nameof(Samples), typeof(IReadOnlyList<double>), typeof(SparklineControl), new PropertyMetadata(null, (_, e) => { }));

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

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == SamplesProperty)
            Redraw();
    }

    private void Redraw()
    {
        if (Line == null)
            return;
        var samples = Samples;
        var points = new PointCollection();
        if (samples == null || samples.Count == 0 || ActualWidth < 2 || ActualHeight < 2)
        {
            Line.Points = points;
            return;
        }
        var max = Math.Max(100, samples.Max());
        var step = samples.Count == 1 ? 0 : ActualWidth / (samples.Count - 1);
        for (var i = 0; i < samples.Count; i++)
        {
            var y = ActualHeight - (Math.Clamp(samples[i], 0, max) / max) * (ActualHeight - 4) - 2;
            points.Add(new Point(i * step, y));
        }
        Line.Points = points;
    }
}
