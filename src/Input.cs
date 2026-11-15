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

        bool guitar = s.Controller == 1;
        Span<bool> f = stackalloc bool[5];
        f[0] = Key(KeyboardKey.A) || Key(KeyboardKey.F1) || Key(KeyboardKey.One);
        f[1] = Key(KeyboardKey.S) || Key(KeyboardKey.F2) || Key(KeyboardKey.Two);
        f[2] = Key(KeyboardKey.D) || Key(KeyboardKey.F3) || Key(KeyboardKey.Three);
        f[3] = Key(KeyboardKey.F) || Key(KeyboardKey.F4) || Key(KeyboardKey.Four);
        f[4] = Key(KeyboardKey.G) || Key(KeyboardKey.F5) || Key(KeyboardKey.Five);

        bool strum = KeyP(KeyboardKey.Enter) || KeyP(KeyboardKey.KpEnter) || KeyP(KeyboardKey.Up) || KeyP(KeyboardKey.Down) || KeyP(KeyboardKey.RightShift);
        bool star = KeyP(KeyboardKey.Space);
        float whammy = 0;
        if (Key(KeyboardKey.K)) whammy = 0.5f + 0.5f * MathF.Sin((float)Raylib.GetTime() * 14f);
        bool pause = KeyP(KeyboardKey.Escape) || KeyP(KeyboardKey.P);

        if (Pad >= 0)
        {
            if (!guitar)
            {
                f[0] |= Btn(GamepadButton.LeftTrigger2) || Axis(GamepadAxis.LeftTrigger) > 0.2f;
                f[1] |= Btn(GamepadButton.LeftTrigger1);
                f[2] |= Btn(GamepadButton.RightTrigger1);
                f[3] |= Btn(GamepadButton.RightTrigger2) || Axis(GamepadAxis.RightTrigger) > 0.2f;
                f[4] |= Btn(GamepadButton.RightFaceDown);
                star |= BtnP(GamepadButton.RightFaceUp) || BtnP(GamepadButton.MiddleLeft);
                // left stick flick also strums
                // right analog stick = whammy bar (any direction)
                float rx = Axis(GamepadAxis.RightX), ry = Axis(GamepadAxis.RightY);
                float mag = MathF.Sqrt(rx * rx + ry * ry);
                if (mag > 0.15f) whammy = Math.Max(whammy, Math.Clamp((mag - 0.15f) / 0.85f, 0, 1));
                float ly = Axis(GamepadAxis.LeftY);
                if (Math.Abs(ly) > 0.6f && Math.Abs(_lyPrev) <= 0.6f) strum = true;
                _lyPrev = ly;
            }
            else
            {
                f[0] |= Btn(GamepadButton.RightFaceDown);
                f[1] |= Btn(GamepadButton.RightFaceRight);
                f[2] |= Btn(GamepadButton.RightFaceUp);
                f[3] |= Btn(GamepadButton.RightFaceLeft);
                f[4] |= Btn(GamepadButton.LeftTrigger1);
                star |= BtnP(GamepadButton.MiddleLeft);
                whammy = Math.Max(whammy, Math.Clamp((Axis(GamepadAxis.RightX) + 1) / 2, 0, 1));
                float tilt = Axis(GamepadAxis.RightY);
                if (tilt < -0.7f && _whPrev >= -0.7f) star = true;
                _whPrev = tilt;
            }
            strum |= BtnP(GamepadButton.LeftFaceUp) || BtnP(GamepadButton.LeftFaceDown);
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
        PrevPage = KeyP(KeyboardKey.Q) || KeyP(KeyboardKey.PageUp) || BtnP(GamepadButton.LeftTrigger1);
        NextPage = KeyP(KeyboardKey.E) || KeyP(KeyboardKey.PageDown) || BtnP(GamepadButton.RightTrigger1);
        Cancel = KeyP(KeyboardKey.Escape) || BtnP(GamepadButton.RightFaceRight);
        Back = KeyP(KeyboardKey.Escape) || KeyP(KeyboardKey.Backspace) || BtnP(GamepadButton.RightFaceRight);
        AnyKey = Raylib.GetKeyPressed() != 0 || Raylib.GetGamepadButtonPressed() != 0 || Raylib.IsMouseButtonPressed(MouseButton.Left);
    }
}
