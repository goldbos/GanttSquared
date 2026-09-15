# GanttSquared Architecture

GanttSquared is a WPF (.NET, `net10.0-windows`) desktop Gantt chart editor. This doc is a map
for anyone new to the codebase: how the projects fit together, where the important logic lives,
and the handful of patterns that show up repeatedly.

## Solution layout

```
GanttSquared.slnx
├─ GanttSquared.Core/          Domain model + business logic. No WPF references - plain C#.
├─ GanttSquared.Core.Tests/    xUnit tests for GanttSquared.Core.
└─ GanttSquared/               The WPF app: ViewModels, XAML views, converters.
```

`GanttSquared.Core` is deliberately framework-agnostic. It knows nothing about WPF, XAML, or
`ObservableObject` - it's plain C# operating on plain C# types, which is what keeps it unit-testable
without spinning up a UI. The WPF project references it and wraps its types in `ObservableObject`
view models for data binding.

## GanttSquared.Core

### Model (`Core/Model/`)

- **`ProjectModel`** - the root aggregate. Owns the flat list of tasks, dependency links, and
  resources, plus lookup/query helpers (`FindTask`, `GetChildren`, `GetDescendants`,
  `WouldCreateCycle`, ...). Tasks form a tree via `GanttTask.ParentId`, not by nesting objects -
  `ProjectModel` reconstructs parent/child relationships on demand from that flat list.
- **`GanttTask`** - a single task, milestone, or group ("section" in the UI - just a task with
  children and no independent dates of its own). Its date-related invariants (`EndDate >=
  StartDate`, milestone ⇒ `EndDate == StartDate`) are enforced by routing every date change
  through `SetDates`/`MoveTo`/`ResizeTo`/`SetDurationDays`/`SetMilestone` rather than exposing
  public setters directly.
- **`DependencyLink`** - a directed predecessor → successor constraint. `DependencyType` (FS/SS/
  FF/SF) and `LagDays` are modeled and persisted, though the UI currently only ever creates
  finish-to-start links with no lag.
- **`ProjectResource`** - a person/role tasks can be assigned to, via `GanttTask.AssignedResourceIds`.
- **`PriorityLevel`** - drives a task's default bar color when it has no explicit `Color`.

### Commands (`Core/Commands/`) - the undo/redo system

Every edit to a `ProjectModel` goes through an `IUndoableCommand`, never a direct mutation from
the UI layer. This is the single most load-bearing pattern in the codebase:

```csharp
public interface IUndoableCommand
{
    string Description { get; }
    void Execute();
    void Undo();
}
```

`UndoRedoManager` holds two stacks (undo/redo) and is the only thing that calls `Execute`/`Undo`
directly:

- `Do(command)` executes it, pushes it onto the undo stack, and clears the redo stack.
- `Undo()` / `Redo()` pop-and-invoke, moving the command to the other stack.
- `StateChanged` fires after every one of those - `MainViewModel` subscribes to this as its one
  hook to rebuild the visible tree, mark the project dirty, and write a crash-recovery snapshot.

Each concrete command is a small, self-contained class: it snapshots whatever it's about to
overwrite in `Execute()` so `Undo()` can restore it exactly. A few worth knowing:

- **`EditTaskFieldCommand<T>`** - generic single-field edit (name, priority, color, description,
  notes, progress, ...), parameterized by a getter/setter pair. Most simple field edits from the
  Properties panel go through this rather than a dedicated command class per field.
- **`RescheduleTaskCommand`** - moving/resizing a task's dates. By default cascades the change to
  dependent successors via `SchedulingEngine` (see below); the whole cascade undoes as one step.
- **`ReparentTaskCommand` / `IndentTaskCommand` / `OutdentTaskCommand`** - restructuring the task
  tree (drag-and-drop, Tab/Shift+Tab). Each snapshots every sibling whose `OrderIndex`/`ParentId`
  might shift, not just the moved task, so the whole reorder undoes together.
- **`CompositeCommand`** - bundles several commands into one undo step, e.g. deleting/indenting
  multiple selected tasks at once.

Two categories of edit deliberately bypass this system entirely: resource *assignment* checkboxes
(`GanttTask.AssignedResourceIds`) and resource *field* edits (name/email/color in the Resources
tab) apply immediately with no undo support - a conscious simplification, since resources are a
much lighter-weight concept than tasks.

