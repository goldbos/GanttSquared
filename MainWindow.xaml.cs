using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using GanttSquared.ViewModels;
using Microsoft.Win32;
using WinFormsColorDialog = System.Windows.Forms.ColorDialog;
using DrawingColor = System.Drawing.Color;

namespace GanttSquared
{
    public partial class MainWindow : Window
    {
        private MainViewModel ViewModel => (MainViewModel)DataContext;

        private bool _syncingScroll;
        private bool _syncingSelection;

        // Bar move/resize drag.
        private Point? _barDragStartPoint;
        private FrameworkElement? _barDragElement;
        private bool _barDragResizeLeft;
        private bool _barDragResizeRight;
        private bool _barDragActive;

        // Canvas background pan.
        private Point? _panStartPoint;
        private double _panStartH;
        private double _panStartV;
        private bool _isPanning;

        // Task-list drag-to-reparent.
        private Point? _reparentDragStartPoint;
        private TaskNodeViewModel? _reparentDragSource;

        public MainWindow()
        {
            InitializeComponent();
            DataContext = new MainViewModel();
            Closing += MainWindow_Closing;
            Loaded += MainWindow_Loaded;

            CanvasScroll.ScrollChanged += CanvasScroll_ScrollChanged;
            CanvasScroll.SizeChanged += (_, _) => UpdateTimelineViewportWidth();
            CanvasScroll.PreviewMouseWheel += CanvasScroll_PreviewMouseWheel;

            // Panning handlers go on the content Grid, not the ScrollViewer itself: a plain
            // (non-Preview) MouseLeftButtonDown on the ScrollViewer control arrives already
            // Handled by its own internal chrome, even for clicks on empty background, so the
            // Grid actually receiving the click is the only reliable place to hook this.
            CanvasBodyGrid.MouseLeftButtonDown += CanvasScroll_MouseLeftButtonDown;
            CanvasBodyGrid.MouseMove += CanvasScroll_MouseMove;
            CanvasBodyGrid.MouseLeftButtonUp += CanvasScroll_MouseLeftButtonUp;

            TaskListScroll.ScrollChanged += TaskListScroll_ScrollChanged;

            // Resources tab's allocation timeline mirrors the Gantt tab's list/header/canvas
            // scroll-sync wiring above, just targeting its own set of controls - see the comments
            // on the Gantt versions for why each hook exists (they apply here unchanged).
            ResourceCanvasScroll.ScrollChanged += ResourceCanvasScroll_ScrollChanged;
            ResourceCanvasScroll.SizeChanged += (_, _) => UpdateTimelineViewportWidth();
            ResourceCanvasScroll.PreviewMouseWheel += CanvasScroll_PreviewMouseWheel;
            ResourceCanvasBodyGrid.MouseLeftButtonDown += ResourceCanvasScroll_MouseLeftButtonDown;
            ResourceCanvasBodyGrid.MouseMove += ResourceCanvasScroll_MouseMove;
            ResourceCanvasBodyGrid.MouseLeftButtonUp += ResourceCanvasScroll_MouseLeftButtonUp;
            ResourceListScroll.ScrollChanged += ResourceListScroll_ScrollChanged;
            ResourceListScroll.PreviewMouseWheel += CanvasScroll_PreviewMouseWheel;

            TaskList.SelectionChanged += TaskList_SelectionChanged;
            TaskList.PreviewMouseWheel += TaskList_PreviewMouseWheel;
            TaskList.PreviewKeyDown += TaskList_PreviewKeyDown;
            TaskList.PreviewMouseLeftButtonDown += TaskList_PreviewMouseLeftButtonDown;
            TaskList.PreviewMouseMove += TaskList_PreviewMouseMove;
            TaskList.Drop += TaskList_Drop;
            TaskList.MouseDoubleClick += TaskList_MouseDoubleClick;

            ViewModel.PropertyChanged += ViewModel_PropertyChanged;

            // Silently re-saves to the current file (only once one exists and there are
            // unsaved edits) so a long editing session without a manual Ctrl+S still can't lose
            // more than a couple of minutes of work - on top of, not instead of, the recovery
            // snapshot above, which covers a project that's never been saved anywhere yet.
            _autosaveTimer.Tick += (_, _) =>
            {
                if (ViewModel.IsDirty && ViewModel.CurrentFilePath is not null)
                    ViewModel.SaveProjectCommand.Execute(null);
            };
            _autosaveTimer.Start();
        }

