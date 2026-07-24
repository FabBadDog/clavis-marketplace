using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace FabioSoft.Nucleus.Plugins.TaskTracker;

/// The status-bar tracker control: a borderless count line ("N Tasks") that toggles a popover list of the
/// live tasks. A running task is a breathing outlined ring; a finished one a green check with its state.
/// The list rides a Popup (Placement=Top) so it opens upward out of the bottom status bar instead of being
/// clipped by the bar's height. The whole strip collapses when there are no tasks. Rebuilt wholesale on
/// each Apply - the task counts here are tiny, so a full rebuild is simpler than diffing and never stale.
[ExcludeFromCodeCoverage] // WPF construction + animation
internal sealed class TaskTrackerView
{
    private const double IndicatorSize = 14;

    private readonly StackPanel _root;
    private readonly TextBlock _countLine;
    private readonly Run _countNumber;
    private readonly Run _countLabel;
    private readonly Popup _popup;
    private readonly StackPanel _list;

    public TaskTrackerView()
    {
        _countNumber = new Run();
        _countNumber.SetResourceReference(TextElement.ForegroundProperty, "ClavisBrush");
        _countLabel = new Run();
        _countLabel.SetResourceReference(TextElement.ForegroundProperty, "TextBrush");

        _countLine = new TextBlock
        {
            FontSize = 12,
            Cursor = System.Windows.Input.Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center
        };
        _countLine.Inlines.Add(_countNumber);
        _countLine.Inlines.Add(_countLabel);
        _countLine.SetResourceReference(TextBlock.FontFamilyProperty, "UiFont");
        _countLine.MouseLeftButtonUp += (_, _) => _popup.IsOpen = !_popup.IsOpen;

        _list = new StackPanel { Margin = new Thickness(12, 10, 12, 10) };
        var listSurface = new StackPanel { MinWidth = 260, MaxWidth = 420 };
        listSurface.SetResourceReference(Panel.BackgroundProperty, "BlackBrush");
        listSurface.Children.Add(_list);

        _popup = new Popup
        {
            Child = listSurface,
            PlacementTarget = _countLine,
            Placement = PlacementMode.Top,
            StaysOpen = false,
            AllowsTransparency = true,
            HorizontalOffset = 0,
            VerticalOffset = -6
        };

        _root = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        _root.Children.Add(_countLine);
        _root.Children.Add(_popup);
    }

    public FrameworkElement Element => _root;

    public void Apply(IReadOnlyList<TaskEntry> tasks)
    {
        if (tasks.Count == 0)
        {
            _root.Visibility = Visibility.Collapsed;
            _popup.IsOpen = false;
            return;
        }

        _root.Visibility = Visibility.Visible;
        _countNumber.Text = tasks.Count.ToString();
        _countLabel.Text = tasks.Count == 1 ? " Task" : " Tasks";

        _list.Children.Clear();
        foreach (var task in tasks)
        {
            _list.Children.Add(CreateRow(task));
        }
    }

    private static FrameworkElement CreateRow(TaskEntry task)
    {
        var row = new Grid { Margin = new Thickness(0, 5, 0, 5) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(IndicatorSize) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var indicator = task.IsDone ? DoneCheck() : RunningRing();
        indicator.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(indicator, 0);
        row.Children.Add(indicator);

        var description = new TextBlock
        {
            Text = task.Description,
            FontSize = 13.5,
            Margin = new Thickness(11, 0, 11, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        description.SetResourceReference(TextBlock.ForegroundProperty, "TextBrightBrush");
        Grid.SetColumn(description, 1);
        row.Children.Add(description);

        var trailing = new TextBlock
        {
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center
        };
        trailing.SetResourceReference(TextBlock.FontFamilyProperty, task.IsDone ? "UiFont" : "MonoFont");
        if (task.IsDone)
        {
            trailing.Text = string.IsNullOrEmpty(task.Status) ? "done" : task.Status;
            trailing.SetResourceReference(TextBlock.ForegroundProperty, "GreenBrush");
            if (!string.IsNullOrEmpty(task.Summary))
            {
                trailing.ToolTip = task.Summary;
            }
        }
        else
        {
            trailing.Text = task.TaskType;
            trailing.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
        }

        Grid.SetColumn(trailing, 2);
        row.Children.Add(trailing);

        return row;
    }

    // Running: an unfilled circle the size of the done check, with a near-white outline, breathing so the
    // motion signals "in progress" without a second element.
    private static FrameworkElement RunningRing()
    {
        var ring = new Ellipse
        {
            Width = IndicatorSize,
            Height = IndicatorSize,
            StrokeThickness = 1,
            Fill = Brushes.Transparent
        };
        ring.SetResourceReference(Shape.StrokeProperty, "TextBrightBrush");

        var breathe = new DoubleAnimation
        {
            From = 0.35,
            To = 1.0,
            Duration = TimeSpan.FromSeconds(1.9),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        ring.BeginAnimation(UIElement.OpacityProperty, breathe);
        return ring;
    }

    // Done: a filled green circle with a dark check glyph, same diameter as the ring.
    private static FrameworkElement DoneCheck()
    {
        var disc = new Grid { Width = IndicatorSize, Height = IndicatorSize };

        var circle = new Ellipse { Width = IndicatorSize, Height = IndicatorSize };
        circle.SetResourceReference(Shape.FillProperty, "GreenBrush");
        disc.Children.Add(circle);

        var check = new TextBlock
        {
            Text = "✓",
            FontSize = 9,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        check.SetResourceReference(TextBlock.ForegroundProperty, "BlackBrush");
        disc.Children.Add(check);

        return disc;
    }
}
