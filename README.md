# Assistente

Widget flutuante para Windows: fica como uma alça fina no topo da tela (centralizada) e, ao passar o mouse, abre um painel com ferramentas úteis do dia a dia.

## Por que C# + .NET 8 + WPF

- **Nativo do Windows**: acesso direto a CPU, memória, discos e processos (APIs do Windows) sem bibliotecas extras.
- **Leve**: abre rápido e usa pouca memória, diferente de Electron (que embute um navegador inteiro).
- **Janela sempre por cima, transparente e fora do Alt+Tab** são recursos simples no WPF.
- **Instalação sem .NET**: o build gera um `.exe` único que já inclui o runtime.

## Funcionalidades (MVP)

| Categoria | Ferramenta | O que faz |
|---|---|---|
| Sistema | Visão geral | Nome do PC, Windows, processador, uso de CPU e RAM ao vivo, discos e volumes com espaço livre, tempo ligado |
| Sistema | Desempenho | Gráficos do último minuto (estilo Gerenciador de Tarefas): CPU total e por núcleo, memória, disco e rede |
| Sistema | Hardware | Detalhes estilo CPU-Z: processador (cache, clock, soquete), placa-mãe e BIOS, cada pente de memória, GPU, discos (SSD/HDD, NVMe/SATA), sistema |
| Sistema | Processos | Lista estilo Gerenciador de Tarefas (memória e CPU por processo), filtro, finalizar processo, abrir local do arquivo |
| Calculadoras | Calculadora | Expressões com `( ) ^ %`, teclado na tela, histórico |
| Calculadoras | Juros compostos | Valor inicial + aporte mensal, taxa ao mês/ano, prazo em meses/anos, tabela mês a mês |
| Calculadoras | Regra de três | Direta e inversa |
| Calculadoras | Porcentagem | X% de Y, quantos % é, variação, aumento/desconto |
| Geradores | CPF, CNPJ e RG | Gera em lote (com ou sem pontuação, CPF por estado) e valida, inclusive o CNPJ alfanumérico |
| Utilitários | Histórico de cópias | Guarda tudo que é copiado (Ctrl+C) enquanto o app está aberto, com busca; clique duplo copia de novo. Só em memória, ignora cópias marcadas por gerenciadores de senha |
| Tempo | Timer | Contagem regressiva com atalhos (1 a 60 min); ao terminar toca som, mostra notificação e abre o painel |
| Tempo | Cronômetro | Iniciar/pausar, voltas, copiar voltas |
| Utilitários | Gerador de senhas | Gerador criptográfico, escolha de caracteres |
| Utilitários | UUID / GUID | Em lote, com opções de formato |
| Chat IA | Conversa | Chat com IA gratuita (Pollinations, sem cadastro) ou com a sua chave (Groq, OpenRouter, OpenAI…) |
| Música | Tocando agora | Controla YouTube Music, Spotify e outros players pelo controle de mídia do Windows: capa, título, progresso, tocar/pausar, anterior/próxima, volume |

As ferramentas ficam agrupadas na barra lateral (Sistema, Chat IA, Música, Ferramentas, Calculadoras, Tempo) e cada grupo tem sub-abas.

**Configurações** (botão ⚙ ou ícone da bandeja) abrem numa janela própria: iniciar com o Windows, temas, cor de destaque, transparência do painel e das janelas, tamanho, animações, **10 avatares originais** para a alça, posição, serviço de IA e atualizações.

Também: ícone na bandeja (abrir, borda topo/direita/esquerda, posição na borda, iniciar com o Windows, sair), botão de fixar o painel aberto, e a última ferramenta usada é lembrada.

> Os documentos gerados servem apenas para testes de software.

## Como rodar

Pré-requisito: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (`winget install Microsoft.DotNet.SDK.8`).

```powershell
dotnet run --project src\Assistente.App          # abre o app
dotnet test tests\Assistente.Core.Tests          # testes da lógica
```

Ou abra `Assistente.sln` no Visual Studio 2022 / Rider.

## Como gerar o instalador

```powershell
winget install JRSoftware.InnoSetup   # uma vez
.\build.ps1 -Version 0.1.0
```

