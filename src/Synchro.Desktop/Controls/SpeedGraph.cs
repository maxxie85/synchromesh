using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using Synchro.ViewModels;

namespace Synchro.Desktop.Controls;

/// <summary>
/// Download and upload speed over time as two filled lines. It scrolls smoothly: the right edge trails the clock by
/// about one poll interval, so a new sample slides in from the right instead of the line jumping a step every poll,
/// and the scale eases to a new top instead of snapping. Colours come from brushes, so XAML can bind them to the
/// theme's resources.
/// </summary>
public sealed class SpeedGraph : Control
{
    public static readonly StyledProperty<IReadOnlyList<SpeedSample>?> SamplesProperty =
        AvaloniaProperty.Register<SpeedGraph, IReadOnlyList<SpeedSample>?>(nameof(Samples));

    public static readonly StyledProperty<long> ScaleProperty =
        AvaloniaProperty.Register<SpeedGraph, long>(nameof(Scale), SpeedHistoryViewModel.MinimumScale);

    public static readonly StyledProperty<TimeSpan> WindowProperty =
        AvaloniaProperty.Register<SpeedGraph, TimeSpan>(nameof(Window), TimeSpan.FromMinutes(3));

    public static readonly StyledProperty<IBrush?> DownloadBrushProperty =
        AvaloniaProperty.Register<SpeedGraph, IBrush?>(nameof(DownloadBrush));

    public static readonly StyledProperty<IBrush?> UploadBrushProperty =
        AvaloniaProperty.Register<SpeedGraph, IBrush?>(nameof(UploadBrush));

    public static readonly StyledProperty<IBrush?> GridBrushProperty =
        AvaloniaProperty.Register<SpeedGraph, IBrush?>(nameof(GridBrush));

    // 30 frames a second is plenty: the line moves about a pixel per second.
    private static readonly TimeSpan FrameInterval = TimeSpan.FromSeconds(1.0 / 30);
    private static readonly TimeSpan MinimumLag = TimeSpan.FromSeconds(0.5);
    private static readonly TimeSpan MaximumLag = TimeSpan.FromSeconds(10);
    private const double EaseSeconds = 0.35;

    private readonly DispatcherTimer _timer;
    private long _lastFrame;
    private double _shownScale = SpeedHistoryViewModel.MinimumScale;
    private double _lagSeconds = -1;

    static SpeedGraph()
    {
        AffectsRender<SpeedGraph>(SamplesProperty, DownloadBrushProperty, UploadBrushProperty, GridBrushProperty);
        ClipToBoundsProperty.OverrideDefaultValue<SpeedGraph>(true);
    }

