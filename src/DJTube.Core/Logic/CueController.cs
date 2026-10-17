using DJTube.Core.Abstractions;

namespace DJTube.Core.Logic;

/// <summary>
/// Comportamiento del botón CUE al estilo CDJ:
/// <list type="bullet">
/// <item>Reproduciendo: CUE vuelve al punto de cue y pausa.</item>
/// <item>En pausa fuera del punto de cue: CUE fija el punto de cue en la posición actual.</item>
/// <item>En pausa sobre el punto de cue: mantener CUE reproduce; al soltar vuelve al cue y pausa.</item>
/// <item>Pulsar PLAY mientras se mantiene CUE deja la pista sonando al soltar.</item>
/// </list>
/// </summary>
public sealed class CueController
{
    /// <summary>Distancia máxima (segundos) para considerar que se está sobre el punto de cue.</summary>
    public const double OnCueTolerance = 0.02;

    private readonly IDeckTransport _transport;

    public CueController(IDeckTransport transport)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
    }

    public double CuePoint { get; private set; }

    /// <summary>Se está reproduciendo porque CUE está pulsado.</summary>
    public bool IsPreviewing { get; private set; }

    /// <summary>Vuelve al estado inicial (al cargar una pista nueva).</summary>
    public void Reset(double cuePoint = 0)
    {
        CuePoint = Math.Max(0, cuePoint);
        IsPreviewing = false;
    }

    public void CueDown()
    {
        if (IsPreviewing)
            return;

        if (_transport.IsPlaying)
        {
            _transport.Pause();
            _transport.Seek(CuePoint);
            return;
        }

        if (Math.Abs(_transport.PositionSeconds - CuePoint) > OnCueTolerance)
        {
            CuePoint = Math.Max(0, _transport.PositionSeconds);
            return;
        }

        IsPreviewing = true;
        _transport.Play();
    }

    public void CueUp()
    {
        if (!IsPreviewing)
            return;

        IsPreviewing = false;
        _transport.Pause();
        _transport.Seek(CuePoint);
    }

    public void PlayPause()
    {
        if (IsPreviewing)
        {
            // Sigue sonando: al soltar CUE ya no se vuelve al punto de cue.
            IsPreviewing = false;
            return;
        }

        if (_transport.IsPlaying)
            _transport.Pause();
        else
            _transport.Play();
    }
}
