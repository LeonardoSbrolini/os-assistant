using System;
using System.Windows.Media;

namespace Assistente.App.Tools;

/// <summary>
/// Mola de verdade (como a Dynamic Island do iPhone): leva vários números até um alvo
/// com física de mola, quadro a quadro. Dá para mudar o alvo no meio do caminho e a mola
/// continua dali, com a velocidade que já tinha (abre/fecha sem "trancos").
/// </summary>
public sealed class Spring
{
    private readonly double[] _x;
    private readonly double[] _v;
    private readonly double[] _t;
    private readonly Action<double[]> _apply;
    private double _k = 200;
    private double _c = 20;
    private TimeSpan? _last;
    private bool _hooked;
    private Action? _done;

    public Spring(int count, Action<double[]> apply)
    {
        _x = new double[count];
        _v = new double[count];
        _t = new double[count];
        _apply = apply;
    }

    /// <summary>Ainda está se mexendo.</summary>
    public bool IsRunning => _hooked;

    /// <summary>Valores atuais.</summary>
    public double this[int i] => _x[i];

    /// <summary>Pula direto para os valores, sem animar.</summary>
    public void Snap(params double[] values)
    {
        Unhook();
        _done = null;
        for (var i = 0; i < _x.Length; i++)
        {
            _x[i] = _t[i] = values[i];
            _v[i] = 0;
        }
        _apply(_x);
    }

    /// <summary>
    /// Vai até os alvos. <paramref name="response"/> = tempo (s) de uma oscilação (quanto menor,
    /// mais rápido); <paramref name="damping"/> 1 = sem quicar, menor = quica um pouco.
    /// </summary>
    public void To(double response, double damping, Action? done, params double[] targets)
    {
        for (var i = 0; i < _t.Length; i++) _t[i] = targets[i];
        var w = 2 * Math.PI / Math.Max(0.05, response);
        _k = w * w;
        _c = 2 * damping * w;
        _done = done;
        if (!Anim.Enabled)
        {
            Settle();
            return;
        }
        if (_hooked) return;
        _hooked = true;
        _last = null;
        CompositionTarget.Rendering += OnFrame;
    }

    public void Stop()
    {
        Unhook();
        _done = null;
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        var now = e is RenderingEventArgs r ? r.RenderingTime : TimeSpan.FromTicks(DateTime.Now.Ticks);
        if (_last == now) return; // o evento pode vir mais de uma vez no mesmo quadro
        var dt = _last is { } last ? Math.Clamp((now - last).TotalSeconds, 0, 0.05) : 1 / 60.0;
        _last = now;

        // Passos pequenos deixam a mola estável mesmo com quadros atrasados.
        var steps = Math.Max(1, (int)Math.Ceiling(dt * 240));
        var h = dt / steps;
        for (var s = 0; s < steps; s++)
        {
            for (var i = 0; i < _x.Length; i++)
            {
                var a = -_k * (_x[i] - _t[i]) - _c * _v[i];
                _v[i] += a * h;
                _x[i] += _v[i] * h;
            }
        }

        var settled = true;
        for (var i = 0; i < _x.Length && settled; i++)
            settled = Math.Abs(_x[i] - _t[i]) < 0.35 && Math.Abs(_v[i]) < 4;
        if (settled) Settle();
        else _apply(_x);
    }

    private void Settle()
    {
        Unhook();
        for (var i = 0; i < _x.Length; i++)
        {
            _x[i] = _t[i];
            _v[i] = 0;
        }
        _apply(_x);
        var done = _done;
        _done = null;
        done?.Invoke();
    }

    private void Unhook()
    {
        if (!_hooked) return;
        _hooked = false;
        CompositionTarget.Rendering -= OnFrame;
    }
}
