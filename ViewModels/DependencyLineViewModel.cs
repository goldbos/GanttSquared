namespace GanttSquared.ViewModels;

/// <summary>An elbow-routed connector from a predecessor bar's edge to a successor bar's edge.</summary>
public sealed record DependencyLineViewModel(double X1, double Y1, double X2, double Y2)
{
    private const double Elbow = 12;

    public string PathData => $"M {X1},{Y1} L {X1 + Elbow},{Y1} L {X1 + Elbow},{Y2} L {X2 - 4},{Y2}";

    public string ArrowPoints => $"{X2 - 8},{Y2 - 4} {X2},{Y2} {X2 - 8},{Y2 + 4}";
}
