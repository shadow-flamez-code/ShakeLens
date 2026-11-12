using System.Windows;
using ShakeLens.Core;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace ShakeLens.Views;

public sealed record MenuEntry(string Icon, string Label, Action? Action, bool IsChecked = false);

public partial class TrayMenuWindow : Window
{
    private readonly List<MenuEntry> _entries;

    public TrayMenuWindow(IEnumerable<MenuEntry> entries)
    {
        _entries = entries.ToList();
        InitializeComponent();

        Loaded += (_, _) =>
        {
            Build();
            Position();
            Opacity = 0;
            var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120));
            BeginAnimation(OpacityProperty, fade);

            // the menu stays open until a click lands outside of it
            _watch = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
            _watch.Tick += (_, _) => WatchMouse();
            _watch.Start();
        };

        Closed += (_, _) => _watch?.Stop();
    }

    private DispatcherTimer? _watch;
    private bool _buttonWasDown;

    private void WatchMouse()
    {
        bool down = Native.IsMouseButtonDown(0x01) || Native.IsMouseButtonDown(0x02);
        if (down)
        {
            _buttonWasDown = true;
            return;
        }
        if (!_buttonWasDown) return;

        _buttonWasDown = false;
        (int x, int y) = Native.CursorPosition();
        if (!IsInside(x, y)) Hide();
    }

    private bool IsInside(int x, int y)
    {
        Point local = PointFromScreen(new Point(x, y));
        return local.X >= 0 && local.Y >= 0 && local.X <= ActualWidth && local.Y <= ActualHeight;
    }

    private void Build()
    {
        foreach (MenuEntry entry in _entries)
        {
            if (entry.Icon == "-")
            {
                Items.Children.Add(new Separator
                {
                    Margin = new Thickness(6, 4, 6, 4),
                    Background = (Brush)Application.Current.Resources["OutlineBrush"]
                });
                continue;
            }

            var button = new Button
            {
                Height = 34,
                Cursor = Cursors.Hand,
                Focusable = false,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(10, 0, 12, 0),
                HorizontalContentAlignment = HorizontalAlignment.Left
            };

            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            panel.Children.Add(new TextBlock
            {
                Text = entry.Icon,
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 14,
                Foreground = (Brush)Application.Current.Resources["TextDimBrush"],
                VerticalAlignment = VerticalAlignment.Center,
                Width = 24
            });
            panel.Children.Add(new TextBlock
            {
                Text = entry.Label,
                Foreground = (Brush)Application.Current.Resources["TextBrush"],
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center
            });
            if (entry.IsChecked)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = "&#xE73E;",
                    FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    FontSize = 12,
                    Foreground = (Brush)Application.Current.Resources["AccentBrush"],
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(10, 0, 0, 0)
                });
            }
            button.Content = panel;

            var template = new ControlTemplate(typeof(Button));
            var border = new FrameworkElementFactory(typeof(Border));
            border.Name = "bg";
            border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            border.SetValue(Border.PaddingProperty, new Thickness(0));
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
            border.AppendChild(presenter);
            template.VisualTree = border;

            // the hover colour has to live in the template, a style trigger
            // cannot target the border element
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            var setter = new Setter(Border.BackgroundProperty,
                (Brush)Application.Current.Resources["SurfaceHighBrush"]);
            setter.TargetName = "bg";
            hover.Setters.Add(setter);
            template.Triggers.Add(hover);

            var style = new Style(typeof(Button));
            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            button.Style = style;

            if (entry.Action is not null)
            {
                button.Click += (_, _) =>
                {
                    Hide();
                    entry.Action();
                };
            }

            Items.Children.Add(button);
        }
    }

    private void Position()
    {
        (int x, int y) = Core.Native.CursorPosition();
        double width = ActualWidth;
        double height = ActualHeight;

        double left = x - width + 8;
        double top = y + 8;

        if (left < 8) left = x + 8;
        if (top + height > SystemParameters.WorkArea.Height - 8) top = y - height - 8;
        if (top < 8) top = 8;

        Left = left;
        Top = top;
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        // the menu is shown without activation, so it only closes on a click
        // outside of it - handled by the watcher
    }
}
