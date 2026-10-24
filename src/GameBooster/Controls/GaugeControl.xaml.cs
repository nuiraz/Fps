using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

namespace GameBooster.Controls;

public partial class GaugeControl : System.Windows.Controls.UserControl
{
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(double), typeof(GaugeControl), new PropertyMetadata(0d, OnVisualChanged));

    public static readonly DependencyProperty MaximumProperty =
        DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(GaugeControl), new PropertyMetadata(100d, OnVisualChanged));

    public static readonly DependencyProperty CaptionProperty =
        DependencyProperty.Register(nameof(Caption), typeof(string), typeof(GaugeControl), new PropertyMetadata("", OnVisualChanged));

    public static readonly DependencyProperty ValueLabelProperty =
        DependencyProperty.Register(nameof(ValueLabel), typeof(string), typeof(GaugeControl), new PropertyMetadata("—", OnVisualChanged));

    public static readonly DependencyProperty AccentProperty =
        DependencyProperty.Register(nameof(Accent), typeof(Brush), typeof(GaugeControl), new PropertyMetadata(null, OnVisualChanged));

    public GaugeControl()
    {
        InitializeComponent();
        Loaded += (_, _) => Redraw();
    }

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public string Caption
    {
        get => (string)GetValue(CaptionProperty);
        set => SetValue(CaptionProperty, value);
    }

    public string ValueLabel
    {
        get => (string)GetValue(ValueLabelProperty);
        set => SetValue(ValueLabelProperty, value);
    }

    public Brush Accent
    {
        get => (Brush)GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }

    private static void OnVisualChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((GaugeControl)d).Redraw();

    private void Redraw()
    {
        if (Track == null || ValueArc == null)
            return;
        const double size = 148;
        const double thickness = 8;
        var radius = (size - thickness) / 2;
        var center = new Point(size / 2, size / 2);
        Track.Data = Arc(center, radius, 135, 405);
        var span = Maximum <= 0 ? 0 : Math.Clamp(Value / Maximum, 0, 1);
        ValueArc.Data = Arc(center, radius, 135, 135 + Math.Max(0.4, span * 270));
        ValueArc.Stroke = Accent ?? (Brush)FindResource("Brush.Lime");
        ValueText.Text = string.IsNullOrWhiteSpace(ValueLabel) ? "—" : ValueLabel;
        CaptionText.Text = Caption;
    }

    private static Geometry Arc(Point center, double radius, double startDeg, double endDeg)
    {
        Point At(double deg)
        {
            var rad = deg * Math.PI / 180d;
            return new Point(center.X + radius * Math.Cos(rad), center.Y + radius * Math.Sin(rad));
        }
        var figure = new PathFigure { StartPoint = At(startDeg), IsClosed = false };
        figure.Segments.Add(new ArcSegment(At(endDeg), new Size(radius, radius), 0, endDeg - startDeg > 180, SweepDirection.Clockwise, true));
        return new PathGeometry([figure]);
    }
}
