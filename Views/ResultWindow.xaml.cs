using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ShakeLens.Core;

namespace ShakeLens.Views;

public partial class ResultWindow : Window
{
    public ResultWindow()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            Opacity = 0;
            var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(170))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            BeginAnimation(OpacityProperty, fade);

            var scale = new ScaleTransform(0.97, 0.97);
            PanelScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty,
                new DoubleAnimation(0.97, 1.0, TimeSpan.FromMilliseconds(190))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
            PanelScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty,
                new DoubleAnimation(0.97, 1.0, TimeSpan.FromMilliseconds(190))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
        };
    }

    private Window? _owner;

    public void Present(Window? owner, string title, string body, bool isError)
    {
        _owner = owner;
        TitleText.Text = title;
        BodyText.Text = body;
        TitleText.Foreground = isError
            ? (Brush)Application.Current.Resources["DangerBrush"]
            : (Brush)Application.Current.Resources["TextBrush"];

        double width = 520;
        double height = Math.Min(520, 220 + body.Length * 0.42);

        Width = width;
        Height = height;
        SizeToContent = SizeToContent.Manual;

        if (owner is not null)
        {
            Left = owner.Left + (owner.Width - width) / 2;
            Top = owner.Top + (owner.Height - height) / 2;
        }

        Show();
        Activate();
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(BodyText.Text);
            CopyButton.IsEnabled = false;
        }
        catch { /* ignore */ }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        _owner = null;
        base.OnClosed(e);
    }
}
