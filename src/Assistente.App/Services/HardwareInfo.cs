using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Management;
using Assistente.Core.Text;

namespace Assistente.App.Services;

/// <summary>Uma seção de informações (ex.: "Processador") com pares rótulo/valor.</summary>
public sealed record HardwareSection(string Title, string Glyph, IReadOnlyList<(string Key, string Value)> Items);

/// <summary>
/// Detalhes de hardware no estilo CPU-Z, lidos via WMI.
/// É lento (1–3 s), então deve ser chamado fora da thread da interface.
/// </summary>
public static class HardwareInfo
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static IReadOnlyList<HardwareSection> Read()
    {
        var sections = new List<HardwareSection>();
        Try(sections, Processor);
        Try(sections, Motherboard);
        Try(sections, Memory);
        Try(sections, Graphics);
        Try(sections, Disks);
        Try(sections, OsSection);
        return sections;
    }

    private static void Try(List<HardwareSection> sections, Func<HardwareSection?> read)
    {
        try
        {
            if (read() is { Items.Count: > 0 } section) sections.Add(section);
        }
        catch (Exception)
        {
            // Uma classe WMI indisponível não impede as outras.
        }
    }

    // ---------- Seções ----------

    private static HardwareSection? Processor()
    {
        var items = new List<(string, string)>();
        foreach (var cpu in Query("SELECT * FROM Win32_Processor"))
        {
            Add(items, "Nome", Str(cpu, "Name"));
            Add(items, "Fabricante", Str(cpu, "Manufacturer"));
            Add(items, "Identificação", Str(cpu, "Caption"));
            Add(items, "Soquete", Str(cpu, "SocketDesignation"));
            Add(items, "Arquitetura", Architecture(U(cpu, "Architecture")));
            Add(items, "Núcleos físicos", U(cpu, "NumberOfCores")?.ToString());
            Add(items, "Núcleos lógicos (threads)", U(cpu, "NumberOfLogicalProcessors")?.ToString());
            Add(items, "Clock base", Mhz(U(cpu, "MaxClockSpeed")));
            Add(items, "Clock atual", Mhz(U(cpu, "CurrentClockSpeed")));
            Add(items, "Cache L2", Kb(U(cpu, "L2CacheSize")));
            Add(items, "Cache L3", Kb(U(cpu, "L3CacheSize")));
            Add(items, "Virtualização no firmware", YesNo(cpu, "VirtualizationFirmwareEnabled"));
        }
        return new HardwareSection("Processador", "", items);
    }

    private static HardwareSection? Motherboard()
    {
        var items = new List<(string, string)>();
        foreach (var cs in Query("SELECT Manufacturer, Model, SystemType FROM Win32_ComputerSystem"))
        {
            Add(items, "Fabricante do sistema", Str(cs, "Manufacturer"));
            Add(items, "Modelo do sistema", Str(cs, "Model"));
            Add(items, "Tipo", Str(cs, "SystemType"));
        }
        foreach (var board in Query("SELECT Manufacturer, Product, Version FROM Win32_BaseBoard"))
        {
            Add(items, "Placa-mãe", Join(Str(board, "Manufacturer"), Str(board, "Product")));
            Add(items, "Revisão", Str(board, "Version"));
        }
        foreach (var bios in Query("SELECT Manufacturer, SMBIOSBIOSVersion, ReleaseDate FROM Win32_BIOS"))
        {
            Add(items, "BIOS", Join(Str(bios, "Manufacturer"), Str(bios, "SMBIOSBIOSVersion")));
            Add(items, "Data da BIOS", Date(bios, "ReleaseDate"));
        }
        return new HardwareSection("Placa-mãe e BIOS", "", items);
    }

    private static HardwareSection? Memory()
    {
        var items = new List<(string, string)>();
        var modules = Query("SELECT * FROM Win32_PhysicalMemory").ToList();
        ulong total = 0;
        foreach (var m in modules) total += U(m, "Capacity") ?? 0;

        var slots = Query("SELECT MemoryDevices FROM Win32_PhysicalMemoryArray")
            .Sum(a => (long)(U(a, "MemoryDevices") ?? 0));

        Add(items, "Total instalado", ByteSize.Format(total, PtBr));
        Add(items, "Slots usados", slots > 0 ? $"{modules.Count} de {slots}" : modules.Count.ToString());

        var i = 1;
        foreach (var m in modules)
        {
            var type = MemoryType(U(m, "SMBIOSMemoryType"));
            var speed = U(m, "ConfiguredClockSpeed") is { } cfg and > 0 ? cfg : U(m, "Speed");
            var parts = new[]
            {
                ByteSize.Format(U(m, "Capacity") ?? 0, PtBr),
                type,
                speed is > 0 ? $"{speed} MT/s" : null,
                FormFactor(U(m, "FormFactor")),
                Str(m, "Manufacturer"),
                Str(m, "PartNumber"),
            };
            var slot = Str(m, "DeviceLocator") ?? Str(m, "BankLabel") ?? $"Módulo {i}";
            Add(items, slot, string.Join(" · ", parts.Where(p => !string.IsNullOrWhiteSpace(p))));
            i++;
        }
        return new HardwareSection("Memória RAM", "", items);
    }

    private static HardwareSection? Graphics()
    {
        var items = new List<(string, string)>();
        foreach (var gpu in Query("SELECT * FROM Win32_VideoController"))
        {
            var name = Str(gpu, "Name") ?? "Placa de vídeo";
            var ram = U(gpu, "AdapterRAM");
            // AdapterRAM é 32 bits: acima de 4 GB o Windows informa o máximo.
            var vram = ram is null or 0 ? null : ram >= uint.MaxValue - 1 ? "4 GB ou mais" : ByteSize.Format(ram.Value, PtBr);
            var res = U(gpu, "CurrentHorizontalResolution") is { } w and > 0
                ? $"{w} × {U(gpu, "CurrentVerticalResolution")} @ {U(gpu, "CurrentRefreshRate")} Hz"
                : null;
            Add(items, name, string.Join(" · ", new[] { vram, res }.Where(p => p is not null)));
            Add(items, "  Driver", Join(Str(gpu, "DriverVersion"), Date(gpu, "DriverDate")));
        }
        return new HardwareSection("Placa de vídeo", "", items);
    }

    private static HardwareSection? Disks()
    {
        var items = new List<(string, string)>();
        try
        {
            // Mais detalhado (SSD/HDD, NVMe/SATA), disponível no Windows 8+.
            foreach (var d in Query("SELECT FriendlyName, MediaType, BusType, Size FROM MSFT_PhysicalDisk", @"root\Microsoft\Windows\Storage"))
            {
                Add(items, Str(d, "FriendlyName") ?? "Disco",
                    string.Join(" · ", new[] { ByteSize.Format(U(d, "Size") ?? 0, PtBr), DiskMedia(U(d, "MediaType")), DiskBus(U(d, "BusType")) }
                        .Where(p => p is not null)));
            }
        }
        catch (Exception)
        {
            // Usa a consulta clássica abaixo.
        }
        if (items.Count == 0)
        {
            foreach (var d in Query("SELECT Model, Size, InterfaceType, MediaType FROM Win32_DiskDrive"))
                Add(items, Str(d, "Model") ?? "Disco",
                    Join(ByteSize.Format(U(d, "Size") ?? 0, PtBr), Str(d, "InterfaceType")));
        }
        return new HardwareSection("Armazenamento", "", items);
    }

    private static HardwareSection? OsSection()
    {
        var items = new List<(string, string)>();
        foreach (var os in Query("SELECT Caption, Version, BuildNumber, OSArchitecture, InstallDate, LastBootUpTime FROM Win32_OperatingSystem"))
        {
            Add(items, "Sistema", Str(os, "Caption"));
            Add(items, "Versão", Join(Str(os, "Version"), Str(os, "OSArchitecture")));
            Add(items, "Instalado em", Date(os, "InstallDate"));
            Add(items, "Ligado desde", DateTimeText(os, "LastBootUpTime"));
        }
        return new HardwareSection("Sistema operacional", "", items);
    }

    // ---------- Auxiliares ----------

    private static IEnumerable<ManagementBaseObject> Query(string wql, string scope = @"root\CIMV2")
    {
        using var searcher = new ManagementObjectSearcher(scope, wql);
        using var results = searcher.Get();
        foreach (var item in results) yield return item;
    }

    private static object? Prop(ManagementBaseObject o, string name)
    {
        try
        {
            return o[name];
        }
        catch (ManagementException)
        {
            return null;
        }
    }

    private static string? Str(ManagementBaseObject o, string name) =>
        Prop(o, name)?.ToString()?.Trim() is { Length: > 0 } s ? s : null;

    private static ulong? U(ManagementBaseObject o, string name)
    {
        var v = Prop(o, name);
        return v is null ? null : Convert.ToUInt64(v, CultureInfo.InvariantCulture);
    }

    private static string? YesNo(ManagementBaseObject o, string name) =>
        Prop(o, name) is bool b ? (b ? "Sim" : "Não") : null;

    private static string? Date(ManagementBaseObject o, string name) =>
        Str(o, name) is { } s ? ManagementDateTimeConverter.ToDateTime(s).ToString("dd/MM/yyyy", PtBr) : null;

    private static string? DateTimeText(ManagementBaseObject o, string name) =>
        Str(o, name) is { } s ? ManagementDateTimeConverter.ToDateTime(s).ToString("dd/MM/yyyy HH:mm", PtBr) : null;

    private static void Add(List<(string, string)> items, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) items.Add((key, value));
    }

    private static string? Join(params string?[] parts)
    {
        var s = string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        return s.Length == 0 ? null : s;
    }

    private static string? Mhz(ulong? mhz) => mhz is > 0 ? (mhz >= 1000 ? $"{mhz / 1000.0:0.00} GHz" : $"{mhz} MHz") : null;

    private static string? Kb(ulong? kb) => kb is > 0 ? (kb >= 1024 ? $"{kb / 1024.0:0.#} MB" : $"{kb} KB") : null;

    private static string? Architecture(ulong? code) => code switch
    {
        0 => "x86",
        5 => "ARM",
        9 => "x64",
        12 => "ARM64",
        _ => null,
    };

    private static string? MemoryType(ulong? code) => code switch
    {
        20 => "DDR",
        21 => "DDR2",
        24 => "DDR3",
        26 => "DDR4",
        34 => "DDR5",
        35 => "LPDDR5",
        30 => "LPDDR4",
        _ => null,
    };

    private static string? FormFactor(ulong? code) => code switch
    {
        8 => "DIMM",
        12 => "SO-DIMM",
        _ => null,
    };

    private static string? DiskMedia(ulong? code) => code switch
    {
        3 => "HDD",
        4 => "SSD",
        5 => "SCM",
        _ => null,
    };

    private static string? DiskBus(ulong? code) => code switch
    {
        1 => "SCSI",
        3 => "ATA",
        7 => "USB",
        8 => "RAID",
        11 => "SATA",
        12 => "SD",
        17 => "NVMe",
        _ => null,
    };
}
