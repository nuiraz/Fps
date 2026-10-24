using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace GameBooster.Controls;

public partial class CircularGauge : System.Windows.Controls.UserControl
{
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(double), typeof(CircularGauge), new PropertyMetadata(0d, OnValueChanged));

    public static readonly DependencyProperty MaximumProperty =
        DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(CircularGauge), new PropertyMetadata(100d, OnVisualChanged));

    public static readonly DependencyProperty CaptionProperty =
        DependencyProperty.Register(nameof(Caption), typeof(string), typeof(CircularGauge), new PropertyMetadata("", OnVisualChanged));

    public static readonly DependencyProperty ReadingProperty =
        DependencyProperty.Register(nameof(Reading), typeof(string), typeof(CircularGauge), new PropertyMetadata("", OnVisualChanged));

    public static readonly DependencyProperty DetailProperty =
        DependencyProperty.Register(nameof(Detail), typeof(string), typeof(CircularGauge), new PropertyMetadata("", OnVisualChanged));

    public static readonly DependencyProperty AccentProperty =
        DependencyProperty.Register(nameof(Accent), typeof(Brush), typeof(CircularGauge), new PropertyMetadata(null, OnVisualChanged));

    public static readonly DependencyProperty ThicknessProperty =
        DependencyProperty.Register(nameof(Thickness), typeof(double), typeof(CircularGauge), new PropertyMetadata(12d, OnVisualChanged));

    public static readonly DependencyProperty GlowProperty =
        DependencyProperty.Register(nameof(Glow), typeof(bool), typeof(CircularGauge), new PropertyMetadata(true, OnVisualChanged));

    public static readonly DependencyProperty ShowCenterProperty =
        DependencyProperty.Register(nameof(ShowCenter), typeof(bool), typeof(CircularGauge), new PropertyMetadata(true, OnVisualChanged));

    public static readonly DependencyProperty IsIndeterminateProperty =
        DependencyProperty.Register(nameof(IsIndeterminate), typeof(bool), typeof(CircularGauge), new PropertyMetadata(false, OnVisualChanged));

    private static readonly DependencyProperty ShownProperty =
        DependencyProperty.Register(nameof(Shown), typeof(double), typeof(CircularGauge), new PropertyMetadata(0d, OnVisualChanged));

    private readonly RotateTransform _spin = new(-90);
    private double _lastDash = double.NaN;
    private bool _spinning;

    public CircularGauge()
    {
        InitializeComponent();
        ValueArc.RenderTransform = _spin;
        GlowArc.RenderTransform = _spin;
        ValueArc.RenderTransformOrigin = new Point(0.5, 0.5);
        GlowArc.RenderTransformOrigin = new Point(0.5, 0.5);
        SizeChanged += (_, _) => Redraw(true);
        Loaded += (_, _) =>
        {
            AnimateTo(Value);
            UpdateSpin();
        };
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

    public string Reading
    {
        get => (string)GetValue(ReadingProperty);
        set => SetValue(ReadingProperty, value);
    }

    public string Detail
    {
        get => (string)GetValue(DetailProperty);
        set => SetValue(DetailProperty, value);
    }

    public Brush Accent
    {
        get => (Brush)GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }

    public double Thickness
    {
        get => (double)GetValue(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }

    public bool Glow
    {
        get => (bool)GetValue(GlowProperty);
        set => SetValue(GlowProperty, value);
    }

    public bool ShowCenter
    {
        get => (bool)GetValue(ShowCenterProperty);
        set => SetValue(ShowCenterProperty, value);
    }

    public bool IsIndeterminate
    {
        get => (bool)GetValue(IsIndeterminateProperty);
        set => SetValue(IsIndeterminateProperty, value);
    }

    private double Shown
    {
        get => (double)GetValue(ShownProperty);
        set => SetValue(ShownProperty, value);
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((CircularGauge)d).AnimateTo((double)e.NewValue);

    private static void OnVisualChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var gauge = (CircularGauge)d;
        if (e.Property == IsIndeterminateProperty)
            gauge.UpdateSpin();
        gauge.Redraw(e.Property != ShownProperty);
    }

    private void AnimateTo(double target)
    {
        if (!IsLoaded || double.IsNaN(target))
        {
            BeginAnimation(ShownProperty, null);
            Shown = double.IsNaN(target) ? 0 : target;
            return;
        }
        if (target < Shown - 50)
        {
            BeginAnimation(ShownProperty, null);
            Shown = target;
            return;
        }
        var animation = new DoubleAnimation(Shown, target, TimeSpan.FromMilliseconds(450))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.HoldEnd
        };
        BeginAnimation(ShownProperty, animation);
    }

    private void UpdateSpin()
    {
        if (IsIndeterminate)
        {
            if (_spinning)
                return;
            _spinning = true;
            _spin.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(-90, 270, TimeSpan.FromSeconds(1.15))
            {
                RepeatBehavior = RepeatBehavior.Forever
            });
            return;
        }
        _spinning = false;
        _spin.BeginAnimation(RotateTransform.AngleProperty, null);
        _spin.Angle = -90;
    }

    private void Redraw(bool force)
    {
        if (Track == null || ValueArc == null || GlowArc == null)
            return;
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size < 8)
            return;
        var thickness = Math.Clamp(Thickness, 2, size / 4);
        var inset = thickness / 2 + (Glow ? thickness * 0.7 : 0);
        var radius = size / 2 - inset;
        if (radius <= 1)
            return;
        var diameter = radius * 2 + thickness;
        Track.Width = Track.Height = diameter;
        ValueArc.Width = ValueArc.Height = diameter;
        GlowArc.Width = GlowArc.Height = diameter;
        Track.StrokeThickness = thickness;
        ValueArc.StrokeThickness = thickness;
        GlowArc.StrokeThickness = thickness * 2.1;
        var accent = Accent ?? TryBrush("Brush.Lime");
        ValueArc.Stroke = accent;
        GlowArc.Stroke = accent;
        GlowArc.Visibility = Glow ? Visibility.Visible : Visibility.Collapsed;
        Center.Visibility = ShowCenter ? Visibility.Visible : Visibility.Collapsed;
        ReadingText.Text = Reading ?? "";
        ReadingText.FontSize = Math.Clamp(size * 0.2, 16, 44);
        CaptionText.Text = Caption ?? "";
        DetailText.Text = Detail ?? "";
        DetailText.Visibility = string.IsNullOrWhiteSpace(Detail) ? Visibility.Collapsed : Visibility.Visible;

        var units = (2 * Math.PI * radius) / thickness;
        var fraction = IsIndeterminate
            ? 0.28
            : Maximum <= 0 ? 0 : Math.Clamp(Shown / Maximum, 0, 1);
        var dash = fraction <= 0.001 ? 0 : fraction >= 0.999 ? units : Math.Max(0.08, fraction * units);
        var gap = Math.Max(0.01, units - dash);
        if (!force && !double.IsNaN(_lastDash) && Math.Abs(dash - _lastDash) < 0.08)
            return;
        _lastDash = dash;
        var pattern = new DoubleCollection { dash, gap };
        ValueArc.StrokeDashArray = pattern;
        GlowArc.StrokeDashArray = new DoubleCollection { dash, gap };
    }

    private Brush TryBrush(string key)
    {
        try
        {
            if (TryFindResource(key) is Brush brush)
                return brush;
        }
        catch
        {
            // thème pas encore chargé
        }
        return Brushes.White;
    }
}
