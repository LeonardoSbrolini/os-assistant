using System.Windows;

namespace Assistente.App.Tools;

/// <summary>
/// Contrato de uma ferramenta do painel.
/// Para adicionar uma ferramenta nova basta criar uma classe pública que implemente
/// esta interface (com construtor sem parâmetros): ela é descoberta automaticamente.
/// </summary>
public interface ITool
{
    /// <summary>Nome exibido na lista lateral.</summary>
    string Name { get; }

    /// <summary>Grupo da lista lateral (ex.: "Sistema", "Calculadoras").</summary>
    string Category { get; }

    /// <summary>Ícone: caractere da fonte Segoe Fluent Icons / MDL2 Assets.</summary>
    string Glyph { get; }

    /// <summary>Ordem dentro da categoria (menor aparece primeiro).</summary>
    int Order => 100;

    /// <summary>Cria a tela da ferramenta. É chamada uma vez; a tela é reaproveitada.</summary>
    FrameworkElement CreateView();
}