    public SpeedGraph()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = FrameInterval };
        _timer.Tick += (_, _) => OnFrame();
    }

    public IReadOnlyList<SpeedSample>? Samples
    {
        get => GetValue(SamplesProperty);
        set => SetValue(SamplesProperty, value);
    }

    /// <summary>The top of the graph in bytes per second.</summary>
    public long Scale
    {
        get => GetValue(ScaleProperty);
        set => SetValue(ScaleProperty, value);
    }

    /// <summary>How much time the width shows.</summary>
    public TimeSpan Window
    {
        get => GetValue(WindowProperty);
        set => SetValue(WindowProperty, value);
    }

    public IBrush? DownloadBrush
    {
        get => GetValue(DownloadBrushProperty);
        set => SetValue(DownloadBrushProperty, value);
    }

    public IBrush? UploadBrush
    {
        get => GetValue(UploadBrushProperty);
        set => SetValue(UploadBrushProperty, value);
    }

    public IBrush? GridBrush
    {
        get => GetValue(GridBrushProperty);
        set => SetValue(GridBrushProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        UpdateTimer();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _timer.Stop();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SamplesProperty)
        {
            if (Samples is not { Count: > 0 }) _lagSeconds = -1; // a fresh line starts without easing
            UpdateTimer();
        }
        else if (change.Property == ScaleProperty && !_timer.IsEnabled)
        {
            _shownScale = Scale; // nothing moving yet: a new line starts at its own scale instead of easing in from the minimum
            InvalidateVisual();
        }
    }

    // Animate only while there is a line to move.
    private void UpdateTimer()
    {
        var animate = VisualRoot is not null && Samples is { Count: >= 2 };
        if (animate && !_timer.IsEnabled)
        {
            _lastFrame = Stopwatch.GetTimestamp();
            _timer.Start();
        }
        else if (!animate)
        {
            _timer.Stop();
        }
    }

    private void OnFrame()
    {
        var now = Stopwatch.GetTimestamp();
        var elapsed = Stopwatch.GetElapsedTime(_lastFrame, now).TotalSeconds;
        _lastFrame = now;
        var ease = 1 - Math.Exp(-elapsed / EaseSeconds);

        _shownScale += (Math.Max(Scale, 1) - _shownScale) * ease;
        if (TargetLag() is { } lag)
            _lagSeconds = _lagSeconds < 0 ? lag : _lagSeconds + (lag - _lagSeconds) * ease;

        if (IsEffectivelyVisible) InvalidateVisual();
    }

    /// <summary>The time between the last two samples: the right edge trails by that much, so it rarely runs out of line.</summary>
    private double? TargetLag()
    {
        if (Samples is not { Count: >= 2 } samples) return null;
        var interval = samples[^1].Time - samples[^2].Time;
        return Math.Clamp(interval.TotalSeconds, MinimumLag.TotalSeconds, MaximumLag.TotalSeconds);
    }

    public override void Render(DrawingContext context)
    {
        var width = Bounds.Width;
        var height = Bounds.Height;
        if (width <= 0 || height <= 0) return;

        DrawGrid(context, width, height);

        if (Samples is not { Count: >= 2 } samples || Window <= TimeSpan.Zero) return;

        var right = DateTimeOffset.UtcNow - TimeSpan.FromSeconds(Math.Max(_lagSeconds, 0));
        var left = right - Window;
        var scale = Math.Max(_shownScale, 1);
        double X(DateTimeOffset time) => (time - left) / Window * width;
        // One pixel of headroom keeps a full-scale line from being clipped in half.
        double Y(long value) => height - 1 - Math.Min(value / scale, 1.05) * (height - 2);

        // Download (usually the larger area) first, upload on top.
        DrawSeries(context, samples, s => s.Download, DownloadBrush, X, Y, height);
        DrawSeries(context, samples, s => s.Upload, UploadBrush, X, Y, height);
    }

    private void DrawGrid(DrawingContext context, double width, double height)
    {
        if (GridBrush is not { } brush) return;

        var solid = new ImmutablePen(brush.ToImmutable(), 1);
        var dashed = new ImmutablePen(brush.ToImmutable(), 1, new ImmutableDashStyle([2, 3], 0));
        context.DrawLine(solid, new Point(0, height - 0.5), new Point(width, height - 0.5));
        context.DrawLine(dashed, new Point(0, 0.5), new Point(width, 0.5));
        context.DrawLine(dashed, new Point(0, Math.Round(height / 2) + 0.5), new Point(width, Math.Round(height / 2) + 0.5));
    }

    private static void DrawSeries(DrawingContext context, IReadOnlyList<SpeedSample> samples, Func<SpeedSample, long> value,
        IBrush? brush, Func<DateTimeOffset, double> x, Func<long, double> y, double height)
    {
        if (brush is null) return;

        var line = new StreamGeometry();
        using (var g = line.Open())
        {
            g.BeginFigure(new Point(x(samples[0].Time), y(value(samples[0]))), false);
            for (var i = 1; i < samples.Count; i++)
                g.LineTo(new Point(x(samples[i].Time), y(value(samples[i]))));
            g.EndFigure(false);
        }

        var area = new StreamGeometry();
        using (var g = area.Open())
        {
            g.BeginFigure(new Point(x(samples[0].Time), height), true);
            foreach (var sample in samples)
                g.LineTo(new Point(x(sample.Time), y(value(sample))));
            g.LineTo(new Point(x(samples[^1].Time), height));
            g.EndFigure(true);
        }

        context.DrawGeometry(AreaBrush(brush), null, area);
        context.DrawGeometry(null, new ImmutablePen(brush.ToImmutable(), 1.5, lineJoin: PenLineJoin.Round), line);
    }

    // The line's colour fading out towards the bottom; other brushes just get a faint fill.
    private static IBrush AreaBrush(IBrush brush)
    {
        if (brush is not ISolidColorBrush { Color: var color }) return new ImmutableSolidColorBrush(Colors.Transparent);

        return new ImmutableLinearGradientBrush(
            [new ImmutableGradientStop(0, Color.FromArgb(80, color.R, color.G, color.B)),
             new ImmutableGradientStop(1, Color.FromArgb(8, color.R, color.G, color.B))],
            startPoint: new RelativePoint(0, 0, RelativeUnit.Relative),
            endPoint: new RelativePoint(0, 1, RelativeUnit.Relative));
    }
}
