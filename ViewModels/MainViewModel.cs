using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GanttSquared.Core.Commands;
using GanttSquared.Core.Model;
using GanttSquared.Core.Persistence;
using Microsoft.Win32;

namespace GanttSquared.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    public ProjectModel Project { get; }

    public UndoRedoManager UndoRedo { get; } = new();

    public TaskPropertiesViewModel Properties { get; }

    public ObservableCollection<TaskNodeViewModel> RootNodes { get; } = new();

    // Same CanExecute-requery gap as TaskPropertiesViewModel.HasSelection: these commands'
    // CanExecute reads SelectedNode via CanEditSelection(), but nothing calls
    // NotifyCanExecuteChanged() when SelectedNode changes unless we say so here - otherwise
    // Delete/Indent/Outdent stay disabled until some unrelated UndoRedo action happens to
    // refresh them as a side effect.
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(IndentSelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(OutdentSelectedCommand))]
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

    public string WindowTitleText =>
        $"{Project.Name}{(IsDirty ? " *" : "")}{(CurrentFilePath is null ? " (unsaved)" : "")}";

    public MainViewModel()
    {
        Project = new ProjectModel { Name = "Website Redesign Project" };
        Properties = new TaskPropertiesViewModel(Project, UndoRedo);
        Properties.Applied += (_, _) => RebuildTree();

        UndoRedo.StateChanged += (_, _) =>
        {
            RebuildTree();
            IsDirty = true;
            NewTaskCommand.NotifyCanExecuteChanged();
            DeleteSelectedCommand.NotifyCanExecuteChanged();
            IndentSelectedCommand.NotifyCanExecuteChanged();
            OutdentSelectedCommand.NotifyCanExecuteChanged();
            UndoCommand.NotifyCanExecuteChanged();
            RedoCommand.NotifyCanExecuteChanged();
        };

        SeedSampleData();
        RebuildTree();
    }

    partial void OnSelectedNodeChanged(TaskNodeViewModel? value) => Properties.LoadFrom(value?.Task);

    private void RebuildTree()
    {
        var selectedId = SelectedNode?.Task.Id;

        RootNodes.Clear();
        foreach (var root in Project.GetRootTasks())
            RootNodes.Add(BuildNode(root));

        SelectedNode = selectedId is { } id ? FindNode(RootNodes, id) : null;
    }

    private TaskNodeViewModel BuildNode(GanttTask task)
    {
        var node = new TaskNodeViewModel(task);
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
        if (node is not null)
            SelectedNode = node;
    }

    private bool CanEditSelection() => SelectedNode is not null;

    [RelayCommand(CanExecute = nameof(CanEditSelection))]
    private void DeleteSelected()
    {
        if (SelectedNode is null)
            return;

        UndoRedo.Do(new DeleteTaskCommand(Project, SelectedNode.Task.Id));
        SelectedNode = null;
    }

    [RelayCommand(CanExecute = nameof(CanEditSelection))]
    private void IndentSelected()
    {
        if (SelectedNode is null)
            return;

        try
        {
            UndoRedo.Do(new IndentTaskCommand(Project, SelectedNode.Task.Id));
        }
        catch (InvalidOperationException)
        {
            // No preceding sibling to indent under; ignore.
        }
    }

    [RelayCommand(CanExecute = nameof(CanEditSelection))]
    private void OutdentSelected()
    {
        if (SelectedNode is null)
            return;

        try
        {
            UndoRedo.Do(new OutdentTaskCommand(Project, SelectedNode.Task.Id));
        }
        catch (InvalidOperationException)
        {
            // Already top-level; ignore.
        }
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo() => UndoRedo.Undo();

    private bool CanUndo() => UndoRedo.CanUndo;

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo() => UndoRedo.Redo();

    private bool CanRedo() => UndoRedo.CanRedo;

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
        SelectedNode = null;
        RebuildTree();
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
        SelectedNode = FindNode(RootNodes, match.Id);
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