        private readonly DispatcherTimer _autosaveTimer = new() { Interval = TimeSpan.FromMinutes(2) };

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // ActualWidth right at Loaded can still reflect a pre-final layout pass (e.g. before
            // the GridSplitter columns settle), so defer one dispatcher cycle to get the real size.
            Dispatcher.BeginInvoke(() =>
            {
                UpdateTimelineViewportWidth();
                ViewModel.ResetZoomCommand.Execute(null);
            });

            if (MainViewModel.HasPendingRecovery())
            {
                var result = MessageBox.Show(
                    "GanttSquared didn't close properly last time. Restore the unsaved work from before it closed?",
                    "Restore Unsaved Work?", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                    ViewModel.RestoreFromRecovery();
                else
                    ViewModel.DiscardRecovery();
            }
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!ViewModel.ConfirmProceedPastUnsavedChanges("closing"))
            {
                e.Cancel = true;
                return;
            }

            _autosaveTimer.Stop();
            ViewModel.DiscardRecovery();
        }

        // The timeline's pixels-per-day is always derived from this, so the canvas keeps
        // exactly filling the available width (and showing the same day span) as the window,
        // the GridSplitter, or an appearing/disappearing scrollbar changes the viewport. Gantt
        // and Resources share one Timeline, so this reads whichever of the two canvases is
        // actually visible right now; a 0 from the other one (Collapsed) is ignored rather than
        // stomping the real value, and neither fires while on the Dashboard tab.
        private void UpdateTimelineViewportWidth()
        {
            var viewport = ViewModel.ActiveTab == MainTab.Resources ? ResourceCanvasScroll.ViewportWidth : CanvasScroll.ViewportWidth;
            if (viewport > 0)
                ViewModel.Timeline.ViewportWidth = viewport;
        }

        // ---- "Virtually infinite" horizontal scroll: rather than truly virtualizing the canvas
        // (rendering only the visible date window), which would mean reworking every layer of
        // the chart - gridlines, row stripes, bars, dependency lines, and the Resources tab's
        // mirror of all of that - this just widens Timeline.RangeStart/RangeEnd by a chunk
        // whenever the user scrolls within one viewport-width of either edge. Everything already
        // reacts to a range change (RecomputeLayout/RebuildResources via Timeline.PropertyChanged),
        // so this is the cheap 90% of the benefit: normal use never hits a hard edge, at the cost
        // of accumulating more realized elements the further someone scrolls in one sitting -
        // fine for the day/week/month distances a project timeline actually gets scrolled. ----

        private const int TimelineRangeExtensionDays = 30;
        private bool _isExtendingTimelineRange;

        // Keying this off raw offset ("am I near 0?") rather than direction was wrong: offset 0
        // is also just where every canvas *starts*, before the user has touched it - window
        // load, a tab switch, a zoom reset all land there too, and each one was reliably
        // mistaken for "user scrolled to the left edge" and yanked RangeStart back by a month
        // before anyone had scrolled at all. Requiring e.HorizontalChange to actually be moving
        // toward that edge (not just resting there) is what actually distinguishes the two.
        private void MaybeExtendTimelineRange(ScrollViewer scroll, ScrollChangedEventArgs e)
        {
            // Re-entrancy guard: widening RangeStart resizes the canvas, which raises another
            // ScrollChanged before the compensating offset (below) has been applied.
            if (_isExtendingTimelineRange)
                return;

            // Nothing to approach the edge of if the whole range already fits on screen.
            if (scroll.ViewportWidth <= 0 || scroll.ExtentWidth <= scroll.ViewportWidth)
                return;

            var timeline = ViewModel.Timeline;

            if (e.HorizontalChange < 0 && scroll.HorizontalOffset < scroll.ViewportWidth)
            {
                // Widening the start moves RangeStart earlier, which shifts every existing
                // element's X right (DateToX is relative to RangeStart) - so the view would
                // otherwise jump. Compensate by scrolling further right by the same pixel
                // amount, deferred until the layout pass triggered by the range change has
                // actually resized the canvas (ExtentWidth doesn't update synchronously).
                _isExtendingTimelineRange = true;
                var previousStart = timeline.RangeStart;
                timeline.RangeStart = previousStart.AddDays(-TimelineRangeExtensionDays);
                var addedWidth = (previousStart.DayNumber - timeline.RangeStart.DayNumber) * timeline.DayWidth;

                Dispatcher.BeginInvoke(() =>
                {
                    scroll.ScrollToHorizontalOffset(scroll.HorizontalOffset + addedWidth);
                    _isExtendingTimelineRange = false;
                }, DispatcherPriority.Render);
            }
            else if (e.HorizontalChange > 0 && scroll.ExtentWidth - scroll.HorizontalOffset - scroll.ViewportWidth < scroll.ViewportWidth)
            {
                // Widening the end only grows content further right - existing X positions
                // (relative to the unchanged RangeStart) don't move, so no offset compensation.
                timeline.RangeEnd = timeline.RangeEnd.AddDays(TimelineRangeExtensionDays);
            }
        }

