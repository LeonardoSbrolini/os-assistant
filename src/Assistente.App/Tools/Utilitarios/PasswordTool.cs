using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Assistente.Core.Text;

namespace Assistente.App.Tools.Utilitarios;

public sealed class PasswordTool : ITool
{
    public string Name => "Gerador de senhas";
    public string Category => "Ferramentas";
    public string Glyph => "";
    public int Order => 0;
    public FrameworkElement CreateView() => new PasswordView();
}

internal sealed class PasswordView : UserControl
{
    private readonly TextBox _length = Ui.Input("16");
    private readonly TextBox _quantity = Ui.Input("5");
    private readonly CheckBox _lower = Ui.Check("a-z", true);
    private readonly CheckBox _upper = Ui.Check("A-Z", true);
    private readonly CheckBox _digits = Ui.Check("0-9", true);
    private readonly CheckBox _symbols = Ui.Check("Símbolos", true);
    private readonly CheckBox _ambiguous = Ui.Check("Evitar parecidos (l, 1, O, 0)");
    private readonly TextBox _output = Ui.Output(110);
    private readonly TextBlock _error = Ui.Error();

    public PasswordView()
    {
        Ui.OnEnter(Generate, _length, _quantity);

        Content = Ui.Scroll(Ui.Stack(
            Ui.Title("Gerador de senhas"),
            Ui.Caption("Gerado localmente com gerador criptográfico. Nada sai do seu computador."),
            Ui.Row(Ui.Field("Tamanho", _length), Ui.Field("Quantidade", _quantity)),
            Ui.Inline(_lower, _upper, _digits, _symbols),
            _ambiguous,
            Ui.Inline(
                Ui.Btn("Gerar", Generate, primary: true),
                Ui.Btn("Copiar primeira", () => Ui.Copy(Ui.Lines(_output.Text).FirstOrDefault() ?? ""))),
            _error,
            _output));

        Generate();
    }

    private void Generate() => Ui.Safe(_error, () =>
    {
        if (!int.TryParse(_length.Text.Trim(), out var length)) throw new FormatException("Tamanho inválido.");
        if (!int.TryParse(_quantity.Text.Trim(), out var count) || count < 1 || count > 100)
            throw new FormatException("A quantidade deve ser entre 1 e 100.");

        var options = new PasswordOptions(length, _lower.IsChecked == true, _upper.IsChecked == true,
            _digits.IsChecked == true, _symbols.IsChecked == true, _ambiguous.IsChecked == true);

        _output.Text = string.Join(Environment.NewLine,
            Enumerable.Range(0, count).Select(_ => PasswordGenerator.Generate(options)));
    });
}
