using System;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Assistente.App.Services;

/// <summary>
/// Clima atual para a barra de menus, sem chave: descobre a cidade aproximada pelo IP
/// (ipwho.is) e busca a temperatura no Open-Meteo. Atualiza a cada 30 minutos.
/// </summary>
public sealed class WeatherService
{
    public static WeatherService Instance { get; } = new();

    public double? Temperature { get; private set; }
    public int Code { get; private set; } = -1;
    public string City { get; private set; } = "";
    public event Action? Changed;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private DateTime _lastFetch = DateTime.MinValue;
    private double? _lat, _lon;
    private bool _busy;

    /// <summary>Busca de novo se passou o intervalo (chamar com frequência; é barato).</summary>
    public void RefreshIfStale()
    {
        var interval = Temperature is null ? TimeSpan.FromMinutes(3) : TimeSpan.FromMinutes(30);
        if (_busy || DateTime.Now - _lastFetch < interval) return;
        _busy = true;
        _lastFetch = DateTime.Now;
        Task.Run(async () =>
        {
            try
            {
                if (_lat is null || _lon is null)
                {
                    using var geo = JsonDocument.Parse(await Http.GetStringAsync("https://ipwho.is/?fields=latitude,longitude,city,success"));
                    var r = geo.RootElement;
                    if (r.TryGetProperty("latitude", out var lat) && r.TryGetProperty("longitude", out var lon))
                    {
                        _lat = lat.GetDouble();
                        _lon = lon.GetDouble();
                        City = r.TryGetProperty("city", out var c) ? c.GetString() ?? "" : "";
                    }
                }
                if (_lat is null || _lon is null) return;
                var url = string.Format(CultureInfo.InvariantCulture,
                    "https://api.open-meteo.com/v1/forecast?latitude={0:0.###}&longitude={1:0.###}&current=temperature_2m,weather_code", _lat, _lon);
                using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
                var current = doc.RootElement.GetProperty("current");
                Temperature = current.GetProperty("temperature_2m").GetDouble();
                Code = current.GetProperty("weather_code").GetInt32();
            }
            catch (Exception)
            {
                // Sem internet: tenta de novo depois.
            }
            finally
            {
                _busy = false;
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() => Changed?.Invoke()));
            }
        });
    }

    /// <summary>Símbolo do tempo (códigos WMO do Open-Meteo).</summary>
    public string Symbol => Code switch
    {
        0 or 1 => DateTime.Now.Hour is >= 6 and < 18 ? "☀" : "☾",
        2 => "⛅",
        3 or 45 or 48 => "☁",
        >= 51 and <= 67 or >= 80 and <= 82 => "☂",
        >= 71 and <= 77 or 85 or 86 => "❄",
        >= 95 => "⚡",
        _ => "☁",
    };

    public string Description => Code switch
    {
        0 => "Céu limpo",
        1 => "Quase limpo",
        2 => "Parcialmente nublado",
        3 => "Nublado",
        45 or 48 => "Neblina",
        >= 51 and <= 57 => "Garoa",
        >= 61 and <= 67 => "Chuva",
        >= 71 and <= 77 => "Neve",
        >= 80 and <= 82 => "Pancadas de chuva",
        85 or 86 => "Neve",
        >= 95 => "Tempestade",
        _ => "",
    };
}
