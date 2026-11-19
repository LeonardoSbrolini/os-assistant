using System.Collections.Generic;
using System.Linq;

namespace Assistente.App.Services;

/// <summary>
/// Textos que aparecem na alça recolhida (ex.: tempo do timer e do cronômetro).
/// Se houver mais de um, mostra o de maior prioridade (menor número).
/// </summary>
public static class HandleStatus
{
    private static readonly Dictionary<string, (int Priority, string Glyph, string Text)> Items = new();

    public static void Set(string source, int priority, string glyph, string text)
    {
        Items[source] = (priority, glyph, text);
        Apply();
    }

    public static void Clear(string source)
    {
        if (Items.Remove(source)) Apply();
    }

    private static void Apply()
    {
        if (Items.Count == 0)
        {
            MainWindow.Current?.SetHandleStatus(null, null);
            return;
        }
        var top = Items.Values.OrderBy(i => i.Priority).First();
        MainWindow.Current?.SetHandleStatus(top.Glyph, top.Text);
    }
}
