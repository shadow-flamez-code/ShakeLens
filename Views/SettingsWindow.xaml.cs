using System.Windows;
using System.Windows.Interop;
using ShakeLens.Core;

namespace ShakeLens.Views;

public partial class SettingsWindow : Window
{
    private readonly Settings _settings;

    public SettingsWindow(Settings settings)
    {
        _settings = settings;
        InitializeComponent();

        SourceInitialized += (_, _) =>
        {
            Native.UseDarkTitleBar(this);
            Native.UseRoundedCorners(this);
        };

        Loaded += (_, _) =>
        {
            ShakeEnabled.IsChecked = settings.ShakeEnabled;
            Sensitivity.SelectedIndex = Math.Clamp(settings.Sensitivity, 0, 2);
            Browser.SelectedIndex = Math.Max(0, new[] { "default", "chrome", "edge", "firefox" }
                .ToList().IndexOf(settings.Browser));
            UploadSize.SelectedIndex = new[] { 1000, 1400, 2000, 2560 }
                .Select((size, index) => (size, index))
                .Where(x => x.size == settings.UploadSize)
                .Select(x => x.index)
                .FirstOrDefault(1);
            Provider.SelectedIndex = Math.Max(0, new[] { "none", "gemini", "openai", "claude" }
                .ToList().IndexOf(settings.AiProvider));
            ApiKey.Text = settings.AiKey;
            Model.Text = settings.AiModel;
            Prompt.Text = settings.AiPrompt;
            AutoStart.IsChecked = settings.IsAutoStartEnabled();

            LensLanguage.Items.Clear();
            (string Label, string Value)[] languages =
            {
                ("Auto (system)", "auto"),
                ("English", "en"),
                ("Deutsch", "de"),
                ("Francais", "fr"),
                ("Espanol", "es"),
                ("Italiano", "it"),
                ("Portugues", "pt"),
                ("Nederlands", "nl"),
                ("Polski", "pl"),
                ("Russian", "ru"),
                ("Japanese", "ja"),
                ("Korean", "ko"),
                ("Chinese (Simplified)", "zh-CN"),
                ("Chinese (Traditional)", "zh-TW")
            };
            int selected = 0;
            for (int i = 0; i < languages.Length; i++)
            {
                LensLanguage.Items.Add(languages[i].Label);
                if (languages[i].Value == settings.LensLanguage) selected = i;
            }
            LensLanguage.SelectedIndex = selected;

            // start at the top, a focused control would otherwise scroll away
            ContentScroll.ScrollToTop();
        };
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        _settings.ShakeEnabled = ShakeEnabled.IsChecked == true;
        _settings.Sensitivity = Sensitivity.SelectedIndex;
        _settings.LensLanguage = LensLanguage.SelectedItem?.ToString() == "Auto (system)"
            ? "auto"
            : ValueOfLanguage(LensLanguage.SelectedIndex);
        _settings.Browser = new[] { "default", "chrome", "edge", "firefox" }[Math.Max(0, Browser.SelectedIndex)];
        _settings.UploadSize = new[] { 1000, 1400, 2000, 2560 }[Math.Max(0, UploadSize.SelectedIndex)];
        _settings.AiProvider = new[] { "none", "gemini", "openai", "claude" }[Math.Max(0, Provider.SelectedIndex)];
        _settings.AiKey = ApiKey.Text.Trim();
        _settings.AiModel = Model.Text.Trim();
        if (!string.IsNullOrWhiteSpace(Prompt.Text)) _settings.AiPrompt = Prompt.Text;

        bool autoStart = AutoStart.IsChecked == true;
        if (autoStart != _settings.AutoStart)
        {
            _settings.SetAutoStart(autoStart);
            _settings.AutoStart = autoStart;
        }

        _settings.Save();
        Close();
    }

    private string ValueOfLanguage(int index)
    {
        string[] values = { "auto", "en", "de", "fr", "es", "it", "pt", "nl", "pl", "ru", "ja", "ko", "zh-CN", "zh-TW" };
        return index >= 0 && index < values.Length ? values[index] : "auto";
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => Close();
}
