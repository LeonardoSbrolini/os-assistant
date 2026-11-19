using System.Windows;
using System.Windows.Controls;
using Assistente.Core.Calculations;

namespace Assistente.App.Tools.Calculadoras;

public sealed class RuleOfThreeTool : ITool
{
    public string Name => "Regra de três";
    public string Category => "Calculadoras";
    public string Glyph => "";
    public int Order => 2;
    public FrameworkElement CreateView() => new RuleOfThreeView();
}

internal sealed class RuleOfThreeView : UserControl
{
    private readonly TextBox _a = Ui.Input("2");
    private readonly TextBox _b = Ui.Input("10");
    private readonly TextBox _c = Ui.Input("6");
    private readonly RadioButton _direct = Ui.Radio("Diretamente proporcional", "r3", true);
    private readonly RadioButton _inverse = Ui.Radio("Inversamente proporcional", "r3");
    private readonly TextBlock _result = Ui.Big();
    private readonly TextBlock _formula = Ui.Caption("");
    private readonly TextBlock _error = Ui.Error();

    public RuleOfThreeView()
    {
        foreach (var box in new[] { _a, _b, _c }) box.TextChanged += (_, _) => Calculate();
        _direct.Checked += (_, _) => Calculate();
        _inverse.Checked += (_, _) => Calculate();

        Content = Ui.Scroll(Ui.Stack(
            Ui.Title("Regra de três"),
            Ui.Caption("A está para B, assim como C está para X."),
            Ui.Inline(_direct, _inverse),
            Ui.Row(Ui.Field("A", _a), Ui.Field("B", _b)),
            Ui.Row(Ui.Field("C", _c), Ui.Field("X (resultado)", _result)),
            _formula,
            _error,
            Ui.Caption("Exemplo direto: 2 kg custam R$ 10, quanto custam 6 kg? → X = 30.\n" +
                       "Exemplo inverso: 4 pedreiros fazem em 6 dias, 8 pedreiros fazem em X dias → X = 3.")));

        Calculate();
    }

    private void Calculate()
    {
        Ui.Safe(_error, () =>
        {
            _result.Text = "";
            var a = Ui.ReadNumber(_a, "A");
            var b = Ui.ReadNumber(_b, "B");
            var c = Ui.ReadNumber(_c, "C");
            var inverse = _inverse.IsChecked == true;
            var x = Finance.RuleOfThree(a, b, c, inverse);
            _result.Text = Ui.Number(x, 6);
            _formula.Text = inverse ? "X = A × B ÷ C" : "X = B × C ÷ A";
        });
    }
}