### Scheduling (`Core/Scheduling/`)

**`SchedulingEngine.ComputeCascade`** implements forward-scheduling: when a task's dates change,
every transitively-dependent successor is recomputed to its earliest possible start given *all*
of its incoming dependencies (standard CPM early-start scheduling, the same model tools like MS
Project use). It's symmetric - a successor gets pulled earlier right along with a predecessor
that moves earlier, not just pushed later, unless a different predecessor still holds it back.
This function is pure: it returns a list of proposed `TaskDateChange`s without mutating anything;
`RescheduleTaskCommand` is what actually applies them (and captures the "before" state for undo).

### Persistence (`Core/Persistence/`)

- **`ProjectFileSerializer`** - the app's native format: a `ProjectModel` ↔ `ProjectFileDto` tree
  ↔ indented JSON. The DTOs (`TaskDto`, `DependencyDto`, `ResourceDto`) exist as a stable on-disk
  contract decoupled from the domain types' invariant-enforcing methods - domain objects are
  never serialized directly.
- **`GanttProjectImporter`** - one-way import of GanttProject's `.gan` XML format. Deliberately
  scoped to a minimal, correct core (tasks, hierarchy, dates, milestones, priority, progress,
  dependencies, resources, assignments) rather than the full format - calendars/holidays,
  vacations, baselines, and custom properties are ignored. See its doc comments for the specific
  value-mapping decisions (GanttProject's 5-level priority collapsed to this app's 4, working-day
  durations converted to calendar-day end dates, etc.).

## GanttSquared (the WPF app)

### MVVM shape

```
MainWindow.xaml / MainWindow.xaml.cs   (View)
        │  DataContext
        ▼
MainViewModel                          (ViewModel)
        │  wraps
        ▼
ProjectModel + UndoRedoManager         (GanttSquared.Core)
```

View models use `CommunityToolkit.Mvvm`'s source generators - `[ObservableProperty]` for bindable
fields, `[RelayCommand]` for commands, `[NotifyPropertyChangedFor]`/`[NotifyCanExecuteChangedFor]`
to wire up derived-property and CanExecute invalidation. A private field like `_isDarkTheme`
becomes a public `IsDarkTheme` property with change notification, without hand-written
boilerplate.

### The view models (`ViewModels/`)

- **`MainViewModel`** - the hub. Owns the `ProjectModel`, the `UndoRedoManager`, the flattened
  `VisibleRows` (task tree flattened respecting expand/collapse, kept in lock-step between the
  task list and the Gantt canvas so their rows line up), drag state (bar move/resize, dependency-
  link dragging), and most of the app's commands (new/open/save/import, indent/outdent/delete,
  zoom, undo/redo, search, crash recovery). `RecomputeLayout()` is its central "everything
  downstream of the data changed" method: it recomputes each row's rollup dates, pixel position,
  overdue flag, and resource-conflict flag, then rebuilds the dependency connector lines.
- **`TaskNodeViewModel`** - wraps a `GanttTask` for display. Holds UI-only derived state that
  doesn't belong on the domain model: pixel position/size on the canvas (`BarX`/`BarWidth`/
  `RowTop`), rollup dates for group rows (`EffectiveStartDate`/`EffectiveEndDate`), and flags
  like `IsOverdue`/`IsResourceConflict`/`IsHovered` that `MainViewModel` computes and this class
  just carries for binding.
- **`GanttTimelineViewModel`** - maps dates to canvas pixel positions and drives the timeline
  header. Pixels-per-day is always `viewport width ÷ VisibleDayCount`, so `ZoomIn`/`ZoomOut`
  change how many days are visible rather than the canvas's raw pixel width - the chart always
  exactly fills the available window width at any zoom level.
- **`TaskPropertiesViewModel`** - the right-hand Properties panel's editable draft of the
  selected task (or a bulk-edit draft for multiple selected tasks). Nothing touches the project
  until `Save()`, at which point each *changed* field becomes its own `IUndoableCommand`.
- **`ResourceRowViewModel` / `ResourceOptionViewModel`** - the Resources tab's rows, and the
  per-resource checkbox rows in the Properties panel's assignment list, respectively.
