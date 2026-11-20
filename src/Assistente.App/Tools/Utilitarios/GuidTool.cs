using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Assistente.App.Tools.Utilitarios;

public sealed class GuidTool : ITool
{
    public string Name => "UUID / GUID";
    public string Category => "Ferramentas";
    public string Glyph => "";
    public int Order => 1;
    public FrameworkElement CreateView() => new GuidView();
}

internal sealed class GuidView : UserControl
{
    private readonly TextBox _quantity = Ui.Input("5");
    private readonly CheckBox _upper = Ui.Check("Maiúsculas");
    private readonly CheckBox _braces = Ui.Check("Com chaves { }");
    private readonly CheckBox _noHyphens = Ui.Check("Sem hífens");
    private readonly TextBox _output = Ui.Output(110);
    private readonly TextBlock _error = Ui.Error();

    public GuidView()
    {
        Ui.OnEnter(Generate, _quantity);

        Content = Ui.Scroll(Ui.Stack(
            Ui.Title("UUID / GUID"),
            Ui.Caption("Identificadores aleatórios (versão 4)."),
            Ui.Field("Quantidade", _quantity),
            Ui.Inline(_upper, _braces, _noHyphens),
            Ui.Inline(
                Ui.Btn("Gerar", Generate, primary: true),
                Ui.Btn("Copiar tudo", () => Ui.Copy(_output.Text))),
            _error,
            _output));

        Generate();
    }

    private void Generate() => Ui.Safe(_error, () =>
    {
        if (!int.TryParse(_quantity.Text.Trim(), out var count) || count < 1 || count > 1000)
            throw new FormatException("A quantidade deve ser entre 1 e 1000.");

        var format = _noHyphens.IsChecked == true ? "N" : _braces.IsChecked == true ? "B" : "D";
        _output.Text = string.Join(Environment.NewLine, Enumerable.Range(0, count).Select(_ =>
        {
            var s = Guid.NewGuid().ToString(format);
            return _upper.IsChecked == true ? s.ToUpperInvariant() : s;
        }));
    });
}
