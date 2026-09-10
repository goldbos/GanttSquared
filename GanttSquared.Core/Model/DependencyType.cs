namespace GanttSquared.Core.Model;

/// <summary>
/// Standard project-scheduling dependency types, as used by GanttProject/MS Project.
/// </summary>
public enum DependencyType
{
    /// <summary>Successor cannot start before predecessor finishes.</summary>
    FinishToStart,

    /// <summary>Successor cannot start before predecessor starts.</summary>
    StartToStart,

    /// <summary>Successor cannot finish before predecessor finishes.</summary>
    FinishToFinish,

    /// <summary>Successor cannot finish before predecessor starts.</summary>
    StartToFinish
}
