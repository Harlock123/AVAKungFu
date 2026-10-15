using Avalonia.Input;

namespace AVAKungFu.Engine;

/// <summary>
/// Keyboard and gamepad boiled down to the controls: left and right to walk (the last pressed wins),
/// up to jump, down to crouch, and the punch and kick buttons.
/// </summary>
public sealed class InputHandler : IDisposable
{
    private readonly List<Key> _held = [];
    private readonly ControllerManager _pad = new(0);

    public void OnKeyDown(Key key)
    {
        _held.Remove(key);
        _held.Add(key);
    }

    public void OnKeyUp(Key key) => _held.Remove(key);
    public bool Has(Key key) => _held.Contains(key);
    private bool Any(params Key[] keys) => keys.Any(_held.Contains);

    public void Update() => _pad.Update();

    private static readonly Key[] LeftKeys = [Key.Left, Key.A, Key.NumPad4];
    private static readonly Key[] RightKeys = [Key.Right, Key.D, Key.NumPad6];

    public Controls Controls
    {
        get
        {
            int dx = 0;
            for (int i = _held.Count - 1; i >= 0 && dx == 0; i--)
            {
                if (LeftKeys.Contains(_held[i])) dx = -1;
                else if (RightKeys.Contains(_held[i])) dx = 1;
            }
            int dy = Any(Key.Down, Key.S, Key.NumPad2) ? 1 : Any(Key.Up, Key.W, Key.NumPad8) ? -1 : 0;
            if (dx == 0) dx = _pad.DPadLeft || _pad.LeftStickX < -0.4f ? -1 : _pad.DPadRight || _pad.LeftStickX > 0.4f ? 1 : 0;
            if (dy == 0) dy = _pad.DPadDown || _pad.LeftStickY > 0.5f ? 1 : _pad.DPadUp || _pad.LeftStickY < -0.5f ? -1 : 0;
            bool punch = Any(Key.Z, Key.J, Key.LeftCtrl) || _pad.ButtonX || _pad.ButtonY;
            bool kick = Any(Key.X, Key.K, Key.Space, Key.LeftAlt) || _pad.ButtonA || _pad.ButtonB;
            return new Controls(dx, dy, punch, kick);
        }
    }

    public bool Start => Any(Key.Enter) || _pad.ButtonStart;

    public bool PadUp => _pad.DPadUp || _pad.LeftStickY < -0.5f;
    public bool PadDown => _pad.DPadDown || _pad.LeftStickY > 0.5f;
    public bool PadLeft => _pad.DPadLeft || _pad.LeftStickX < -0.5f;
    public bool PadRight => _pad.DPadRight || _pad.LeftStickX > 0.5f;
    public bool PadAccept => _pad.ButtonA || _pad.ButtonX || _pad.ButtonB;
    public bool PadDone => _pad.ButtonStart;
    public bool PadPause => _pad.ButtonStart;
    public bool PadBack => _pad.ButtonBack;

    public void Dispose() => _pad.Dispose();
}