- **`DependencyLineViewModel`** - a single elbow-routed connector between two bars' edges,
  recomputed by `MainViewModel.RebuildDependencyLines()` whenever layout changes.

### The view (`MainWindow.xaml` / `MainWindow.xaml.cs`)

One window, three main regions: a left icon rail (new/open/save/export/project-properties/
add-task/indent/outdent/delete/undo/redo), a task list + Gantt canvas (or the Resources tab, in
its place), and the right-hand Properties panel. `MainWindow.xaml.cs` is where WPF-specific
concerns that don't belong in a view model live: mouse-driven bar drag/resize, dependency-link
dragging, hover synchronization between the list and canvas, keyboard navigation in the task
list, the PNG/PDF chart export, and theme switching.

A few patterns worth knowing if you're touching this file:

- **Canvas-based bar layout.** Bars, gridlines, row stripes, and dependency lines are separate
  `ItemsControl`s layered via `Canvas.Left`/`Canvas.Top`, all reading pixel positions computed by
  `MainViewModel`/`GanttTimelineViewModel`. WPF centers a `Stretch`-aligned element with an
  explicit size inside a larger container by default, so every layered `ItemsControl` needs
  explicit `HorizontalAlignment="Left" VerticalAlignment="Top"` to anchor top-left instead.
- **Theme switching without `DynamicResource` everywhere.** Rather than converting every brush
  reference to `DynamicResource` (touching dozens of bindings), most theme brushes are
  `SolidColorBrush` resources with `po:Freeze="False"`, and `MainWindow.ApplyTheme(bool)` mutates
  each brush's own `Color` property in place. Since `SolidColorBrush.Color` is itself a dependency
  property, every consumer repaints immediately - as long as it's a live brush *instance* shared
  by reference (a `StaticResource` consumer that captured a snapshot at load time won't update;
  `DynamicResource` consumers always resolve the current instance, so it works either way here
  since nothing swaps *which* brush a key points to). A handful of `SystemColors` keys
  (`ControlBrushKey`, `HighlightBrushKey`, ...) are overridden the same way, since some default
  WPF control templates (`TreeViewItem`'s selection state, `Calendar`'s day-grid) consult those
  instead of this app's own theme resources.
- **Crash recovery + autosave.** `MainViewModel` writes a full snapshot to
  `%LocalAppData%\GanttSquared\recovery.gantt.json` on every `UndoRedoManager.StateChanged` (i.e.
  every committed edit), independent of the user's own save location. `MainWindow_Loaded` checks
  for a leftover snapshot at startup - its mere existence means the last session didn't exit
  cleanly - and offers to restore it. A separate `DispatcherTimer` in `MainWindow.xaml.cs`
  additionally autosaves to the user's own file every couple of minutes if one is set.
- **`--test` launches `TestEditWindow` instead of `MainWindow`** (see `App.xaml.cs`) - a minimal
  scratch window for isolating a control in a small test harness, not part of the normal app.

### Converters (`Converters/`)

Small `IValueConverter`/`IMultiValueConverter` implementations for XAML bindings that need a
transform a plain `{Binding}` can't express: bool ↔ Visibility (regular and inverted), a hex
string ↔ `Brush`, tree depth ↔ left-margin indent, and a couple of other layout helpers.

## Testing

`GanttSquared.Core.Tests` covers the Core project only - the WPF layer has no automated tests
(view models are tested indirectly by exercising `ProjectModel`/`UndoRedoManager` directly, which
covers the actual logic without needing a UI). Tests are organized by the class under test
(`ProjectModelTests`, `GanttTaskTests`, `SetMilestoneCommandTests`, `ProjectFileSerializerTests`,
`GanttProjectImporterTests`, ...) using xUnit.

## Naming conventions

Standard C# conventions throughout, not Hungarian notation: `PascalCase` for types and public
members, `_camelCase` for private fields (matching `CommunityToolkit.Mvvm`'s `[ObservableProperty]`
generator, which turns `_fooBar` into a public `FooBar` property), `camelCase` for parameters and
locals. No type-prefixed names (`strName`, `bIsActive`, etc.) - Microsoft's own C# guidelines
discourage Hungarian notation, and it isn't used anywhere in this codebase.
