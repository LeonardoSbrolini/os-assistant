using System;
using System.Collections.Generic;
using System.Linq;

namespace Assistente.App.Tools;

/// <summary>Descobre por reflexão todas as classes que implementam <see cref="ITool"/>.</summary>
public static class ToolRegistry
{
    /// <summary>Ordem das categorias na lista lateral. Categorias novas vão para o fim.</summary>
    private static readonly string[] CategoryOrder = { "Sistema", "Utilitários", "Tempo", "Calculadoras", "Geradores", "Aplicativo" };

    private static readonly Lazy<IReadOnlyList<ITool>> LazyTools = new(Discover);

    public static IReadOnlyList<ITool> Tools => LazyTools.Value;

    private static IReadOnlyList<ITool> Discover() =>
        typeof(ToolRegistry).Assembly.GetTypes()
            .Where(t => typeof(ITool).IsAssignableFrom(t) && t is { IsClass: true, IsAbstract: false }
                        && t.GetConstructor(Type.EmptyTypes) is not null)
            .Select(t => (ITool)Activator.CreateInstance(t)!)
            .OrderBy(t => CategoryRank(t.Category))
            .ThenBy(t => t.Category)
            .ThenBy(t => t.Order)
            .ThenBy(t => t.Name)
            .ToList();

    private static int CategoryRank(string category)
    {
        var i = Array.IndexOf(CategoryOrder, category);
        return i < 0 ? int.MaxValue : i;
    }
}
