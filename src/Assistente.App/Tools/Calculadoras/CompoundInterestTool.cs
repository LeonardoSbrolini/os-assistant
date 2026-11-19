using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Assistente.Core.Calculations;

namespace Assistente.App.Tools.Calculadoras;

public sealed class CompoundInterestTool : ITool
{
    public string Name => "Juros compostos";
    public string Category => "Calculadoras";
    public string Glyph => "";
    public int Order => 1;
    public FrameworkElement CreateView() => new CompoundInterestView();
}

internal sealed class CompoundInterestView : UserControl
{
    private readonly TextBox _initial = Ui.Input("1.000,00");
    private readonly TextBox _monthly = Ui.Input("100,00");
    private readonly TextBox _rate = Ui.Input("1");
    private readonly TextBox _term = Ui.Input("12");
    private readonly RadioButton _rateMonthly = Ui.Radio("ao mês", "rate", true);
    private readonly RadioButton _rateYearly = Ui.Radio("ao ano", "rate");
    private readonly RadioButton _termMonths = Ui.Radio("meses", "term", true);
    private readonly RadioButton _termYears = Ui.Radio("anos", "term");
    private readonly TextBlock _error = Ui.Error();
    private readonly TextBlock _final = Ui.Big();
    private readonly TextBlock _details = Ui.Caption("");
    private readonly DataGrid _table = new() { MaxHeight = 260 };

    public CompoundInterestView()
    {
        _table.Columns.Add(Column("Mês", nameof(CompoundRow.Month), null));
        _table.Columns.Add(Column("Investido", nameof(CompoundRow.Invested), "C2"));
        _table.Columns.Add(Column("Juros", nameof(CompoundRow.Interest), "C2"));
        _table.Columns.Add(Column("Saldo", nameof(CompoundRow.Balance), "C2"));

        Ui.OnEnter(Calculate, _initial, _monthly, _rate, _term);

        Content = Ui.Scroll(Ui.Stack(
            Ui.Title("Juros compostos"),
            Ui.Caption("Simula aplicação com valor inicial e aportes mensais (aporte no fim de cada mês)."),
            Ui.Row(
                Ui.Field("Valor inicial (R$)", _initial),
                Ui.Field("Aporte mensal (R$)", _monthly)),
            Ui.Row(
                Ui.Field("Taxa de juros (%)", Ui.Stack(_rate, Ui.Inline(_rateMonthly, _rateYearly))),
                Ui.Field("Prazo", Ui.Stack(_term, Ui.Inline(_termMonths, _termYears)))),
            Ui.Btn("Calcular", Calculate, primary: true),
            _error,
            _final,
            _details,
            _table));

        Calculate();
    }

    private static DataGridTextColumn Column(string header, string path, string? format) => new()
    {
        Header = header,
        Width = new DataGridLength(1, DataGridLengthUnitType.Star),
        Binding = new Binding(path) { StringFormat = format, ConverterCulture = Ui.PtBr },
    };

    private void Calculate() => Ui.Safe(_error, () =>
    {
        var initial = Ui.ReadNumber(_initial, "Valor inicial");
        var monthly = Ui.ReadNumber(_monthly, "Aporte mensal");
        var rate = Ui.ReadNumber(_rate, "Taxa de juros");
        var term = Ui.ReadNumber(_term, "Prazo");
        var months = (int)Math.Round(_termYears.IsChecked == true ? term * 12 : term);
        var period = _rateYearly.IsChecked == true ? RatePeriod.Yearly : RatePeriod.Monthly;

        var result = Finance.CompoundInterest(initial, monthly, rate, period, months);

        _final.Text = Ui.Money(result.FinalBalance);
        _details.Text =
            $"Total investido: {Ui.Money(result.TotalInvested)}\n" +
            $"Total em juros: {Ui.Money(result.TotalInterest)}\n" +
            $"Taxa mensal equivalente: {Ui.Number(result.MonthlyRate * 100, 4)}% · " +
            $"anual: {Ui.Number((Math.Pow(1 + result.MonthlyRate, 12) - 1) * 100, 4)}%";
        _table.ItemsSource = result.Rows.ToList();
    });
}
