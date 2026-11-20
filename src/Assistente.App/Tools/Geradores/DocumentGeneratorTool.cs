using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Assistente.Core.Documents;

namespace Assistente.App.Tools.Geradores;

public sealed class DocumentGeneratorTool : ITool
{
    public string Name => "CPF, CNPJ e RG";
    public string Category => "Ferramentas";
    public string Glyph => "";
    public int Order => 0;
    public FrameworkElement CreateView() => new DocumentGeneratorView();
}

internal sealed class DocumentGeneratorView : UserControl
{
    private readonly RadioButton _cpf = Ui.Radio("CPF", "doc", true);
    private readonly RadioButton _cnpj = Ui.Radio("CNPJ", "doc");
    private readonly RadioButton _rg = Ui.Radio("RG (SP)", "doc");
    private readonly CheckBox _formatted = Ui.Check("Com pontuação", true);
    private readonly ComboBox _uf = new();
    private readonly TextBox _quantity = Ui.Input("1");
    private readonly TextBox _output = Ui.Output(90);
    private readonly TextBox _validateInput = Ui.Input();
    private readonly TextBlock _validateResult = new() { FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _error = Ui.Error();

    public DocumentGeneratorView()
    {
        _uf.Items.Add("Qualquer UF");
        foreach (var uf in Cpf.RegionDigitByUf.Keys.OrderBy(k => k)) _uf.Items.Add(uf);
        _uf.SelectedIndex = 0;
        _uf.ToolTip = "O 9º dígito do CPF indica a região fiscal";

        _cpf.Checked += (_, _) => _uf.IsEnabled = true;
        _cnpj.Checked += (_, _) => _uf.IsEnabled = false;
        _rg.Checked += (_, _) => _uf.IsEnabled = false;

        Ui.OnEnter(GenerateAndCopy, _quantity);
        Ui.OnEnter(Validate, _validateInput);
        _validateInput.TextChanged += (_, _) => Validate();

        Content = Ui.Scroll(Ui.Stack(
            Ui.Title("Gerador de documentos"),
            Ui.Caption("Números com dígitos verificadores válidos, para testes de software. Não pertencem a pessoas reais de forma intencional e não devem ser usados para fraude."),
            Ui.Inline(_cpf, _cnpj, _rg),
            Ui.Row(
                Ui.Field("Estado (só CPF)", _uf),
                Ui.Field("Quantidade", _quantity)),
            _formatted,
            Ui.Inline(
                Ui.Btn("Gerar", GenerateAndCopy, primary: true),
                Ui.Btn("Copiar", () => Ui.Copy(_output.Text))),
            _error,
            _output,
            Ui.Section("Validar"),
            Ui.Caption("Cole um CPF, CNPJ (inclusive alfanumérico) ou RG de SP."),
            _validateInput,
            _validateResult));

        Generate();
    }

    private void Generate() => Ui.Safe(_error, () =>
    {
        if (!int.TryParse(_quantity.Text.Trim(), out var count) || count < 1 || count > 1000)
            throw new FormatException("A quantidade deve ser entre 1 e 1000.");

        var formatted = _formatted.IsChecked == true;
        var uf = _uf.SelectedIndex > 0 ? _uf.SelectedItem as string : null;

        Func<string> next =
            _cnpj.IsChecked == true ? () => Cnpj.Generate(formatted) :
            _rg.IsChecked == true ? () => Rg.Generate(formatted) :
            () => Cpf.Generate(formatted, uf);

        _output.Text = string.Join(Environment.NewLine, Enumerable.Range(0, count).Select(_ => next()));
    });

    /// <summary>Gera e, se for um único número, já copia para a área de transferência.</summary>
    private void GenerateAndCopy()
    {
        Generate();
        if (_error.Text.Length == 0 && Ui.Lines(_output.Text).Length == 1) Ui.Copy(_output.Text);
    }

    private void Validate()
    {
        var text = _validateInput.Text.Trim();
        if (text.Length == 0)
        {
            _validateResult.Text = "";
            return;
        }

        var normalized = new string(text.Where(char.IsLetterOrDigit).ToArray());
        (string Name, bool Valid)? result = normalized.Length switch
        {
            11 when normalized.All(char.IsDigit) => ("CPF", Cpf.IsValid(text)),
            14 => ("CNPJ", Cnpj.IsValid(text)),
            9 => ("RG (SP)", Rg.IsValid(text)),
            _ => null,
        };

        if (result is not { } r)
        {
            _validateResult.Text = "Formato não reconhecido (CPF tem 11 dígitos, CNPJ 14 e RG 9).";
            _validateResult.Foreground = Ui.Res("MutedBrush");
            return;
        }

        _validateResult.Text = r.Valid ? $"✔ {r.Name} válido" : $"✘ {r.Name} inválido";
        _validateResult.Foreground = Ui.Res(r.Valid ? "SuccessBrush" : "ErrorBrush");
    }
}
