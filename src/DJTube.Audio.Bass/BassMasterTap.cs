using DJTube.Core.Abstractions;
using ManagedBass;
using ManagedBass.Mix;

namespace DJTube.Audio.Bass;

using Bass = ManagedBass.Bass;

/// <summary>
/// Copia del master: un splitter "esclavo" del mezclador master. Solo recibe lo que la salida ya ha
/// pedido (no adelanta ni frena nada) y se lee desde el hilo del grabador. Si el motor recrea el
/// mezclador (otra salida u otro formato), se vuelve a enganchar al nuevo.
/// </summary>
internal sealed class BassMasterTap : IAudioTap
{
    private readonly Lock _sync = new();
    private readonly Action<BassMasterTap> _onDispose;
    private int _split;
    private int _rate;
    private int _channels;
    private bool _disposed;

    public BassMasterTap(Action<BassMasterTap> onDispose)
    {
        _onDispose = onDispose;
    }

    /// <summary>Engancha la copia a un mezclador (0 = ninguno).</summary>
    internal void Attach(int mixer, int rate, int channels)
    {
        lock (_sync)
        {
            FreeSplitNoLock();
            if (_disposed || mixer == 0)
                return;
            _split = BassMix.CreateSplitStream(mixer, BassFlags.Decode | BassFlags.SplitSlave, null);
            (_rate, _channels) = (rate, channels);
        }
    }

    /// <summary>Suelta el mezclador antes de que el motor lo libere.</summary>
    internal void Detach()
    {
        lock (_sync)
            FreeSplitNoLock();
    }

    public TapRead Read(float[] buffer)
    {
        lock (_sync)
        {
            if (_split == 0)
                return default;
            int samples = buffer.Length - buffer.Length % Math.Max(1, _channels);
            int bytes = Bass.ChannelGetData(_split, buffer, samples * sizeof(float) | (int)DataFlags.Float);
            return bytes > 0 ? new TapRead(bytes / sizeof(float), _rate, _channels) : default;
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
                return;
            _disposed = true;
            FreeSplitNoLock();
        }
        _onDispose(this);
    }

    private void FreeSplitNoLock()
    {
        if (_split != 0)
            Bass.StreamFree(_split);
        _split = 0;
    }
}
