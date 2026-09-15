using CommunityToolkit.Mvvm.ComponentModel;
using GanttSquared.Core.Model;

namespace GanttSquared.ViewModels;

public sealed record TimelineTick(double X, string Label, DateOnly Date);

/// <summary>Auto switches between daily/weekly ticks based on zoom (the previous, only behavior); Day/Week force one regardless of zoom.</summary>
public enum TimelineTickMode { Auto, Day, Week }

/// <summary>
/// Maps project dates to horizontal pixel positions on the Gantt canvas and drives the
/// timeline header. Pixels-per-day is always derived from the live viewport width divided by
/// how many days should be visible at once (VisibleDayCount), so the canvas always exactly
/// fills the available width and resizing the window/column reflows the same day span rather
/// than changing it. ZoomIn/ZoomOut/ResetZoom adjust VisibleDayCount, not the pixel width directly.
/// </summary>
public sealed partial class GanttTimelineViewModel : ObservableObject
{
    private const double MinVisibleDays = 4;
    private const double MaxVisibleDays = 240;
    private const double ZoomStepFactor = 1.25;

    /// <summary>Below this many pixels/day, day-level tick labels would overlap, so ticks switch to weekly.</summary>
    private const double WeekTickThreshold = 10;

    private const int PaddingDays = 7;
    private const double DefaultVisibleDays = 30;

    [NotifyPropertyChangedFor(nameof(DayWidth))]
    [NotifyPropertyChangedFor(nameof(TotalWidth))]
    [NotifyPropertyChangedFor(nameof(TodayX))]
    [NotifyPropertyChangedFor(nameof(Ticks))]
    [ObservableProperty]
    private double _viewportWidth;

    [NotifyPropertyChangedFor(nameof(DayWidth))]
    [NotifyPropertyChangedFor(nameof(TotalWidth))]
    [NotifyPropertyChangedFor(nameof(TodayX))]
    [NotifyPropertyChangedFor(nameof(Ticks))]
    [ObservableProperty]
    private double _visibleDayCount = DefaultVisibleDays;

    [NotifyPropertyChangedFor(nameof(TotalWidth))]
    [NotifyPropertyChangedFor(nameof(TodayX))]
    [NotifyPropertyChangedFor(nameof(Ticks))]
    [ObservableProperty]
    private DateOnly _rangeStart = DateOnly.FromDateTime(DateTime.Today).AddDays(-PaddingDays);

    [NotifyPropertyChangedFor(nameof(TotalWidth))]
    [NotifyPropertyChangedFor(nameof(Ticks))]
    [ObservableProperty]
    private DateOnly _rangeEnd = DateOnly.FromDateTime(DateTime.Today).AddDays(PaddingDays);

    partial void OnRangeStartChanged(DateOnly value) => OnPropertyChanged(nameof(RangeStartDate));

    partial void OnRangeEndChanged(DateOnly value) => OnPropertyChanged(nameof(RangeEndDate));

    /// <summary>
    /// DateTime-typed wrappers around RangeStart/RangeEnd for the toolbar's DatePickers (which
    /// bind DateTime?, not DateOnly). A pick that would invert the range (end before/at start,
    /// or vice versa) is rejected rather than applied; the explicit OnPropertyChanged() call in
    /// each setter runs regardless of whether the underlying DateOnly actually changed, so a
    /// rejected pick snaps the picker's displayed value back to the still-current one instead of
    /// silently leaving an unapplied date showing.
    /// </summary>
    public DateTime RangeStartDate
    {
        get => RangeStart.ToDateTime(TimeOnly.MinValue);
        set
        {
            var newDate = DateOnly.FromDateTime(value);
            if (newDate < RangeEnd)
                RangeStart = newDate;
            OnPropertyChanged();
        }
    }

    public DateTime RangeEndDate
    {
        get => RangeEnd.ToDateTime(TimeOnly.MinValue);
        set
        {
            var newDate = DateOnly.FromDateTime(value);
            if (newDate > RangeStart)
                RangeEnd = newDate;
            OnPropertyChanged();
        }
    }

    /// <summary>Pixels per day, derived so exactly VisibleDayCount days fill the current viewport width.</summary>
    public double DayWidth => ViewportWidth > 0 ? ViewportWidth / VisibleDayCount : 20;

    /// <summary>Never less than the viewport width, so the canvas background/gridlines always reach the right edge even for a short project.</summary>
    public double TotalWidth => Math.Max(ViewportWidth, (RangeEnd.DayNumber - RangeStart.DayNumber) * DayWidth);

    public double TodayX => DateToX(DateOnly.FromDateTime(DateTime.Today));

    public double DateToX(DateOnly date) => (date.DayNumber - RangeStart.DayNumber) * DayWidth;

    [NotifyPropertyChangedFor(nameof(Ticks))]
    [ObservableProperty]
    private TimelineTickMode _tickMode = TimelineTickMode.Auto;

    public IReadOnlyList<TimelineTickMode> TickModes { get; } = Enum.GetValues<TimelineTickMode>();

    public IReadOnlyList<TimelineTick> Ticks => BuildTicks();

    /// <summary>Widens the visible date range (with padding) so every task fits, if any exist.</summary>
    public void FitToTasks(IEnumerable<GanttTask> tasks)
    {
        var list = tasks.ToList();
        if (list.Count == 0)
            return;

        RangeStart = list.Min(t => t.StartDate).AddDays(-PaddingDays);
        RangeEnd = list.Max(t => t.EndDate).AddDays(PaddingDays);
    }

    public void ZoomIn() => VisibleDayCount = Math.Max(MinVisibleDays, VisibleDayCount / ZoomStepFactor);

    public void ZoomOut() => VisibleDayCount = Math.Min(MaxVisibleDays, VisibleDayCount * ZoomStepFactor);

    /// <summary>Resets to a condensed default: roughly a month as a rolling span of days, not a calendar month.</summary>
    public void ResetZoom() => VisibleDayCount = DefaultVisibleDays;

    private IReadOnlyList<TimelineTick> BuildTicks()
    {
        var ticks = new List<TimelineTick>();

        var useWeekly = TickMode switch
        {
            TimelineTickMode.Day => false,
            TimelineTickMode.Week => true,
            _ => DayWidth < WeekTickThreshold
        };

        if (useWeekly)
        {
            var cursor = StartOfWeek(RangeStart);
            while (cursor < RangeEnd)
            {
                ticks.Add(new TimelineTick(DateToX(cursor), $"{cursor:MMM d}", cursor));
                cursor = cursor.AddDays(7);
            }
        }
        else
        {
            var cursor = RangeStart;
            while (cursor < RangeEnd)
            {
                var label = cursor.Day == 1 || cursor == RangeStart ? $"{cursor:MMM d}" : $"{cursor.Day}";
                ticks.Add(new TimelineTick(DateToX(cursor), label, cursor));
                cursor = cursor.AddDays(1);
            }
        }

        return ticks;
    }

    private static DateOnly StartOfWeek(DateOnly date) => date.AddDays(-(int)date.DayOfWeek);
}
