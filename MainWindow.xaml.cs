using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GanttSquared.ViewModels;

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

            TaskList.SelectionChanged += TaskList_SelectionChanged;
            TaskList.PreviewKeyDown += TaskList_PreviewKeyDown;
            TaskList.PreviewMouseLeftButtonDown += TaskList_PreviewMouseLeftButtonDown;
            TaskList.PreviewMouseMove += TaskList_PreviewMouseMove;
            TaskList.Drop += TaskList_Drop;
            TaskList.MouseDoubleClick += TaskList_MouseDoubleClick;

            ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // ActualWidth right at Loaded can still reflect a pre-final layout pass (e.g. before
            // the GridSplitter columns settle), so defer one dispatcher cycle to get the real size.
            Dispatcher.BeginInvoke(() =>
            {
                UpdateTimelineViewportWidth();
                ViewModel.ResetZoomCommand.Execute(null);
            });
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!ViewModel.ConfirmProceedPastUnsavedChanges("closing"))
                e.Cancel = true;
        }

        // The timeline's pixels-per-day is always derived from this, so the canvas keeps
        // exactly filling the available width (and showing the same day span) as the window,
        // the GridSplitter, or an appearing/disappearing scrollbar changes the viewport.
        private void UpdateTimelineViewportWidth() =>
            ViewModel.Timeline.ViewportWidth = CanvasScroll.ViewportWidth;

        // ---- Vertical scroll sync between the task list and the canvas; horizontal sync from the canvas to the (non-interactive) header ----

        private void CanvasScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (e.ViewportWidthChange != 0)
                UpdateTimelineViewportWidth();

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

        // ---- Task list: multi-select sync, keyboard nav, inline rename, drag-to-reparent ----

        private void TaskList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncingSelection)
                return;

            _syncingSelection = true;
            ViewModel.SetSelection(TaskList.SelectedItems.Cast<TaskNodeViewModel>());
            _syncingSelection = false;
        }

        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.IsDarkTheme))
            {
                ApplyTheme(ViewModel.IsDarkTheme);
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
            ("WindowBackgroundBrush", "#FF17181C", "#FFF3F4F6"),
            ("PanelBrush", "#FF1E1F24", "#FFFFFFFF"),
            ("PanelAltBrush", "#FF24252B", "#FFF3F4F6"),
            ("BorderBrush2", "#FF34353D", "#FFE2E4E9"),
            ("TextBrush", "#FFE8E9ED", "#FF1F2328"),
            ("MutedTextBrush", "#FF9AA0AC", "#FF6B7280"),
            ("RowAltBrush", "#FF212227", "#FFF8F9FB"),
            ("FieldBackgroundBrush", "#FF2A2B32", "#FFF3F4F6"),
            ("CanvasBackgroundBrush", "#FF19191E", "#FFFAFAFB"),
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
    }
}
