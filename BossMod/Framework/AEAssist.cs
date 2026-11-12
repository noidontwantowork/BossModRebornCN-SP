using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
namespace BossMod;

// IPC bridge to the CN-only AEAssist (AEAssistV3) plugin: exposes the positional it currently wants
// and lets boss modules ask it to enter a special state for a limited time.
public sealed class AEAssistModule : IDisposable
{
    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly ICallGateSubscriber<byte> _getDesiredPositional;
    private readonly ICallGateSubscriber<byte, object> _desiredPositionalChanged;
    private readonly ICallGateSubscriber<byte, float, object> _triggerSpecialStateWithDuration;
    private const string aeassist = "AEAssistV3";

    // current value of AEAssist's desired positional, refreshed on every read so a missed event cannot desync us
    private Positional _desiredPositional;

    public Positional DesiredPositional
    {
        get
        {
            var pos = GetDesiredPositional();
            if (pos != _desiredPositional)
            {
                _desiredPositional = pos;
                DesiredPositionalChanged?.Invoke(pos);
            }
            return _desiredPositional;
        }
    }

    public event Action<Positional>? DesiredPositionalChanged;

    public AEAssistModule(IDalamudPluginInterface pluginInterface)
    {
        _pluginInterface = pluginInterface;
        _getDesiredPositional = pluginInterface.GetIpcSubscriber<byte>("AEAssist.GetDesiredPositional");
        _desiredPositionalChanged = pluginInterface.GetIpcSubscriber<byte, object>("AEAssist.ActionUpdater.DesiredPositionalChanged");
        _triggerSpecialStateWithDuration = pluginInterface.GetIpcSubscriber<byte, float, object>("AEAssist.TriggerSpecialStateWithDuration");
        try
        {
            _desiredPositionalChanged.Subscribe(OnDesiredPositionalChanged);
        }
        catch
        {
            // AEAssist not installed/loaded yet - ignore, we'll still be able to poll GetDesiredPositional() later
        }
        _desiredPositional = GetDesiredPositional();
    }

    public void Dispose()
    {
        try
        {
            _desiredPositionalChanged.Unsubscribe(OnDesiredPositionalChanged);
        }
        catch
        {
            // ignore
        }
    }

    public bool IsInstalled
    {
        get
        {
            foreach (var x in _pluginInterface.InstalledPlugins)
            {
                if (x.IsLoaded && x.InternalName.Equals(aeassist, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }
    }

    // polls AEAssist's current desired positional (0=None, 1=Rear, 2=Flank, 3=Front); returns Any if AEAssist is not installed/loaded
    public Positional GetDesiredPositional()
    {
        try
        {
            return MapPositional(_getDesiredPositional.InvokeFunc());
        }
        catch
        {
            return Positional.Any;
        }
    }

    public void TriggerSpecialStateWithDuration(SpecialCommandType specialCommand, float duration)
    {
        try
        {
            if (_triggerSpecialStateWithDuration.HasAction)
            {
                _triggerSpecialStateWithDuration.InvokeAction((byte)specialCommand, duration);
            }
        }
        catch
        {
            // AEAssist not installed/loaded
        }
    }

    private void OnDesiredPositionalChanged(byte value)
    {
        var pos = MapPositional(value);
        if (pos != _desiredPositional)
        {
            _desiredPositional = pos;
            DesiredPositionalChanged?.Invoke(pos);
        }
    }

    private static Positional MapPositional(byte value) => value switch
    {
        1 => Positional.Rear,
        2 => Positional.Flank,
        3 => Positional.Front,
        _ => Positional.Any,
    };

    // mirrors AEAssistV3's own SpecialCommandType ordering, sent raw as a byte over IPC
    public enum SpecialCommandType : byte
    {
        EndSpecial,
        HealArea,
        HealSingle,
        DefenseArea,
        DefenseSingle,
        DispelStancePositional,
        RaiseShirk,
        MoveForward,
        MoveBack,
        AntiKnockback,
        Burst,
        Speed,
        LimitBreak,
        NoCasting,
    }
}