        // ---- Jump navigation: center the canvas horizontally on a given date-derived x, used by both the "Today" button and each row's hover jump button ----

        private void JumpToToday_Click(object sender, RoutedEventArgs e) => ScrollCanvasToX(ViewModel.Timeline.TodayX);

        // Enter commits the rename by moving focus out (LostFocus is what ProjectName's binding
        // updates on). Escape discards it: the Text is reset to the last-committed ProjectName
        // first, since ClearFocus alone would still commit whatever's currently typed.
        private void ProjectNameTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && sender is TextBox textBox)
                textBox.Text = ViewModel.ProjectName;

            if (e.Key is Key.Enter or Key.Escape)
            {
                Keyboard.ClearFocus();
                e.Handled = true;
            }
        }

        private void ResourceJumpToToday_Click(object sender, RoutedEventArgs e) =>
            ResourceCanvasScroll.ScrollToHorizontalOffset(Math.Max(0, ViewModel.Timeline.TodayX - ResourceCanvasScroll.ViewportWidth / 2));

        private void JumpToTaskButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: TaskNodeViewModel node })
                ScrollCanvasToX(node.BarX + node.BarWidth / 2);
        }

        private void ScrollCanvasToX(double x) =>
            CanvasScroll.ScrollToHorizontalOffset(Math.Max(0, x - CanvasScroll.ViewportWidth / 2));

        // ---- Hover linkage: hovering a task's row highlights its bar (and vice versa) via the shared TaskNodeViewModel.IsHovered ----

        private void TaskRow_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: TaskNodeViewModel node })
                node.IsHovered = true;
        }

        private void TaskRow_MouseLeave(object sender, MouseEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: TaskNodeViewModel node })
                node.IsHovered = false;
        }

        // ---- Vertical scroll sync between the task list and the canvas; horizontal sync from the canvas to the (non-interactive) header ----

        private void CanvasScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (e.ViewportWidthChange != 0)
                UpdateTimelineViewportWidth();

            MaybeExtendTimelineRange(CanvasScroll, e);

            if (_syncingScroll)
                return;

            _syncingScroll = true;
            if (e.VerticalChange != 0)
                TaskListScroll.ScrollToVerticalOffset(e.VerticalOffset);
            if (e.HorizontalChange != 0)
                HeaderScroll.ScrollToHorizontalOffset(e.HorizontalOffset);
            _syncingScroll = false;
        }

        private void TaskListScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (_syncingScroll)
                return;

            _syncingScroll = true;
            if (e.VerticalChange != 0)
                CanvasScroll.ScrollToVerticalOffset(e.VerticalOffset);
            _syncingScroll = false;
        }

        // ---- Same vertical/horizontal scroll sync as above, for the Resources tab's name list + canvas + header ----

        private void ResourceCanvasScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (e.ViewportWidthChange != 0)
                UpdateTimelineViewportWidth();

            MaybeExtendTimelineRange(ResourceCanvasScroll, e);

            if (_syncingScroll)
                return;

            _syncingScroll = true;
            if (e.VerticalChange != 0)
                ResourceListScroll.ScrollToVerticalOffset(e.VerticalOffset);
            if (e.HorizontalChange != 0)
                ResourceHeaderScroll.ScrollToHorizontalOffset(e.HorizontalOffset);
            _syncingScroll = false;
        }

        private void ResourceListScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (_syncingScroll)
                return;

            _syncingScroll = true;
            if (e.VerticalChange != 0)
                ResourceCanvasScroll.ScrollToVerticalOffset(e.VerticalOffset);
            _syncingScroll = false;
        }

        // ---- Ctrl+scroll zoom, and click-drag panning on empty canvas background ----

        private void CanvasScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers != ModifierKeys.Control)
                return;

            if (e.Delta > 0)
                ViewModel.Timeline.ZoomIn();
            else
                ViewModel.Timeline.ZoomOut();
            e.Handled = true;
        }

        private void CanvasScroll_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _panStartPoint = e.GetPosition(CanvasScroll);
            _panStartH = CanvasScroll.HorizontalOffset;
            _panStartV = CanvasScroll.VerticalOffset;
            _isPanning = false;
            CanvasBodyGrid.CaptureMouse();
        }

        private void CanvasScroll_MouseMove(object sender, MouseEventArgs e)
        {
            if (_panStartPoint is null || e.LeftButton != MouseButtonState.Pressed)
                return;

            var pos = e.GetPosition(CanvasScroll);
            var dx = pos.X - _panStartPoint.Value.X;
            var dy = pos.Y - _panStartPoint.Value.Y;

            if (!_isPanning && Math.Abs(dx) < 3 && Math.Abs(dy) < 3)
                return;

            _isPanning = true;
            CanvasScroll.ScrollToHorizontalOffset(_panStartH - dx);
            CanvasScroll.ScrollToVerticalOffset(_panStartV - dy);
        }

        private void CanvasScroll_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            CanvasBodyGrid.ReleaseMouseCapture();
            _panStartPoint = null;
            _isPanning = false;
        }

        // ---- Same click-drag panning as above, for the Resources tab's canvas. Shares the
        // _panStartPoint/_panStartH/_panStartV/_isPanning fields with the Gantt version above -
        // safe since only one tab's canvas is ever visible/interactive at a time. ----

        private void ResourceCanvasScroll_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _panStartPoint = e.GetPosition(ResourceCanvasScroll);
            _panStartH = ResourceCanvasScroll.HorizontalOffset;
            _panStartV = ResourceCanvasScroll.VerticalOffset;
            _isPanning = false;
            ResourceCanvasBodyGrid.CaptureMouse();
        }

        private void ResourceCanvasScroll_MouseMove(object sender, MouseEventArgs e)
        {
            if (_panStartPoint is null || e.LeftButton != MouseButtonState.Pressed)
                return;

            var pos = e.GetPosition(ResourceCanvasScroll);
            var dx = pos.X - _panStartPoint.Value.X;
            var dy = pos.Y - _panStartPoint.Value.Y;

            if (!_isPanning && Math.Abs(dx) < 3 && Math.Abs(dy) < 3)
                return;

            _isPanning = true;
            ResourceCanvasScroll.ScrollToHorizontalOffset(_panStartH - dx);
            ResourceCanvasScroll.ScrollToVerticalOffset(_panStartV - dy);
        }

        private void ResourceCanvasScroll_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            ResourceCanvasBodyGrid.ReleaseMouseCapture();
            _panStartPoint = null;
            _isPanning = false;
        }

        // ---- Gantt bar drag: move (middle) and resize (edges within ~6px) ----

        private void Bar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement el || el.DataContext is not TaskNodeViewModel node)
                return;

            var pos = e.GetPosition(el);
            var width = el.ActualWidth;
            _barDragResizeLeft = !node.IsMilestone && !node.IsGroup && pos.X <= 6;
            _barDragResizeRight = !node.IsMilestone && !node.IsGroup && pos.X >= width - 6;
            _barDragStartPoint = e.GetPosition(CanvasScroll);
            _barDragElement = el;
            _barDragActive = false;

            el.CaptureMouse();
            e.Handled = true;
        }

        private void Bar_MouseMove(object sender, MouseEventArgs e)
        {
            if (sender is not FrameworkElement el || el.DataContext is not TaskNodeViewModel node)
                return;

            if (_barDragStartPoint is null || _barDragElement != el || e.LeftButton != MouseButtonState.Pressed)
            {
                if (!node.IsMilestone && !node.IsGroup)
                {
                    var hoverPos = e.GetPosition(el);
                    el.Cursor = hoverPos.X <= 6 || hoverPos.X >= el.ActualWidth - 6 ? Cursors.SizeWE : Cursors.SizeAll;
                }
                return;
            }

            var current = e.GetPosition(CanvasScroll);
            var dx = current.X - _barDragStartPoint.Value.X;

            if (!_barDragActive)
            {
                if (Math.Abs(dx) < 3)
                    return;

                if (!ViewModel.BeginBarDrag(node, _barDragResizeLeft, _barDragResizeRight))
                {
                    _barDragElement = null;
                    return;
                }

                _barDragActive = true;
            }

            var dayDelta = (int)Math.Round(dx / ViewModel.Timeline.DayWidth);
            ViewModel.UpdateBarDrag(dayDelta, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
        }

        private void Bar_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement el)
                return;

            el.ReleaseMouseCapture();

            if (_barDragActive && _barDragElement == el && _barDragStartPoint is { } start)
            {
                var current = e.GetPosition(CanvasScroll);
                var dayDelta = (int)Math.Round((current.X - start.X) / ViewModel.Timeline.DayWidth);
                ViewModel.EndBarDrag(dayDelta, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
            }
            else if (el.DataContext is TaskNodeViewModel node)
            {
                // No drag occurred: treat this as a plain click-to-select.
                ViewModel.SetSelection(new[] { node });
            }

            _barDragStartPoint = null;
            _barDragElement = null;
            _barDragActive = false;
            e.Handled = true;
        }

        // ---- Dependency-link drag: from a bar's hover handle onto another bar ----

        private void LinkHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement el || el.DataContext is not TaskNodeViewModel node)
                return;

            ViewModel.BeginLinkDrag(node);
            el.CaptureMouse();
            e.Handled = true;
        }

        private void LinkHandle_MouseMove(object sender, MouseEventArgs e)
        {
            if (!ViewModel.IsDraggingLink)
                return;

            var pos = e.GetPosition(CanvasBodyGrid);
            ViewModel.UpdateLinkDrag(pos.X, pos.Y, HitTestBarNode(pos));
        }

        private void LinkHandle_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement el)
                el.ReleaseMouseCapture();

            if (!ViewModel.IsDraggingLink)
                return;

            var pos = e.GetPosition(CanvasBodyGrid);
            ViewModel.EndLinkDrag(HitTestBarNode(pos));
            e.Handled = true;
        }

        private TaskNodeViewModel? HitTestBarNode(Point pointInCanvasBody)
        {
            TaskNodeViewModel? found = null;
            VisualTreeHelper.HitTest(
                CanvasBodyGrid,
                null,
                result =>
                {
                    if (result.VisualHit is FrameworkElement { DataContext: TaskNodeViewModel node })
                    {
                        found = node;
                        return HitTestResultBehavior.Stop;
                    }
                    return HitTestResultBehavior.Continue;
                },
                new PointHitTestParameters(pointInCanvasBody));
            return found;
        }

        // A dependency line has no other affordance to remove it by (no context menu
        // infrastructure exists yet), so a direct click deletes it immediately - safe since
        // it's a single undoable command, same as every other edit here.
        private void DependencyLine_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: DependencyLineViewModel link })
                ViewModel.RemoveDependency(link.DependencyId);
            e.Handled = true;
        }

        // ---- Task list: multi-select sync, keyboard nav, inline rename, drag-to-reparent ----

        private void TaskList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncingSelection)
                return;

            _syncingSelection = true;
            ViewModel.SetSelection(TaskList.SelectedItems.Cast<TaskNodeViewModel>());
            _syncingSelection = false;
        }

        // TaskList's own internal ScrollViewer part has scrolling turned off (see the XAML
        // comment on the ListBox) so the outer TaskListScroll can be the one and only source of
        // truth for vertical position, kept in sync with the canvas - but that means a mouse
        // wheel over the list would otherwise just hit that inert internal scroller and go
        // nowhere. Forward it to the outer one directly instead.
        private void TaskList_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            // Ctrl+scroll zooms here too, matching the canvas - the task list and canvas read as
            // one view (their rows line up), so the same gesture should do the same thing
            // regardless of which side of the splitter the cursor happens to be on.
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (e.Delta > 0)
                    ViewModel.Timeline.ZoomIn();
                else
                    ViewModel.Timeline.ZoomOut();
                e.Handled = true;
                return;
            }

            // e.Delta is +/-120 per notch; scale it down so one notch feels like a normal few-line
            // scroll instead of jumping ~120px (a raw 1:1 mapping was reported as far too fast).
            TaskListScroll.ScrollToVerticalOffset(TaskListScroll.VerticalOffset - e.Delta / 3.0);
            e.Handled = true;
        }

        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.IsDarkTheme))
            {
                ApplyTheme(ViewModel.IsDarkTheme);
                return;
            }

            if (e.PropertyName == nameof(MainViewModel.ActiveTab))
            {
                // Whichever canvas just became visible gets its own SizeChanged as it's laid
                // out, but that can land before the tab switch's layout pass is fully settled -
                // same one-dispatcher-cycle deferral as the initial load, for the same reason.
                Dispatcher.BeginInvoke(UpdateTimelineViewportWidth);
                return;
            }

            if (_syncingSelection || e.PropertyName != nameof(MainViewModel.SelectedNode))
                return;

            _syncingSelection = true;
            TaskList.SelectedItem = ViewModel.SelectedNode;
            _syncingSelection = false;
        }

        // Every {StaticResource X} reference in MainWindow.xaml resolved to the SAME brush
        // INSTANCE at load time and keeps that reference for the window's lifetime - but
        // SolidColorBrush.Color is itself a dependency property, so mutating it here repaints
        // every use of that brush immediately. Converting the whole file to DynamicResource
        // (the more usual way to support runtime theme switching) would touch dozens of
        // unrelated bindings; this reaches the same result without it.
        private static readonly (string Key, string Dark, string Light)[] ThemeBrushes =
        {
            ("WindowBackgroundBrush", "#FF101114", "#FFE7E9ED"),
            ("PanelBrush", "#FF191A1F", "#FFFFFFFF"),
            ("PanelAltBrush", "#FF212228", "#FFEFF1F5"),
            ("BorderBrush2", "#FF3C3E4A", "#FFD6D9E1"),
            ("TextBrush", "#FFE8E9ED", "#FF1F2328"),
            ("MutedTextBrush", "#FF9AA0AC", "#FF6B7280"),
            ("RowAltBrush", "#FF2E313D", "#FFDFE3EC"),
            ("SecondaryButtonBrush", "#FF3A3B42", "#FFDDE1E8"),
            ("FieldBackgroundBrush", "#FF2C2E38", "#FFF3F5F8"),
            ("CanvasBackgroundBrush", "#FF121319", "#FFEFF1F5"),
            ("LinkLineBrush", "#FF8B94A6", "#FF475569"),
        };

        // These SystemColors keys are overridden once in XAML (see the comment above them there)
        // so the TreeView's selected-but-unfocused row and the DatePicker calendar's built-in
        // header/nav buttons stay legible instead of falling back to the system theme's default
        // grey. They were hardcoded to dark-theme colors only, so switching to light theme left
        // them stuck dark - a dark selection box or a dark nav button sitting inside an otherwise
        // light popup. Swapping them here alongside everything else fixes that clash.
        private static readonly (object Key, string Dark, string Light)[] SystemColorBrushes =
        {
            (SystemColors.ControlBrushKey, "#FF3C3E4A", "#FFD6D9E1"),
            (SystemColors.ControlTextBrushKey, "#FFE8E9ED", "#FF1F2328"),
            (SystemColors.InactiveSelectionHighlightBrushKey, "#FF3C3E4A", "#FFDDE3EF"),
            (SystemColors.InactiveSelectionHighlightTextBrushKey, "#FFE8E9ED", "#FF1F2328"),
            (SystemColors.WindowTextBrushKey, "#FFE8E9ED", "#FF1F2328"),
            (SystemColors.GrayTextBrushKey, "#FF9AA0AC", "#FF6B7280"),
        };

        private void ApplyTheme(bool isDark)
        {
            // WPF auto-freezes some of these SolidColorBrush resources at load time (a
            // performance optimization for Freezables that are only ever consumed via
            // StaticResource with no bindings/animations) - mutating a frozen brush's Color
            // throws "read-only state". Replacing the dictionary entry with a fresh instance
            // each time sidesteps that entirely, and is exactly what DynamicResource (which
            // every consumer below was switched to) is designed to propagate: StaticResource
            // consumers keep the reference they first resolved forever, so this technique only
            // works because nothing here uses StaticResource for these keys.
            foreach (var (key, dark, light) in ThemeBrushes)
            {
                var color = (Color)ColorConverter.ConvertFromString(isDark ? dark : light);
                Resources[key] = new SolidColorBrush(color);
            }

            foreach (var (key, dark, light) in SystemColorBrushes)
            {
                var color = (Color)ColorConverter.ConvertFromString(isDark ? dark : light);
                Resources[key] = new SolidColorBrush(color);
            }

            // Window.Background can't reference WindowBackgroundBrush via {Dynamic|Static}Resource
            // in XAML (a self-reference on the root element's own attribute resolves before its
            // own Window.Resources dictionary is populated), so it's wired up here instead.
            if (Resources["WindowBackgroundBrush"] is SolidColorBrush windowBackground)
                Background = windowBackground;
        }

        private void TaskList_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Up || e.Key == Key.Down)
            {
                var rows = ViewModel.VisibleRows;
                if (rows.Count == 0)
                    return;

                var idx = ViewModel.SelectedNode is { } current ? rows.IndexOf(current) : -1;
                var newIdx = e.Key == Key.Up ? Math.Max(0, idx - 1) : Math.Min(rows.Count - 1, idx + 1);
                ViewModel.SetSelection(new[] { rows[newIdx] });
                e.Handled = true;
            }
            else if ((e.Key == Key.F2 || e.Key == Key.Enter) && ViewModel.SelectedNode is { } node && !node.IsEditingName)
            {
                BeginInlineRename(node);
                e.Handled = true;
            }
            else if (e.Key == Key.Delete && ViewModel.SelectedNode is not { IsEditingName: true })
            {
                if (ViewModel.DeleteSelectedCommand.CanExecute(null))
                    ViewModel.DeleteSelectedCommand.Execute(null);
                e.Handled = true;
            }
            else if (e.Key == Key.Tab && ViewModel.SelectedNode is not { IsEditingName: true })
            {
                var isOutdent = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
                var command = isOutdent ? ViewModel.OutdentSelectedCommand : ViewModel.IndentSelectedCommand;
                if (command.CanExecute(null))
                    command.Execute(null);
                e.Handled = true;
            }
        }

        private void BeginInlineRename(TaskNodeViewModel node)
        {
            node.BeginRename();
            Dispatcher.BeginInvoke(() =>
            {
                if (TaskList.ItemContainerGenerator.ContainerFromItem(node) is not ListBoxItem item)
                    return;

                if (FindVisualChild<TextBox>(item, "InlineRenameBox") is { } textBox)
                {
                    textBox.Focus();
                    textBox.SelectAll();
                }
            });
        }

        private static T? FindVisualChild<T>(DependencyObject parent, string? name = null) where T : FrameworkElement
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typed && (name is null || typed.Name == name))
                    return typed;

                if (FindVisualChild<T>(child, name) is { } found)
                    return found;
            }

            return null;
        }

        private void InlineRenameBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (sender is not TextBox tb || tb.DataContext is not TaskNodeViewModel node)
                return;

            if (e.Key == Key.Enter)
            {
                ViewModel.CommitInlineRename(node, tb.Text);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                node.IsEditingName = false;
                e.Handled = true;
            }
        }

        private void InlineRenameBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb && tb.DataContext is TaskNodeViewModel node && node.IsEditingName)
                ViewModel.CommitInlineRename(node, tb.Text);
        }

        private void TaskList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (FindRowNode(e.OriginalSource as DependencyObject) is { IsEditingName: false } node)
                BeginInlineRename(node);
        }

        private void TaskList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _reparentDragStartPoint = e.GetPosition(TaskList);
            _reparentDragSource = FindRowNode(e.OriginalSource as DependencyObject);
        }

        private void TaskList_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || _reparentDragStartPoint is null || _reparentDragSource is null)
                return;

            var pos = e.GetPosition(TaskList);
            if (Math.Abs(pos.X - _reparentDragStartPoint.Value.X) < 6 && Math.Abs(pos.Y - _reparentDragStartPoint.Value.Y) < 6)
                return;

            var source = _reparentDragSource;
            _reparentDragStartPoint = null;
            _reparentDragSource = null;

            DragDrop.DoDragDrop(TaskList, new DataObject(typeof(TaskNodeViewModel), source), DragDropEffects.Move);
        }

        private void TaskList_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(typeof(TaskNodeViewModel)))
                return;

            var source = (TaskNodeViewModel)e.Data.GetData(typeof(TaskNodeViewModel))!;
            var target = FindRowNode(e.OriginalSource as DependencyObject);
            ViewModel.ReparentTask(source, target, DropPositionFor(target, e));
        }

        /// <summary>Classifies where within the target row the drop landed: near the top/bottom third
        /// means "insert as a sibling before/after this row", the middle third means "nest inside it".</summary>
        private MainViewModel.DropPosition DropPositionFor(TaskNodeViewModel? target, DragEventArgs e)
        {
            if (target is null || TaskList.ItemContainerGenerator.ContainerFromItem(target) is not ListBoxItem container || container.ActualHeight <= 0)
                return MainViewModel.DropPosition.Into;

            var relativeY = e.GetPosition(container).Y / container.ActualHeight;
            return relativeY < 0.3 ? MainViewModel.DropPosition.Before
                : relativeY > 0.7 ? MainViewModel.DropPosition.After
                : MainViewModel.DropPosition.Into;
        }

        private static TaskNodeViewModel? FindRowNode(DependencyObject? element)
        {
            while (element is not null)
            {
                if (element is FrameworkElement { DataContext: TaskNodeViewModel node })
                    return node;
                element = VisualTreeHelper.GetParent(element);
            }

            return null;
        }

        private void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && ViewModel.SearchCommand.CanExecute(null))
                ViewModel.SearchCommand.Execute(null);
        }

        // There's no WPF-native color picker, hence the WinForms dialog (see the
        // FrameworkReference comment in GanttSquared.csproj for why it's referenced that way
        // instead of via UseWindowsForms). Works for both the single-task and bulk-edit color
        // fields since both bind to the same TaskPropertiesViewModel.Color property - the swatch
        // that triggers this just needs its DataContext to already be that view model.
        private void ColorSwatch_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement { DataContext: TaskPropertiesViewModel properties })
                return;

            using var dialog = new WinFormsColorDialog { FullOpen = true };
            if (!string.IsNullOrWhiteSpace(properties.Color))
            {
                try
                {
                    var c = (Color)ColorConverter.ConvertFromString(properties.Color);
                    dialog.Color = DrawingColor.FromArgb(c.R, c.G, c.B);
                }
                catch (FormatException)
                {
                    // Malformed hex already in the field; just open the picker with its default.
                }
            }

            if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                return;

            var picked = dialog.Color;
            properties.Color = $"#{picked.R:X2}{picked.G:X2}{picked.B:X2}";
        }

        // Renders the timeline header and the full (unclipped) canvas body - both already laid
        // out at their true full-project size regardless of the current scroll position, since
        // that's what CanvasWidth/CanvasHeight bind their Canvas panels to - into one PNG via
        // VisualBrush, which captures a Visual's rendered content independent of any ancestor
        // ScrollViewer's clip. The task list isn't included: it's a virtualizing ListBox that
        // only realizes on-screen rows, so it can't be captured full-height this way, but every
        // bar already carries its own task name as an on-bar label, so the image is still
        // self-describing without it.
        private void ExportChart_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Filter = "PNG Image (*.png)|*.png|PDF Document (*.pdf)|*.pdf",
                DefaultExt = ".png",
                FileName = ViewModel.Project.Name
            };

            if (dialog.ShowDialog() != true)
                return;

            const int headerHeight = 32;
            var width = (int)Math.Ceiling(Math.Max(CanvasBodyGrid.ActualWidth, 1));
            var bodyHeight = (int)Math.Ceiling(Math.Max(CanvasBodyGrid.ActualHeight, 1));
            var totalHeight = headerHeight + bodyHeight;

            var target = new RenderTargetBitmap(width, totalHeight, 96, 96, PixelFormats.Pbgra32);
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                if (Resources["CanvasBackgroundBrush"] is Brush background)
                    dc.DrawRectangle(background, null, new Rect(0, 0, width, totalHeight));
                dc.DrawRectangle(new VisualBrush(HeaderContent), null, new Rect(0, 0, width, headerHeight));
                dc.DrawRectangle(new VisualBrush(CanvasBodyGrid), null, new Rect(0, headerHeight, width, bodyHeight));
            }
            target.Render(visual);

            try
            {
                if (Path.GetExtension(dialog.FileName).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
                    PdfExport.WriteSinglePageImagePdf(target, dialog.FileName);
                else
                {
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(target));
                    using var stream = File.Create(dialog.FileName);
                    encoder.Save(stream);
                }
            }
            catch (IOException ex)
            {
                MessageBox.Show($"Couldn't save the exported image:\n\n{ex.Message}", "Export Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
