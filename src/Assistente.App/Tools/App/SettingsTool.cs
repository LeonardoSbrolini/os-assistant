using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Assistente.App.Services;

namespace Assistente.App.Tools.App;

public sealed class SettingsTool : ITool
{
    public const string ToolName = "Configurações";
    public string Name => ToolName;
    public string Category => "Aplicativo";
    public string Glyph => "";
    public int Order => 0;
    public FrameworkElement CreateView() => new SettingsView();
}

internal sealed class SettingsView : UserControl
{
    private const string ReleasesUrl = "https://github.com/LeonardoSbrolini/os-assistant/releases";

    private readonly AppSettings _settings;
    private readonly MainWindow _window;
    private readonly TextBlock _updateStatus = Ui.Caption("");
    private readonly Button _checkButton;
    private readonly WrapPanel _swatches = new();

    public SettingsView()
    {
        _window = MainWindow.Current!;
        _settings = _window.Settings;

        _checkButton = Ui.Btn("Buscar atualizações", CheckUpdates, primary: true);

        Content = Ui.Scroll(Ui.Stack(
            Ui.Title("Configurações"),
            AppearanceCard(),
            PositionCard(),
            BehaviorCard(),
            UpdatesCard()));

        BuildSwatches();
    }

    // ---------- Aparência ----------

    private Border AppearanceCard()
    {
        var opacity = SliderRow("Transparência do painel", 60, 100, _settings.PanelOpacity * 100, v => $"{v:0}%",
            v => _settings.PanelOpacity = v / 100);
        var size = SliderRow("Tamanho do painel (em relação à tela)", 30, 50, _settings.PanelSize * 100, v => $"{v:0}%",
            v => _settings.PanelSize = v / 100);

        var animations = Ui.Check("Animações", _settings.Animations);
        animations.Checked += (_, _) => Change(() => _settings.Animations = true);
        animations.Unchecked += (_, _) => Change(() => _settings.Animations = false);

        return Ui.Card("Aparência", "",
            Ui.Field("Cor de destaque", _swatches),
            opacity,
            size,
            animations);
    }

    private void BuildSwatches()
    {
        _swatches.Children.Clear();
        var current = ThemeService.ParseColor(_settings.AccentColor, Colors.DodgerBlue);
        foreach (var (name, hex) in ThemeService.AccentPresets)
        {
            var color = ThemeService.ParseColor(hex, Colors.DodgerBlue);
            var selected = color == current;
            var swatch = new Border
            {
                Width = 30,
                Height = 30,
                CornerRadius = new CornerRadius(15),
                Background = new SolidColorBrush(color),
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(selected ? 3 : 0),
                Margin = new Thickness(0, 0, 8, 4),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = name,
            };
            swatch.MouseLeftButtonUp += (_, _) =>
            {
                Change(() => _settings.AccentColor = hex);
                BuildSwatches();
            };
            swatch.MouseEnter += (_, _) => Anim.Pop(swatch);
            _swatches.Children.Add(swatch);
        }
    }

    // ---------- Posição ----------

    private Border PositionCard()
    {
        var top = Ui.Radio("Topo", "edge", _settings.Edge == DockEdge.Top);
        var right = Ui.Radio("Direita", "edge", _settings.Edge == DockEdge.Right);
        var left = Ui.Radio("Esquerda", "edge", _settings.Edge == DockEdge.Left);
        top.Checked += (_, _) => Change(() => _settings.Edge = DockEdge.Top);
        right.Checked += (_, _) => Change(() => _settings.Edge = DockEdge.Right);
        left.Checked += (_, _) => Change(() => _settings.Edge = DockEdge.Left);

        var position = SliderRow("Posição ao longo da borda", 0, 100, _settings.Position * 100,
            v => v < 35 ? "Início" : v > 65 ? "Fim" : "Centro",
            v => _settings.Position = v / 100);

        return Ui.Card("Posição na tela", "",
            Ui.Field("Borda", Ui.Inline(top, right, left)),
            position);
    }

    // ---------- Comportamento ----------

    private Border BehaviorCard()
    {
        var startup = Ui.Check("Iniciar com o Windows", StartupManager.IsEnabled);
        startup.Checked += (_, _) => StartupManager.SetEnabled(true);
        startup.Unchecked += (_, _) => StartupManager.SetEnabled(false);

        var delay = SliderRow("Tempo para recolher depois que o mouse sai", 150, 2000, _settings.CloseDelayMs,
            v => $"{v / 1000:0.0} s",
            v => _settings.CloseDelayMs = (int)v);

        return Ui.Card("Comportamento", "", startup, delay);
    }

    // ---------- Atualizações ----------

    private Border UpdatesCard()
    {
        var version = new TextBlock
        {
            Text = $"Versão {UpdateService.CurrentVersionText}",
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
        };

        var auto = Ui.Check("Buscar atualizações automaticamente (ao abrir e a cada 6 horas)", _settings.AutoUpdate);
        auto.Checked += (_, _) => Change(() => _settings.AutoUpdate = true);
        auto.Unchecked += (_, _) => Change(() => _settings.AutoUpdate = false);

        var notes = Ui.Btn("Ver novidades", () =>
            Process.Start(new ProcessStartInfo(ReleasesUrl) { UseShellExecute = true }));

        if (UpdateService.Instance.LastCheck is { } last)
            _updateStatus.Text = $"Última verificação: {last:dd/MM HH:mm}";

        return Ui.Card("Atualizações", "",
            version,
            Ui.Inline(_checkButton, notes),
            _updateStatus,
            auto);
    }

    private async void CheckUpdates()
    {
        _checkButton.IsEnabled = false;
        _updateStatus.Text = "Procurando atualizações…";
        try
        {
            _updateStatus.Text = await UpdateService.Instance.CheckAsync(manual: false);
        }
        finally
        {
            _checkButton.IsEnabled = true;
            Anim.Pop(_updateStatus);
        }
    }

    // ---------- Auxiliares ----------

    /// <summary>Salva e aplica na hora.</summary>
    private void Change(Action update)
    {
        update();
        _settings.Save();
        _window.ApplySettings();
    }

    private FrameworkElement SliderRow(string label, double min, double max, double value,
        Func<double, string> format, Action<double> apply)
    {
        var valueText = new TextBlock { FontWeight = FontWeights.SemiBold, MinWidth = 54, TextAlignment = TextAlignment.Right };
        var slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            Value = Math.Clamp(value, min, max),
            VerticalAlignment = VerticalAlignment.Center,
        };
        valueText.Text = format(slider.Value);

        // Aplica ao soltar (ou ao clicar/teclar), para não reposicionar a janela a cada pixel.
        slider.ValueChanged += (_, e) => valueText.Text = format(e.NewValue);
        slider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((_, _) => Change(() => apply(slider.Value))));
        slider.PreviewMouseLeftButtonUp += (_, _) => Change(() => apply(slider.Value));
        slider.KeyUp += (_, _) => Change(() => apply(slider.Value));

        var row = new DockPanel();
        DockPanel.SetDock(valueText, Dock.Right);
        row.Children.Add(valueText);
        row.Children.Add(slider);
        return Ui.Field(label, row);
    }
}
