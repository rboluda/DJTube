using ManagedBass;

namespace DJTube.Audio.Bass;

public sealed class BassException : Exception
{
    public BassException(string message)
        : this(message, ManagedBass.Bass.LastError)
    {
    }

    public BassException(string message, Errors error)
        : base($"{message} (BASS: {error})")
    {
        Error = error;
    }

    public Errors Error { get; }
}