Saída:
- `artifacts\publish\Assistente.exe`: executável único (dá para usar sem instalar)
- `artifacts\installer\Assistente-Setup-0.1.0.exe`: instalador em português, sem pedir administrador, com opção de iniciar com o Windows

Quando o projeto estiver no GitHub, o workflow `.github/workflows/build.yml` gera o instalador a cada push (e em tags `v*`).

## Atualização automática

O app consulta a última *release* do repositório no GitHub ao abrir e a cada 6 horas. Se houver versão maior, baixa o novo `.exe`, troca o arquivo e reabre sozinho (só quando o painel está recolhido). Também dá para forçar pelo menu da bandeja → *Verificar atualizações*.

Para publicar uma versão nova, aumente a `<Version>` em `src/Assistente.App/Assistente.App.csproj` e faça push em `main`. O workflow compila as duas versões e cria a release com `Assistente.exe` (completo) e `Assistente-leve.exe` (precisa do .NET 8 Desktop Runtime). Cada app baixa o mesmo tipo que está usando.

## Estrutura

```
src/
  Assistente.Core/          Lógica pura, sem Windows (testável)
    Documents/              CPF, CNPJ, RG
    Calculations/           Juros, regra de três, porcentagem, avaliador de expressões
    Text/                   Números pt-BR, tamanhos em bytes, senhas
  Assistente.App/           App WPF
    MainWindow.xaml(.cs)    Widget: alça na borda, expandir/recolher, lista de ferramentas
    Theme.xaml              Tema escuro (cores e estilos)
    Services/               Leitura do sistema, configurações, bandeja, iniciar com Windows
    Tools/                  Uma pasta por categoria, um arquivo por ferramenta
      ITool.cs              Contrato de ferramenta
      ToolRegistry.cs       Descobre as ferramentas automaticamente
      Ui.cs                 Construtores de UI reaproveitáveis
tests/Assistente.Core.Tests/
installer/Assistente.iss    Script do Inno Setup
build.ps1                   Testes + publish + instalador
```

## Como adicionar uma ferramenta

Crie um arquivo em `src/Assistente.App/Tools/<Categoria>/` com uma classe que implemente `ITool`. Ela aparece sozinha na lista:

```csharp
public sealed class MinhaFerramenta : ITool
{
    public string Name => "Minha ferramenta";
    public string Category => "Utilitários";
    public string Glyph => ""; // ícone Segoe Fluent Icons
    public FrameworkElement CreateView() => new UserControl
    {
        Content = Ui.Stack(Ui.Title("Olá"), Ui.Caption("Funciona!"))
    };
}
```

Se tiver lógica de cálculo, coloque em `Assistente.Core` e escreva um teste.

## Próximas funcionalidades sugeridas

**Sistema**
- Rede: IP local/público, velocidade de download/upload ao vivo, ping
- Bateria, temperatura e GPU (via LibreHardwareMonitor)
- Gráfico de histórico de CPU/RAM dos últimos minutos
- Programas que iniciam com o Windows
- Limpeza de arquivos temporários

**Calculadoras**
- Financiamento (Price e SAC) com tabela de parcelas
- Conversor de unidades (comprimento, peso, dados, temperatura)
- Datas: diferença entre datas, dias úteis, somar dias
- Salário líquido (INSS/IRRF), férias e 13º
- IMC, conversor de moedas (cotação online)

**Geradores**
- CNH, título de eleitor, PIS/NIS, RENAVAM, cartão de crédito de teste (Luhn)
- Pessoa fictícia completa (nome, endereço, CEP, telefone) e exportação para CSV/JSON
- Lorem ipsum

**Utilitários**
- Histórico de cópias: imagens, itens fixados e salvar entre sessões (opcional)
- Conversores: Base64, JSON (formatar/validar), URL encode, hash MD5/SHA
- Contador de caracteres e conversão de maiúsculas/minúsculas
- Seletor de cor da tela (conta-gotas)
- Notas rápidas, lembretes e Pomodoro
- Consulta de CEP

**App**
- Atalho de teclado global para abrir o painel
- Arrastar a alça para mudar a posição; tema claro; ferramentas favoritas
- Atualização automática
