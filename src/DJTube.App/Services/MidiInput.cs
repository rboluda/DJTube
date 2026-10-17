using System.Runtime.InteropServices;
using DJTube.Core.Midi;

namespace DJTube.App.Services;

/// <summary>
/// Entradas MIDI de Windows (winmm): abre todas las controladoras conectadas y entrega cada mensaje en el
/// hilo de la interfaz. Sin dependencias: es la API MIDI que trae Windows.
/// </summary>
public sealed class MidiInput : IDisposable
{
    private const int CallbackFunction = 0x30000;
    private const int MimData = 0x3C3;

    private delegate void MidiInProc(IntPtr handle, int message, IntPtr instance, IntPtr param1, IntPtr param2);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MidiInCaps
    {
        public ushort Mid;
        public ushort Pid;
        public uint DriverVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string Name;
        public uint Support;
    }

    [DllImport("winmm.dll")]
    private static extern int midiInGetNumDevs();

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern int midiInGetDevCaps(IntPtr deviceId, ref MidiInCaps caps, int size);

    [DllImport("winmm.dll")]
    private static extern int midiInOpen(out IntPtr handle, int deviceId, MidiInProc callback, IntPtr instance, int flags);

    [DllImport("winmm.dll")]
    private static extern int midiInStart(IntPtr handle);

    [DllImport("winmm.dll")]
    private static extern int midiInStop(IntPtr handle);

    [DllImport("winmm.dll")]
    private static extern int midiInReset(IntPtr handle);

    [DllImport("winmm.dll")]
    private static extern int midiInClose(IntPtr handle);

    private readonly MidiInProc _callback; // se guarda: winmm lo llama desde su hilo
    private readonly List<IntPtr> _open = [];
    private readonly Action<MidiMessage> _onMessage;
    private readonly System.Windows.Threading.Dispatcher _dispatcher;

    public MidiInput(Action<MidiMessage> onMessage)
    {
        _onMessage = onMessage;
        _dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
        _callback = OnMidi;
    }

    /// <summary>Nombres de las controladoras abiertas.</summary>
    public IReadOnlyList<string> Devices { get; private set; } = [];

    /// <summary>Nombres de las entradas MIDI que hay en el sistema.</summary>
    public static IReadOnlyList<string> ListDevices()
    {
        var names = new List<string>();
        int count = midiInGetNumDevs();
        for (int i = 0; i < count; i++)
        {
            var caps = new MidiInCaps();
            if (midiInGetDevCaps(i, ref caps, Marshal.SizeOf<MidiInCaps>()) == 0)
                names.Add(caps.Name);
        }
        return names;
    }

    /// <summary>(Re)abre todas las entradas MIDI.</summary>
    public void OpenAll()
    {
        CloseAll();
        var opened = new List<string>();
        var names = ListDevices();
        for (int i = 0; i < names.Count; i++)
        {
            if (midiInOpen(out IntPtr handle, i, _callback, IntPtr.Zero, CallbackFunction) != 0)
                continue;
            if (midiInStart(handle) != 0)
            {
                midiInClose(handle);
                continue;
            }
            _open.Add(handle);
            opened.Add(names[i]);
        }
        Devices = opened;
    }

    public void CloseAll()
    {
        foreach (var handle in _open)
        {
            midiInStop(handle);
            midiInReset(handle);
            midiInClose(handle);
        }
        _open.Clear();
        Devices = [];
    }

    public void Dispose() => CloseAll();

    private void OnMidi(IntPtr handle, int message, IntPtr instance, IntPtr param1, IntPtr param2)
    {
        if (message != MimData || MidiMessage.Parse((int)param1.ToInt64()) is not { } parsed)
            return;
        _dispatcher.BeginInvoke(() => _onMessage(parsed));
    }
}
