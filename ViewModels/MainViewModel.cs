using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GanttSquared.Core.Commands;
using GanttSquared.Core.Model;
using GanttSquared.Core.Persistence;
using GanttSquared.Core.Scheduling;
using Microsoft.Win32;

namespace GanttSquared.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private enum DragMode { None, Move, ResizeLeft, ResizeRight }

    public ProjectModel Project { get; }

    public UndoRedoManager UndoRedo { get; } = new();

    public TaskPropertiesViewModel Properties { get; }

    public ObservableCollection<TaskNodeViewModel> RootNodes { get; } = new();

    /// <summary>Flattened, expand/collapse-aware row order shared by the task list and the Gantt canvas so their rows line up.</summary>
    public ObservableCollection<TaskNodeViewModel> VisibleRows { get; } = new();

    public ObservableCollection<DependencyLineViewModel> DependencyLines { get; } = new();

    /// <summary>Every currently selected row; use this for bulk actions. SelectedNode mirrors it only when exactly one row is selected.</summary>
    public ObservableCollection<TaskNodeViewModel> SelectedNodes { get; } = new();

    public GanttTimelineViewModel Timeline { get; } = new();

    public ObservableCollection<ResourceRowViewModel> Resources { get; } = new();

    [ObservableProperty]
    private bool _isResourcesTabActive;

    public const double RowHeight = 32;

    public double CanvasWidth => Timeline.TotalWidth;

    public double CanvasHeight => Math.Max(1, VisibleRows.Count * RowHeight);

    [ObservableProperty]
    private TaskNodeViewModel? _selectedNode;

    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>Path this project was last saved to or loaded from; null for a never-saved project.</summary>
    [NotifyPropertyChangedFor(nameof(WindowTitleText))]
    [ObservableProperty]
    private string? _currentFilePath;

    /// <summary>
    /// True if there are changes since the last save. Simplified: any undo-stack activity
    /// (including Undo/Redo) marks the project dirty, even if it lands back on exactly the
    /// last-saved state - tracking that precisely would mean comparing against the undo
    /// stack's depth at save time, which UndoRedoManager doesn't currently expose.
    /// </summary>
    [NotifyPropertyChangedFor(nameof(WindowTitleText))]
    [ObservableProperty]
    private bool _isDirty;

    // Live rubber-band preview while dragging from a bar's link handle to another bar.
    [ObservableProperty]
    private bool _isDraggingLink;

    [ObservableProperty]
    private double _dragLinkX1;

    [ObservableProperty]
    private double _dragLinkY1;

    [ObservableProperty]
    private double _dragLinkX2;

    [ObservableProperty]
    private double _dragLinkY2;

    [ObservableProperty]
    private bool _dragLinkIsValid;

    private TaskNodeViewModel? _dragLinkSource;

    private TaskNodeViewModel? _dragNode;
    private DragMode _dragMode;
    private DateOnly _dragOriginalStart;
    private DateOnly _dragOriginalEnd;

    /// <summary>
    /// Project.UseWbsNumbering wrapped as a bindable, live-toggleable setting: ProjectModel
    /// isn't an ObservableObject, so flipping it directly wouldn't notify the UI, and every
    /// node's WbsCode needs recomputing (via RebuildTree) the moment it changes anyway.
    /// </summary>
    public bool UseWbsNumbering
    {
        get => Project.UseWbsNumbering;
        set
        {
            if (Project.UseWbsNumbering == value)
                return;

            Project.UseWbsNumbering = value;
            OnPropertyChanged();
            RebuildTree();
        }
    }

    public string WindowTitleText =>
        $"{Project.Name}{(IsDirty ? " *" : "")}{(CurrentFilePath is null ? " (unsaved)" : "")}";

    public MainViewModel()
    {
        Project = new ProjectModel { Name = "Website Redesign Project", UseWbsNumbering = true };
        Properties = new TaskPropertiesViewModel(Project, UndoRedo);
        Properties.Applied += (_, _) => RebuildTree();

        UndoRedo.StateChanged += (_, _) =>
        {
            RebuildTree();
            IsDirty = true;
            NewTaskCommand.NotifyCanExecuteChanged();
            UndoCommand.NotifyCanExecuteChanged();
            RedoCommand.NotifyCanExecuteChanged();
        };

        Timeline.PropertyChanged += (_, _) => RecomputeLayout();

        SeedSampleData();
        RebuildTree();
        Timeline.FitToTasks(Project.Tasks);
    }

    partial void OnSelectedNodeChanged(TaskNodeViewModel? value) => Properties.LoadFrom(value?.Task);

    /// <summary>Single source of truth for selection: updates SelectedNodes plus the primary SelectedNode (set only when exactly one row is selected) and re-queries bulk-action commands.</summary>
    public void SetSelection(IEnumerable<TaskNodeViewModel> nodes)
    {
        var list = nodes.ToList();

        SelectedNodes.Clear();
        foreach (var node in list)
            SelectedNodes.Add(node);

        SelectedNode = list.Count == 1 ? list[0] : null;

        // SelectedNode = null (for 0 or >1 selected) already cleared Properties via
        // OnSelectedNodeChanged above; for >1 that gets overridden here with the bulk view.
        if (list.Count > 1)
            Properties.LoadForBulk(list.Select(n => n.Task).ToList());

        DeleteSelectedCommand.NotifyCanExecuteChanged();
        IndentSelectedCommand.NotifyCanExecuteChanged();
        OutdentSelectedCommand.NotifyCanExecuteChanged();
    }

    private void RebuildTree()
    {
        var selectedIds = SelectedNodes.Select(n => n.Task.Id).ToHashSet();

        RootNodes.Clear();
        foreach (var root in Project.GetRootTasks())
            RootNodes.Add(BuildNode(root));

        RefreshVisibleRows();
        RebuildResources();

        var restored = selectedIds
            .Select(id => FindNode(RootNodes, id))
            .Where(n => n is not null)
            .Cast<TaskNodeViewModel>()
            .ToList();
        SetSelection(restored);
    }

    /// <summary>Rebuilds the Resources view's rows, including each one's assigned-task-names
    /// summary - piggybacked onto the same rebuild points as the task tree (RebuildTree runs
    /// on every undo-stack change, Open, and New) so resource assignment stays in sync
    /// automatically without a separate tracking path.</summary>
    private void RebuildResources()
    {
        var selectedResourceId = Resources.FirstOrDefault(r => r.IsSelected)?.Resource.Id;

        Resources.Clear();
        foreach (var resource in Project.Resources)
        {
            var assignedNames = Project.Tasks
                .Where(t => t.AssignedResourceIds.Contains(resource.Id))
                .Select(t => t.Name);

            var row = new ResourceRowViewModel(resource)
            {
                AssignedTaskNames = string.Join(", ", assignedNames)
            };
            row.IsSelected = resource.Id == selectedResourceId;
            Resources.Add(row);
        }
    }

    /// <summary>Re-flattens RootNodes into VisibleRows respecting each group's IsExpanded, then recomputes canvas layout.</summary>
    private void RefreshVisibleRows()
    {
        var flat = new List<TaskNodeViewModel>();
        FlattenVisible(RootNodes, 0, flat);

        VisibleRows.Clear();
        foreach (var node in flat)
            VisibleRows.Add(node);

        RecomputeLayout();
    }

    private static void FlattenVisible(IEnumerable<TaskNodeViewModel> nodes, int depth, List<TaskNodeViewModel> into)
    {
        foreach (var node in nodes)
        {
            node.Depth = depth;
            into.Add(node);
            if (node.IsExpanded)
                FlattenVisible(node.Children, depth + 1, into);
        }
    }

    /// <summary>Bottom-up: a leaf's effective span is its own dates; a group's is the min/max of its descendants (rollup), computed over the full tree regardless of collapse state.</summary>
    private static void ComputeEffectiveDates(TaskNodeViewModel node)
    {
        if (node.Children.Count == 0)
        {
            node.EffectiveStartDate = node.Task.StartDate;
            node.EffectiveEndDate = node.Task.EndDate;
            return;
        }

        DateOnly? minStart = null;
        DateOnly? maxEnd = null;
        foreach (var child in node.Children)
        {
            ComputeEffectiveDates(child);
            if (minStart is null || child.EffectiveStartDate < minStart)
                minStart = child.EffectiveStartDate;
            if (maxEnd is null || child.EffectiveEndDate > maxEnd)
                maxEnd = child.EffectiveEndDate;
        }

        node.EffectiveStartDate = minStart ?? node.Task.StartDate;
        node.EffectiveEndDate = maxEnd ?? node.Task.EndDate;
    }

    /// <summary>Positions every visible row's bar in canvas pixel space (using rollup dates for groups) and rebuilds the dependency connector list.</summary>
    private void RecomputeLayout()
    {
        foreach (var root in RootNodes)
            ComputeEffectiveDates(root);

        for (var i = 0; i < VisibleRows.Count; i++)
        {
            var node = VisibleRows[i];
            node.RowTop = i * RowHeight;
            node.IsAlternateRow = i % 2 == 1;
            node.BarX = Timeline.DateToX(node.EffectiveStartDate);
            node.BarWidth = node.Task.IsMilestone
                ? 0
                : Math.Max(node.EffectiveEndDate.DayNumber - node.EffectiveStartDate.DayNumber, 1) * Timeline.DayWidth;
        }

        RebuildDependencyLines();

        OnPropertyChanged(nameof(CanvasWidth));
        OnPropertyChanged(nameof(CanvasHeight));
    }

    private void RebuildDependencyLines()
    {
        var byId = VisibleRows.ToDictionary(n => n.Task.Id);
        DependencyLines.Clear();
        foreach (var dep in Project.Dependencies)
        {
            if (!byId.TryGetValue(dep.PredecessorTaskId, out var pred) || !byId.TryGetValue(dep.SuccessorTaskId, out var succ))
                continue;

            var x1 = pred.Task.IsMilestone ? pred.BarX + 8 : pred.BarX + pred.BarWidth;
            var y1 = pred.RowTop + RowHeight / 2;
            var x2 = succ.Task.IsMilestone ? succ.BarX - 8 : succ.BarX;
            var y2 = succ.RowTop + RowHeight / 2;

            DependencyLines.Add(new DependencyLineViewModel(x1, y1, x2, y2));
        }
    }

    [RelayCommand]
    private void ToggleExpand(TaskNodeViewModel? node)
    {
        if (node is null || !node.IsGroup)
            return;

        node.IsExpanded = !node.IsExpanded;
        RefreshVisibleRows();
    }

    [RelayCommand]
    private void ExpandAll() => SetAllExpanded(true);

    [RelayCommand]
    private void CollapseAll() => SetAllExpanded(false);

    private void SetAllExpanded(bool expanded)
    {
        void Recurse(IEnumerable<TaskNodeViewModel> nodes)
        {
            foreach (var node in nodes)
            {
                if (node.IsGroup)
                    node.IsExpanded = expanded;
                Recurse(node.Children);
            }
        }

        Recurse(RootNodes);
        RefreshVisibleRows();
    }

    [RelayCommand]
    private void SelectRow(TaskNodeViewModel? node) => SetSelection(node is null ? Enumerable.Empty<TaskNodeViewModel>() : new[] { node });

    [RelayCommand]
    private void ShowGanttTab() => IsResourcesTabActive = false;

    [RelayCommand]
    private void ShowResourcesTab() => IsResourcesTabActive = true;

    [RelayCommand]
    private void AddResource() => UndoRedo.Do(new AddResourceCommand(Project, new ProjectResource()));

    [RelayCommand]
    private void DeleteResource(ResourceRowViewModel? row)
    {
        if (row is not null)
            UndoRedo.Do(new RemoveResourceCommand(Project, row.Resource.Id));
    }

    [RelayCommand]
    private void ZoomIn() => Timeline.ZoomIn();

    [RelayCommand]
    private void ZoomOut() => Timeline.ZoomOut();

    [RelayCommand]
    private void ResetZoom() => Timeline.ResetZoom();

    /// <summary>Where a task-list drop landed relative to the row it was dropped on.</summary>
    public enum DropPosition { Before, After, Into }

    /// <summary>
    /// Moves a task (and its subtree) via drag-and-drop in the task list. Into makes it the
    /// last child of target (or moves it to the root level, for a null target); Before/After
    /// make it a sibling of target at that exact position, including reordering among its
    /// current siblings if target is already at the same level.
    /// </summary>
    public void ReparentTask(TaskNodeViewModel source, TaskNodeViewModel? target, DropPosition position = DropPosition.Into)
    {
        if (target is not null && target.Task.Id == source.Task.Id)
            return;

        Guid? newParentId;
        Guid? insertBeforeId;

        if (target is null || position == DropPosition.Into)
        {
            newParentId = target?.Task.Id;
            insertBeforeId = null;
        }
        else
        {
            newParentId = target.Task.ParentId;
            if (position == DropPosition.Before)
            {
                insertBeforeId = target.Task.Id;
            }
            else
            {
                var siblings = Project.GetChildren(newParentId).OrderBy(t => t.OrderIndex).ToList();
                var idx = siblings.FindIndex(t => t.Id == target.Task.Id);
                insertBeforeId = idx >= 0 && idx + 1 < siblings.Count ? siblings[idx + 1].Id : null;
            }
        }

        try
        {
            UndoRedo.Do(new ReparentTaskCommand(Project, source.Task.Id, newParentId, insertBeforeId));
        }
        catch (InvalidOperationException)
        {
            // e.g. dropped onto one of its own descendants; ignore.
        }
    }

    public void CommitInlineRename(TaskNodeViewModel node, string newName)
    {
        node.IsEditingName = false;
        var trimmed = newName.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed == node.Task.Name)
            return;

        UndoRedo.Do(new EditTaskFieldCommand<string>(node.Task, "name", t => t.Name, (t, v) => t.Name = v, trimmed));
    }

    // --- Bar drag: move (both dates shift together) and resize (either edge alone) -------

    /// <summary>Starts a move/resize drag; group bars aren't draggable since their span is a computed rollup, not stored data.</summary>
    public bool BeginBarDrag(TaskNodeViewModel node, bool resizeLeft, bool resizeRight)
    {
        if (node.IsGroup)
            return false;

        _dragNode = node;
        _dragOriginalStart = node.Task.StartDate;
        _dragOriginalEnd = node.Task.EndDate;
        _dragMode = resizeLeft ? DragMode.ResizeLeft : resizeRight ? DragMode.ResizeRight : DragMode.Move;
        return true;
    }

    /// <summary>
    /// Live-previews the drag at the given whole-day offset. When liveCascade is true (Shift
    /// held), dependent successors are shown shifting along with it; otherwise only the
    /// dragged bar itself moves on screen, matching a plain drag's "just this task" semantics.
    /// </summary>
    public void UpdateBarDrag(int dayDelta, bool liveCascade)
    {
        if (_dragNode is null)
            return;

        var (newStart, newEnd) = ComputeDragDates(dayDelta);
        ApplyLiveVisual(_dragNode, newStart, newEnd, liveCascade);
    }

    /// <summary>Commits the drag as an undoable reschedule (cascading to successors only if cascade is true), or discards it if nothing actually moved.</summary>
    public void EndBarDrag(int dayDelta, bool cascade)
    {
        var node = _dragNode;
        if (node is null)
            return;

        // Compute the final dates before clearing drag state - ComputeDragDates switches on
        // _dragMode, so resetting it first (as this used to) always fell through to "no change".
        var (newStart, newEnd) = ComputeDragDates(dayDelta);

        _dragNode = null;
        _dragMode = DragMode.None;

        if (newStart == _dragOriginalStart && newEnd == _dragOriginalEnd)
        {
            RecomputeLayout();
            return;
        }

        UndoRedo.Do(new RescheduleTaskCommand(Project, node.Task.Id, newStart, newEnd, cascade));
    }

    /// <summary>Aborts an in-progress drag (e.g. Escape) and snaps every bar back to its committed position.</summary>
    public void CancelBarDrag()
    {
        _dragNode = null;
        _dragMode = DragMode.None;
        RecomputeLayout();
    }

    private (DateOnly Start, DateOnly End) ComputeDragDates(int dayDelta) => _dragMode switch
    {
        DragMode.Move => (_dragOriginalStart.AddDays(dayDelta), _dragOriginalEnd.AddDays(dayDelta)),
        DragMode.ResizeLeft => (ClampBefore(_dragOriginalStart.AddDays(dayDelta), _dragOriginalEnd), _dragOriginalEnd),
        DragMode.ResizeRight => (_dragOriginalStart, ClampAfter(_dragOriginalEnd.AddDays(dayDelta), _dragOriginalStart)),
        _ => (_dragOriginalStart, _dragOriginalEnd)
    };

    private static DateOnly ClampBefore(DateOnly candidate, DateOnly end) => candidate < end ? candidate : end.AddDays(-1);

    private static DateOnly ClampAfter(DateOnly candidate, DateOnly start) => candidate > start ? candidate : start.AddDays(1);

    private void ApplyLiveVisual(TaskNodeViewModel node, DateOnly newStart, DateOnly newEnd, bool liveCascade)
    {
        RecomputeLayout(); // reset every row to its committed position first, so toggling Shift mid-drag never leaves stale cascade visuals behind

        node.BarX = Timeline.DateToX(newStart);
        node.BarWidth = node.Task.IsMilestone ? 0 : Math.Max(newEnd.DayNumber - newStart.DayNumber, 1) * Timeline.DayWidth;

        if (liveCascade)
        {
            var cascade = SchedulingEngine.ComputeCascade(Project, node.Task.Id, newStart, newEnd);
            var byId = VisibleRows.ToDictionary(n => n.Task.Id);
            foreach (var change in cascade)
            {
                if (change.TaskId == node.Task.Id || !byId.TryGetValue(change.TaskId, out var successor))
                    continue;

                successor.BarX = Timeline.DateToX(change.NewStart);
                successor.BarWidth = successor.Task.IsMilestone
                    ? 0
                    : Math.Max(change.NewEnd.DayNumber - change.NewStart.DayNumber, 1) * Timeline.DayWidth;
            }
        }

        RebuildDependencyLines();
    }

    // --- Dependency-link drag: drag from a bar's edge handle onto another bar -------------

    public void BeginLinkDrag(TaskNodeViewModel source)
    {
        if (source.IsGroup)
            return;

        _dragLinkSource = source;
        var x = source.BarX + source.BarWidth;
        var y = source.RowTop + RowHeight / 2;
        DragLinkX1 = x;
        DragLinkY1 = y;
        DragLinkX2 = x;
        DragLinkY2 = y;
        DragLinkIsValid = false;
        IsDraggingLink = true;
    }

    public void UpdateLinkDrag(double canvasX, double canvasY, TaskNodeViewModel? hoveredTarget)
    {
        if (!IsDraggingLink)
            return;

        DragLinkX2 = canvasX;
        DragLinkY2 = canvasY;
        DragLinkIsValid = hoveredTarget is not null
            && _dragLinkSource is not null
            && hoveredTarget != _dragLinkSource
            && !hoveredTarget.IsGroup
            && !Project.WouldCreateCycle(_dragLinkSource.Task.Id, hoveredTarget.Task.Id);
    }

    public void EndLinkDrag(TaskNodeViewModel? droppedOnTarget)
    {
        IsDraggingLink = false;
        var source = _dragLinkSource;
        _dragLinkSource = null;

        if (source is null || droppedOnTarget is null || droppedOnTarget == source || droppedOnTarget.IsGroup)
            return;

        try
        {
            UndoRedo.Do(new AddDependencyCommand(Project, new DependencyLink(source.Task.Id, droppedOnTarget.Task.Id)));
        }
        catch (InvalidOperationException)
        {
            // Would create a cycle, or the link already exists; drop it silently.
        }
    }

    private TaskNodeViewModel BuildNode(GanttTask task)
    {
        var node = new TaskNodeViewModel(task);
        if (Project.UseWbsNumbering)
            node.WbsCode = Project.GetWbsCode(task.Id);
        foreach (var child in Project.GetChildren(task.Id))
            node.Children.Add(BuildNode(child));
        node.RaiseDisplayChanged();
        return node;
    }

    private static TaskNodeViewModel? FindNode(IEnumerable<TaskNodeViewModel> nodes, Guid id)
    {
        foreach (var node in nodes)
        {
            if (node.Task.Id == id)
                return node;

            var found = FindNode(node.Children, id);
            if (found is not null)
                return found;
        }

        return null;
    }

    [RelayCommand]
    private void NewTask()
    {
        var parentId = SelectedNode?.Task.ParentId;
        var task = new GanttTask("New Task", DateOnly.FromDateTime(DateTime.Today), DateOnly.FromDateTime(DateTime.Today).AddDays(3))
        {
            ParentId = parentId
        };

        UndoRedo.Do(new AddTaskCommand(Project, task));
        var node = FindNode(RootNodes, task.Id);
        SetSelection(node is null ? Enumerable.Empty<TaskNodeViewModel>() : new[] { node });
    }

    private bool HasAnySelection() => SelectedNodes.Count > 0;

    [RelayCommand(CanExecute = nameof(HasAnySelection))]
    private void DeleteSelected()
    {
        var commands = SelectedNodes.Select(n => (IUndoableCommand)new DeleteTaskCommand(Project, n.Task.Id)).ToList();
        if (commands.Count == 0)
            return;

        UndoRedo.Do(new CompositeCommand(commands, "Delete tasks"));
        SetSelection(Array.Empty<TaskNodeViewModel>());
    }

    [RelayCommand(CanExecute = nameof(HasAnySelection))]
    private void IndentSelected()
    {
        // IndentTaskCommand.Execute() throws if a task has no preceding sibling to become its
        // new parent - filter those out up front instead of letting CompositeCommand.Execute()
        // hit the first one and throw uncaught (there's no preceding sibling for a task that's
        // already first in its list, e.g. the very first root task).
        var commands = SelectedNodes
            .Where(CanIndent)
            .Select(n => (IUndoableCommand)new IndentTaskCommand(Project, n.Task.Id))
            .ToList();
        if (commands.Count > 0)
            UndoRedo.Do(new CompositeCommand(commands, "Indent tasks"));
    }

    private bool CanIndent(TaskNodeViewModel node)
    {
        var siblings = Project.GetChildren(node.Task.ParentId).OrderBy(t => t.OrderIndex).ToList();
        var idx = siblings.FindIndex(t => t.Id == node.Task.Id);
        return idx > 0;
    }

    [RelayCommand(CanExecute = nameof(HasAnySelection))]
    private void OutdentSelected()
    {
        var commands = SelectedNodes.Select(n => (IUndoableCommand)new OutdentTaskCommand(Project, n.Task.Id)).ToList();
        if (commands.Count > 0)
            UndoRedo.Do(new CompositeCommand(commands, "Outdent tasks"));
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo() => UndoRedo.Undo();

    private bool CanUndo() => UndoRedo.CanUndo;

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo() => UndoRedo.Redo();

    private bool CanRedo() => UndoRedo.CanRedo;

    [RelayCommand]
    private void NewProject()
    {
        if (!ConfirmProceedPastUnsavedChanges("starting a new project"))
            return;

        Project.ReplaceContents(new ProjectModel { Name = "Untitled Project" });
        UndoRedo.Clear();
        CurrentFilePath = null;
        IsDirty = false;
        SetSelection(Array.Empty<TaskNodeViewModel>());
        OnPropertyChanged(nameof(UseWbsNumbering)); // ReplaceContents bypasses the UseWbsNumbering wrapper's setter
        RebuildTree();
    }

    [RelayCommand]
    private void SaveProject()
    {
        if (CurrentFilePath is null)
        {
            SaveProjectAs();
            return;
        }

        ProjectFileSerializer.Save(Project, CurrentFilePath);
        IsDirty = false;
    }

    [RelayCommand]
    private void SaveProjectAs()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "GanttSquared Project (*.gantt.json)|*.gantt.json|All files (*.*)|*.*",
            DefaultExt = ".gantt.json",
            FileName = Project.Name
        };

        if (dialog.ShowDialog() != true)
            return;

        ProjectFileSerializer.Save(Project, dialog.FileName);
        CurrentFilePath = dialog.FileName;
        IsDirty = false;
    }

    [RelayCommand]
    private void OpenProject()
    {
        if (!ConfirmProceedPastUnsavedChanges("opening another project"))
            return;

        var dialog = new OpenFileDialog
        {
            Filter = "GanttSquared Project (*.gantt.json)|*.gantt.json|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog() != true)
            return;

        var loaded = ProjectFileSerializer.Load(dialog.FileName);
        Project.ReplaceContents(loaded);
        UndoRedo.Clear();
        CurrentFilePath = dialog.FileName;
        IsDirty = false;
        OnPropertyChanged(nameof(UseWbsNumbering)); // ReplaceContents bypasses the UseWbsNumbering wrapper's setter
        SetSelection(Array.Empty<TaskNodeViewModel>());
        RebuildTree();
        Timeline.FitToTasks(Project.Tasks);
    }

    [RelayCommand]
    private void ImportGanttProject()
    {
        if (!ConfirmProceedPastUnsavedChanges("importing a GanttProject file"))
            return;

        var dialog = new OpenFileDialog
        {
            Filter = "GanttProject files (*.gan;*.xml)|*.gan;*.xml|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog() != true)
            return;

        ProjectModel imported;
        try
        {
            imported = GanttProjectImporter.Import(dialog.FileName);
        }
        catch (Exception ex) when (ex is InvalidDataException or System.Xml.XmlException or IOException)
        {
            MessageBox.Show(
                $"Could not import '{System.IO.Path.GetFileName(dialog.FileName)}':\n\n{ex.Message}",
                "Import Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        Project.ReplaceContents(imported);
        UndoRedo.Clear();
        CurrentFilePath = null; // .gan isn't this app's native format - Save will prompt for a new .gantt.json location
        IsDirty = true; // freshly imported, not yet saved in our own format
        OnPropertyChanged(nameof(UseWbsNumbering));
        SetSelection(Array.Empty<TaskNodeViewModel>());
        RebuildTree();
        Timeline.FitToTasks(Project.Tasks);
    }

    /// <summary>
    /// If there are unsaved changes, asks the user whether to save, discard, or cancel.
    /// Returns true if the caller is clear to proceed (nothing to save, changes were saved,
    /// or the user chose to discard); false if the caller should abandon what it was doing.
    /// Used both when opening a different project and when the window is closing.
    /// </summary>
    public bool ConfirmProceedPastUnsavedChanges(string actionDescription)
    {
        if (!IsDirty)
            return true;

        var result = MessageBox.Show(
            $"'{Project.Name}' has unsaved changes. Save before {actionDescription}?",
            "Unsaved Changes",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning);

        switch (result)
        {
            case MessageBoxResult.Yes:
                SaveProject();
                return !IsDirty; // stays true if the user cancelled the Save As dialog
            case MessageBoxResult.No:
                return true;
            default:
                return false;
        }
    }

    [RelayCommand]
    private void Search()
    {
        if (string.IsNullOrWhiteSpace(SearchText))
            return;

        var match = Project.Tasks.FirstOrDefault(t => t.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
        if (match is null)
            return;

        ExpandAncestors(match);
        RebuildTree();
        var node = FindNode(RootNodes, match.Id);
        SetSelection(node is null ? Enumerable.Empty<TaskNodeViewModel>() : new[] { node });
    }

    private void ExpandAncestors(GanttTask task)
    {
        var current = task.ParentId;
        while (current is { } id)
        {
            var parent = Project.FindTask(id);
            if (parent is null)
                break;
            parent.IsExpanded = true;
            current = parent.ParentId;
        }
    }

    private void SeedSampleData()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);

        var redesign = AddSampleTask("Website Redesign", today, today, parentId: null);
        var research = AddSampleTask("Research & Planning", today, today.AddDays(4), redesign.Id, PriorityLevel.Medium);
        var design = AddSampleTask("Design Mockups", today.AddDays(5), today.AddDays(9), redesign.Id, PriorityLevel.High);
        var development = AddSampleTask("Development", today.AddDays(10), today.AddDays(19), redesign.Id, PriorityLevel.High);
        var beta = AddSampleTask("Beta Release", today.AddDays(20), today.AddDays(20), redesign.Id, PriorityLevel.Critical, milestone: true);

        var testing = AddSampleTask("Testing", today, today, parentId: null);
        var qa = AddSampleTask("QA Testing", today.AddDays(20), today.AddDays(24), testing.Id, PriorityLevel.Medium);
        var bugFixes = AddSampleTask("Bug Fixes", today.AddDays(25), today.AddDays(28), testing.Id, PriorityLevel.Medium);
        var launch = AddSampleTask("Launch", today.AddDays(29), today.AddDays(29), testing.Id, PriorityLevel.Critical, milestone: true);

        Project.AddDependency(new DependencyLink(research.Id, design.Id));
        Project.AddDependency(new DependencyLink(design.Id, development.Id));
        Project.AddDependency(new DependencyLink(development.Id, beta.Id));
        Project.AddDependency(new DependencyLink(beta.Id, qa.Id));
        Project.AddDependency(new DependencyLink(qa.Id, bugFixes.Id));
        Project.AddDependency(new DependencyLink(bugFixes.Id, launch.Id));

        var alice = new ProjectResource { Name = "Alice", Email = "alice@example.com", Color = "#3478F6" };
        var bob = new ProjectResource { Name = "Bob", Email = "bob@example.com", Color = "#F4B740" };
        Project.AddResource(alice);
        Project.AddResource(bob);
        research.AssignedResourceIds.Add(alice.Id);
        design.AssignedResourceIds.Add(alice.Id);
        development.AssignedResourceIds.Add(bob.Id);
    }

    private GanttTask AddSampleTask(string name, DateOnly start, DateOnly end, Guid? parentId, PriorityLevel priority = PriorityLevel.Medium, bool milestone = false)
    {
        var task = new GanttTask(name, start, end) { ParentId = parentId, Priority = priority };
        if (milestone)
            task.SetMilestone(true);
        Project.AddTask(task);
        return task;
    }
}
