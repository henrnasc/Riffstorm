using Raylib_cs;

namespace Riffstorm;

/// <summary>
/// Unified input. Gamepad profiles:
///  0 "Controle" – the classic PS2 joypad layout: L2 L1 R1 R2 ✕ as the five frets, D-pad ↑/↓ strums.
///    (Xbox: LT LB RB RT A). Star power: △/Y or Select/Back.
///  1 "Guitarra" – USB/Xbox guitar: A B Y X LB frets, strum bar = D-pad ↑/↓, whammy = right stick.
/// Keyboard: A S D F G frets (or F1–F5 / 1–5), Enter/↑/↓ strum, Espaço star power, Esc pause.
/// </summary>
public static class Input
{
    public static readonly bool[] Fret = new bool[5];
    public static readonly bool[] FretPressed = new bool[5];
    public static readonly bool[] FretReleased = new bool[5];
    public static bool Strum, StarPower, Pause;
    public static float Whammy;          // 0..1 position of the whammy bar
    public static bool WhammyActive;     // moved recently (like the original's whammy_timeout 0.5 s)
    static float _whLast; static double _whMovedAt = -9;
    public static bool Up, Down, Left, Right, Confirm, Back, AnyKey;
    /// <summary>Menu page switch: L1/R1 (LB/RB), keyboard Q/E or Page Up/Down.</summary>
    public static bool PrevPage, NextPage;
    /// <summary>Back without Backspace (screens with text fields): Esc or ◯/B.</summary>
    public static bool Cancel;
    public static int Pad = -1;
    public static string PadName = "";

    static readonly bool[] _prev = new bool[5];
    /// <summary>Is each action (see <see cref="Act"/>) held right now.</summary>
    public static readonly bool[] ActDown = new bool[Binds.Count];
    static readonly bool[] _actPrev = new bool[Binds.Count];
    /// <summary>The action's key/button went down this frame.</summary>
    public static bool Hit(Act a) => ActDown[(int)a] && !_actPrev[(int)a];
    static float _lyPrev, _whPrev;
    static double _navRepeat; static int _navDir;

    static bool Btn(GamepadButton b) => Pad >= 0 && Raylib.IsGamepadButtonDown(Pad, b);
    static bool BtnP(GamepadButton b) => Pad >= 0 && Raylib.IsGamepadButtonPressed(Pad, b);
    static float Axis(GamepadAxis a) => Pad >= 0 ? Raylib.GetGamepadAxisMovement(Pad, a) : 0;
    static bool Key(KeyboardKey k) => Raylib.IsKeyDown(k);
    static bool KeyP(KeyboardKey k) => Raylib.IsKeyPressed(k);

    public static unsafe void Update(Settings s)
    {
        Pad = -1;
        for (int i = 0; i < 4; i++) if (Raylib.IsGamepadAvailable(i)) { Pad = i; break; }
        if (Pad >= 0)
        {
            sbyte* n = Raylib.GetGamepadName(Pad);
            PadName = n != null ? new string(n) : "Controle";
        }

        Binds.Ensure(s);
        bool guitar = s.Controller == 1;
        var kb = s.KeyBinds; var pb = s.PadBinds;
        int pad = Pad;
        // current state of every action (keyboard key OR controller input)
        for (int a = 0; a < Binds.Count; a++)
        {
            _actPrev[a] = ActDown[a];
            ActDown[a] = Binds.KeyDown(kb[a]) || (a != (int)Act.Whammy && Binds.PadDown(pad, pb[a]));
        }
        bool Pressed(Act a) => ActDown[(int)a] && !_actPrev[(int)a];

        Span<bool> f = stackalloc bool[5];
        for (int i = 0; i < 5; i++) f[i] = ActDown[i];

        bool strum = Pressed(Act.StrumUp) || Pressed(Act.StrumDown) || KeyP(KeyboardKey.Enter) || KeyP(KeyboardKey.KpEnter);
        bool star = Pressed(Act.Star);
        float whammy = 0;
        if (Binds.KeyDown(kb[(int)Act.Whammy])) whammy = 0.5f + 0.5f * MathF.Sin((float)Raylib.GetTime() * 14f);
        whammy = Math.Max(whammy, Binds.PadValue(pad, pb[(int)Act.Whammy]));
        ActDown[(int)Act.Whammy] |= whammy > 0.08f;
        bool pause = Pressed(Act.Pause) || KeyP(KeyboardKey.Escape);

        if (pad >= 0)
        {
            if (!guitar)
            {
                // left stick flick also strums
                float ly = Axis(GamepadAxis.LeftY);
                if (Math.Abs(ly) > 0.6f && Math.Abs(_lyPrev) <= 0.6f) strum = true;
                _lyPrev = ly;
            }
            else
            {
                // tilting the guitar up = star power
                float tilt = Axis(GamepadAxis.RightY);
                if (tilt < -0.7f && _whPrev >= -0.7f) star = true;
                _whPrev = tilt;
            }
            pause |= BtnP(GamepadButton.MiddleRight);
        }

        for (int i = 0; i < 5; i++)
        {
            Fret[i] = f[i];
            FretPressed[i] = f[i] && !_prev[i];
            FretReleased[i] = !f[i] && _prev[i];
            _prev[i] = f[i];
        }
        Strum = strum; StarPower = star; Whammy = whammy; Pause = pause;
        double nowW = Raylib.GetTime();
        if (Math.Abs(whammy - _whLast) > 0.02f) { _whMovedAt = nowW; _whLast = whammy; }
        WhammyActive = whammy > 0.08f && nowW - _whMovedAt < 0.5;

        // ---- menu navigation ----
        int dir = 0;
        if (Key(KeyboardKey.Up) || Btn(GamepadButton.LeftFaceUp) || Axis(GamepadAxis.LeftY) < -0.6f) dir = 1;
        else if (Key(KeyboardKey.Down) || Btn(GamepadButton.LeftFaceDown) || Axis(GamepadAxis.LeftY) > 0.6f) dir = 2;
        else if (Key(KeyboardKey.Left) || Btn(GamepadButton.LeftFaceLeft) || Axis(GamepadAxis.LeftX) < -0.6f) dir = 3;
        else if (Key(KeyboardKey.Right) || Btn(GamepadButton.LeftFaceRight) || Axis(GamepadAxis.LeftX) > 0.6f) dir = 4;
        double now = Raylib.GetTime();
        bool fire = false;
        if (dir != _navDir) { fire = dir != 0; _navRepeat = now + 0.4; }
        else if (dir != 0 && now >= _navRepeat) { fire = true; _navRepeat = now + 0.08; }
        _navDir = dir;
        Up = fire && dir == 1; Down = fire && dir == 2; Left = fire && dir == 3; Right = fire && dir == 4;

        Confirm = KeyP(KeyboardKey.Enter) || KeyP(KeyboardKey.KpEnter) || BtnP(GamepadButton.RightFaceDown) || BtnP(GamepadButton.MiddleRight);
        PrevPage = Pressed(Act.PrevGame) || KeyP(KeyboardKey.PageUp);
        NextPage = Pressed(Act.NextGame) || KeyP(KeyboardKey.PageDown);
        Cancel = KeyP(KeyboardKey.Escape) || BtnP(GamepadButton.RightFaceRight);
        Back = KeyP(KeyboardKey.Escape) || KeyP(KeyboardKey.Backspace) || BtnP(GamepadButton.RightFaceRight);
        AnyKey = Raylib.GetKeyPressed() != 0 || Raylib.GetGamepadButtonPressed() != 0 || Raylib.IsMouseButtonPressed(MouseButton.Left);
    }
}
