using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Assistente.App.Services;

namespace Assistente.App.Tools.Sistema;

public sealed class HardwareTool : ITool
{
    public string Name => "Hardware";
    public string Category => "Sistema";
    public string Glyph => "";
    public int Order => 3;
    public FrameworkElement CreateView() => new HardwareView();
}

/// <summary>Detalhes do hardware no estilo CPU-Z.</summary>
internal sealed class HardwareView : UserControl
{
    private readonly StackPanel _sections = new();
    private readonly TextBlock _status = Ui.Caption("Lendo informações do hardware…");
    private string _text = "";
    private bool _loaded;

    public HardwareView()
    {
        var copy = Ui.Btn("Copiar tudo", () => Ui.Copy(_text));
        var reload = Ui.Btn("Atualizar", () => _ = LoadAsync());

        Content = Ui.Scroll(Ui.Stack(
            Ui.Title("Hardware"),
            Ui.Inline(copy, reload),
            _status,
            _sections));

        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible && !_loaded) _ = LoadAsync();
        };
    }

    private async Task LoadAsync()
    {
        _loaded = true;
        _status.Text = "Lendo informações do hardware…";
        _status.Visibility = Visibility.Visible;
        _sections.Children.Clear();

        try
        {
            var sections = await Task.Run(HardwareInfo.Read);
            var text = new StringBuilder();
            var i = 0;
            foreach (var section in sections)
            {
                var card = Ui.Card(section.Title, section.Glyph, Ui.KeyValues(section.Items.ToArray()));
                _sections.Children.Add(card);
                Anim.FadeUp(card, i++ * 60);

                text.AppendLine($"== {section.Title} ==");
                foreach (var (key, value) in section.Items) text.AppendLine($"{key.Trim()}: {value}");
                text.AppendLine();
            }
            _text = text.ToString();
            _status.Visibility = sections.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
            if (sections.Count == 0) _status.Text = "Não foi possível ler as informações (WMI indisponível).";
        }
        catch (Exception ex)
        {
            _status.Text = $"Erro ao ler o hardware: {ex.Message}";
        }
    }
}
