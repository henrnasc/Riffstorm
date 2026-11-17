using System.Numerics;
using Raylib_cs;

namespace Riffstorm;

/// <summary>Screen-space bloom: emissive elements are redrawn into a small buffer, blurred and added on top.</summary>
public static class Bloom
{
    static RenderTexture2D _a, _b;
    static int _w, _h;
    static Shader _blur;
    static int _dirLoc;
    static bool _init;

    const string BlurFs = @"#version 330
in vec2 fragTexCoord; in vec4 fragColor; uniform sampler2D texture0; uniform vec2 dir; out vec4 finalColor;
void main(){
  float w0 = 0.2270270270, w1 = 0.3162162162, w2 = 0.0702702703;
  vec4 s = texture(texture0, fragTexCoord) * w0;
  s += texture(texture0, fragTexCoord + dir * 1.3846153846) * w1;
  s += texture(texture0, fragTexCoord - dir * 1.3846153846) * w1;
  s += texture(texture0, fragTexCoord + dir * 3.2307692308) * w2;
  s += texture(texture0, fragTexCoord - dir * 3.2307692308) * w2;
  finalColor = vec4(s.rgb, 1.0);
}";

    static void Ensure()
    {
        if (!_init) { _blur = Raylib.LoadShaderFromMemory(null, BlurFs); _dirLoc = Raylib.GetShaderLocation(_blur, "dir"); _init = true; }
        int w = Math.Max(160, Gfx.W / 3), h = Math.Max(90, Gfx.H / 3);
        if (w == _w && h == _h) return;
        if (_w > 0) { Raylib.UnloadRenderTexture(_a); Raylib.UnloadRenderTexture(_b); }
        _w = w; _h = h;
        _a = Raylib.LoadRenderTexture(w, h); _b = Raylib.LoadRenderTexture(w, h);
        Raylib.SetTextureFilter(_a.Texture, TextureFilter.Bilinear); Raylib.SetTextureFilter(_b.Texture, TextureFilter.Bilinear);
    }

    public static void Begin()
    {
        Ensure();
        Raylib.BeginTextureMode(_a);
        Raylib.ClearBackground(Color.Black);
    }

    public static void EndAndComposite(float strength = 1f)
    {
        Raylib.EndTextureMode();
        for (int pass = 0; pass < 3; pass++)
        {
            float sp = 1 + pass;
            Blit(_a, _b, new Vector2(sp / _w, 0));
            Blit(_b, _a, new Vector2(0, sp / _h));
        }
        Raylib.BeginBlendMode(BlendMode.Additive);
        var c = new Color(255, 255, 255, (int)Math.Clamp(255 * strength, 0, 255));
        Raylib.DrawTexturePro(_a.Texture, new Rectangle(0, 0, _w, -_h), new Rectangle(0, 0, Gfx.W, Gfx.H), Vector2.Zero, 0, c);
        Raylib.DrawTexturePro(_a.Texture, new Rectangle(0, 0, _w, -_h), new Rectangle(0, 0, Gfx.W, Gfx.H), Vector2.Zero, 0, c);
        Raylib.EndBlendMode();
    }

    static void Blit(RenderTexture2D src, RenderTexture2D dst, Vector2 dir)
    {
        Raylib.BeginTextureMode(dst);
        Raylib.ClearBackground(Color.Black);
        Raylib.BeginShaderMode(_blur);
        Raylib.SetShaderValue(_blur, _dirLoc, dir, ShaderUniformDataType.Vec2);
        Raylib.DrawTexturePro(src.Texture, new Rectangle(0, 0, _w, -_h), new Rectangle(0, 0, _w, _h), Vector2.Zero, 0, Color.White);
        Raylib.EndShaderMode();
        Raylib.EndTextureMode();
    }
}

/// <summary>Meshes and textures shared by the highway (built once).</summary>
public static class HwAssets
{
    public static Mesh Ball, BallBand, BallBase, Shadow, Puck, PuckRim, PuckInner, Board, Bar, Plane;
    public static Texture2D BoardTex, FireTex, GlowTex, StreakTex, SegTex, LaneTex, StarTex;
    static bool _done;

    static Vector2[] Arc(float rx, float ry, float cy, int n, float a0 = -MathF.PI / 2, float a1 = MathF.PI / 2)
    {
        var p = new Vector2[n + 1];
        for (int i = 0; i <= n; i++) { float a = a0 + (a1 - a0) * i / n; p[i] = new(MathF.Max(0, MathF.Cos(a) * rx), cy + MathF.Sin(a) * ry); }
        p[0].X = 0; p[n].X = 0;
        return p;
    }

    public static Mesh SurfaceBoard; static float _surfaceRepeat = -1;

    /// <summary>Board mesh whose texture repeats <paramref name="repeat"/> times along its length.</summary>
    public static void EnsureSurfaceMesh(float repeat)
    {
        if (MathF.Abs(repeat - _surfaceRepeat) < 0.001f) return;
        _surfaceRepeat = repeat;
        SurfaceBoard = new MeshBuilder().PlaneXZ(1, 1, 1, repeat).Build();
    }

    public static void Init()
    {
        if (_done) return; _done = true;
        // note "ball": slightly flattened glossy sphere sitting on a thin dark seat
        Ball = new MeshBuilder().Lathe(Arc(0.155f, 0.115f, 0.125f, 18), 36).Build();
        BallBand = new MeshBuilder().Lathe(new Vector2[] { new(0.158f, 0.105f), new(0.168f, 0.118f), new(0.168f, 0.135f), new(0.158f, 0.148f) }, 36).Build();
        BallBase = new MeshBuilder().Lathe(new Vector2[] { new(0, 0.0f), new(0.17f, 0.0f), new(0.178f, 0.012f), new(0.17f, 0.024f), new(0, 0.024f) }, 36).Build();
        Shadow = new MeshBuilder().Lathe(new Vector2[] { new(0, 0), new(1, 0), new(1, 0.0005f), new(0, 0.0005f) }, 32).Build();
        // strike-line targets: glossy black pucks with a thin lit rim
        Puck = new MeshBuilder().Lathe(new Vector2[] { new(0, 0), new(0.232f, 0), new(0.245f, 0.01f), new(0.247f, 0.032f), new(0.238f, 0.046f), new(0.2f, 0.05f), new(0, 0.05f) }, 48).Build();
        PuckRim = new MeshBuilder().Lathe(new Vector2[] { new(0.226f, 0.044f), new(0.238f, 0.054f), new(0.25f, 0.046f), new(0.252f, 0.03f), new(0.25f, 0.012f) }, 48).Build();
        PuckInner = new MeshBuilder().Lathe(new Vector2[] { new(0.15f, 0.051f), new(0.16f, 0.0515f), new(0.165f, 0.051f) }, 40).Build();
        Board = new MeshBuilder().PlaneXZ(1, 1, 1, (Highway.Far + 1.5f) / 4f).Build();
        Bar = new MeshBuilder().Box(new Vector3(0, 0.5f, -0.5f), Vector3.One).Build();      // unit box spanning z 0..-1
        Plane = new MeshBuilder().PlaneXZ(1, 1, 1, 1).Build();                             // unit quad spanning z 0..-1

        BoardTex = Tex.Make(256, 256, (x, y) =>
        {
            float u = x / 256f, v = y / 256f;
            float grain = Tex.Fbm(u * 40, v * 6, 3, 3, 40, 6) * 0.03f;
            float fine = (Tex.Noise(x * 1.3f, y * 1.3f, 8) - 0.5f) * 0.02f;
            float b = 0.075f + grain + fine;
            return Tex.C(b * 1.02f, b, b * 1.05f);
        });
        GlowTex = Tex.Make(64, 64, (x, y) =>
        {
            float dx = (x - 31.5f) / 32, dy = (y - 31.5f) / 32; float d = MathF.Sqrt(dx * dx + dy * dy);
            return Tex.C(1, 1, 1, MathF.Pow(Math.Clamp(1 - d, 0, 1), 2.2f));
        }, false, false);
        StreakTex = Tex.Make(16, 64, (x, y) =>
        {
            float dx = MathF.Abs(x - 7.5f) / 8, dy = (y - 31.5f) / 32;
            float a = MathF.Pow(Math.Clamp(1 - dx, 0, 1), 2) * MathF.Pow(Math.Clamp(1 - MathF.Abs(dy), 0, 1), 0.8f);
            return Tex.C(1, 1, 0.9f + 0.1f * (1 - MathF.Abs(dy)), a);
        }, false, false);
        StarTex = Tex.Make(64, 64, (x, y) =>
        {
            float dx = (x - 31.5f) / 32, dy = (y - 31.5f) / 32;
            float a = MathF.Max(0, 1 - MathF.Abs(dx) * 10) * MathF.Max(0, 1 - MathF.Abs(dy)) + MathF.Max(0, 1 - MathF.Abs(dy) * 10) * MathF.Max(0, 1 - MathF.Abs(dx));
            a += MathF.Pow(MathF.Max(0, 1 - MathF.Sqrt(dx * dx + dy * dy) * 2.5f), 2);
            return Tex.C(1, 1, 1, Math.Clamp(a, 0, 1));
        }, false, false);
        // strike-strip segment: rounded warm light bar with soft falloff
        SegTex = Tex.Make(128, 64, (x, y) =>
        {
            float u = (x - 63.5f) / 64, v = (y - 31.5f) / 32;
            float bx = MathF.Max(0, MathF.Abs(u) - 0.78f) / 0.22f, by = MathF.Max(0, MathF.Abs(v) - 0.35f) / 0.65f;
            float d = MathF.Sqrt(bx * bx + by * by);
            float core = Math.Clamp(1 - d * 1.6f, 0, 1);
            float a = MathF.Pow(Math.Clamp(1 - d, 0, 1), 1.5f);
            return Tex.C(1, 0.85f + 0.15f * core, 0.6f + 0.4f * core, a);
        }, false, false);
        // lane light: bright near the strike line, fading up the highway
        LaneTex = Tex.Make(32, 128, (x, y) =>
        {
            float u = MathF.Abs(x - 15.5f) / 16, v = y / 127f;    // v=0 near, 1 far
            float a = MathF.Pow(1 - v, 1.6f) * (0.75f + 0.25f * MathF.Pow(1 - u, 2));
            return Tex.C(1, 1, 1, a);
        }, false, false);
        FireTex = MakeFire();
    }

    /// <summary>4×4 atlas of an evolving fireball (procedural noise).</summary>
    static Texture2D MakeFire()
    {
        const int F = 128;
        return Tex.Make(F * 4, F * 4, (x, y) =>
        {
            int fx = x / F, fy = y / F; int frame = fy * 4 + fx;
            float ph = frame / 16f;
            float u = (x % F - F / 2f) / (F / 2f), v = (y % F - F / 2f) / (F / 2f);   // -1..1, v down
            float up = -v;                                         // +1 top
            // tongue shape: wide base, rising and shrinking as the frame advances
            float rise = ph * 0.9f;
            float cx = u, cy = up + 0.35f - rise * 0.6f;
            float r = MathF.Sqrt(cx * cx * (1.1f + ph) + cy * cy * 0.55f);
            float n = Tex.Fbm(x * 0.045f + frame * 1.7f, y * 0.045f + ph * 6f, 21, 4);
            float body = (0.95f - ph * 0.45f) - r + (n - 0.5f) * 0.9f;
            float a = Math.Clamp(body * 3.2f, 0, 1) * Math.Clamp(1 - ph * 0.8f, 0, 1);
            float heat = Math.Clamp(body * 2.2f + 0.15f - ph * 0.5f, 0, 1);
            // colour ramp: deep red → orange → yellow → white core
            float R = Math.Clamp(0.6f + heat * 1.2f, 0, 1), G = Math.Clamp(heat * 1.25f - 0.1f, 0, 1), B = Math.Clamp(heat * heat * 1.3f - 0.4f, 0, 1);
            return Tex.C(R, G, B, a);
        }, false, false);
    }
}

/// <summary>The 3D note highway (refined: matte board, thin lines, amber strike strip, glossy note balls, bloom, fire).</summary>
public sealed partial class Highway
{
    public const float LaneW = 0.5f, Far = 15f, BoardHalf = 1.32f;
    public float Speed = 7.5f;
    public bool Lefty;
    public float Whammy;
    public bool EnergyFilling;
    /// <summary>Another player's highway (online): ignores the local frets and skips the bloom pass.</summary>
    public bool Ghost;
    /// <summary>Original highway surface from a disc (null = Riffstorm's own board).</summary>
    public static Texture2D? Surface;
    float _whSmooth;
    Camera3D _cam;

    struct Particle { public Vector3 P, V; public float Life, Max; public Color C; public float Size; public int Kind; public float Rot; }
    readonly List<Particle> _parts = new();
    readonly Random _rng = new();
    readonly float[] _laneFlash = new float[5];
    readonly float[] _laneLight = new float[5];
    readonly float[] _laneHeld = new float[5];
    readonly bool[] _sustaining = new bool[5];
    Player _p; double _t; bool _star;

    public Highway() => HwAssets.Init();

    // battle effects (set by the game every frame)
    public bool Mirror;                    // lanes flipped
    public int[] ColorPerm;                // gem colours shuffled (null = normal)
    public float HideNear;                 // notes vanish this close to the strike line
    public float Sway;                     // 0..1 drunk-highway strength
    public readonly List<(int lane, double time)> Fakes = new();   // decoy notes

    float LaneX(int lane) => (((Lefty ^ Mirror) ? 4 - lane : lane) - 2) * LaneW;
    float Z(double noteTime, double t) => -(float)(noteTime - t) * Speed;
    static Color LaneCol(int l, bool star) => star ? Gfx.StarBlue : Gfx.Fret[l];

    void Begin3D(Rectangle view, int fbH)
    {
        Raylib.BeginMode3D(_cam);
        Rlgl.MatrixMode(MatrixMode.Projection);
        Rlgl.LoadIdentity();
        double aspect = view.Width / view.Height, near = 0.05;
        double top = near * Math.Tan(_cam.FovY * 0.5 * Math.PI / 180);
        Rlgl.Frustum(-top * aspect, top * aspect, -top, top, near, 200);
        Rlgl.MatrixMode(MatrixMode.ModelView);
    }

    public void Draw(Player p, double t, double renderDt, Rectangle view, bool star)
    {
        _p = p; _t = t; _star = star;
        float dt = (float)renderDt;
        _whSmooth += (Whammy - _whSmooth) * Math.Min(1, dt * 12);
        _cam = new Camera3D(new Vector3(0, 4.25f, 5.0f), new Vector3(0, 0, -5.0f), Vector3.UnitY, 44, CameraProjection.Perspective);
        if (Sway > 0)
        {
            float sx = MathF.Sin((float)t * 2.3f) * 1.1f * Sway, roll = MathF.Sin((float)t * 1.7f + 1) * 0.22f * Sway;
            _cam.Position += new Vector3(sx, MathF.Sin((float)t * 3.1f) * 0.35f * Sway, 0);
            _cam.Up = Vector3.Normalize(new Vector3(MathF.Sin(roll), MathF.Cos(roll), 0));
        }
        for (int l = 0; l < 5; l++)
        {
            _laneFlash[l] = Math.Max(0, _laneFlash[l] - dt * 4);
            _laneHeld[l] += ((!Ghost && Input.Fret[l] ? 1 : 0) - _laneHeld[l]) * Math.Min(1, dt * 30);
        }
        UpdateSustainFlags();
        for (int l = 0; l < 5; l++)
        {
            float target = Math.Max(_laneFlash[l], _sustaining[l] ? 0.85f : 0);
            _laneLight[l] += (target - _laneLight[l]) * Math.Min(1, dt * (target > _laneLight[l] ? 30 : 5));
        }
        StepParticles(dt);

        // ---------- main pass
        Rlgl.DrawRenderBatchActive();
        Rlgl.Viewport((int)view.X, (int)(Gfx.H - view.Y - view.Height), (int)view.Width, (int)view.Height);
        Begin3D(view, Gfx.H);
        R3.Lights(_cam.Position, new Vector3(0.15f, -1, -0.35f), new Color(255, 248, 240, 255), 0.9f, new Color(60, 60, 75, 255), 0.5f,
                  new Vector3(0, 0.8f, 0.9f), star ? new Color(90, 170, 255, 255) : new Color(255, 160, 70, 255), 0.55f);
        Raylib.BeginShaderMode(R3.Shader);
        DrawBoard(t, star);
        DrawLaneLights(star);
        DrawStrip(t, star, false);
        DrawSustains(t, star, false);
        DrawNotes(t, star);
        DrawTargets(star, false);
        Raylib.EndShaderMode();
        R3.NoFade(); R3.SetUv(Vector2.Zero);
        DrawParticles(false);
        Raylib.EndMode3D();
        Rlgl.Viewport(0, 0, Gfx.W, Gfx.H);
        if (Ghost) return;

        // ---------- glow pass (bloom)
        Bloom.Begin();
        Begin3D(new Rectangle(0, 0, Gfx.W, Gfx.H), Gfx.H);
        Raylib.BeginShaderMode(R3.Shader);
        R3.SetFade(-Far + 5f, -Far + 0.5f);
        DrawStrip(t, star, true);
        DrawSustains(t, star, true);
        DrawTargets(star, true);
        Raylib.EndShaderMode();
        R3.NoFade();
        DrawNoteGlows(t, star);
        DrawParticles(true);
        Raylib.EndMode3D();
        Bloom.EndAndComposite(0.85f);
    }

    void UpdateSustainFlags()
    {
        Array.Clear(_sustaining);
        if (_p == null) return;
        for (int i = Math.Max(0, _p.Next - 8); i < Math.Min(_p.Notes.Count, _p.Next + 1); i++)
            if (_p.Notes[i].Holding)
                for (int l = 0; l < 5; l++) if ((_p.Notes[i].Mask & (1 << l)) != 0) _sustaining[l] = true;
    }

    // ------------------------------------------------------------------ board
    void DrawBoard(double t, bool star)
    {
        R3.SetFade(-Far + 5f, -Far + 0.5f);
        float len = Far + 1.5f;
        if (Surface is Texture2D surf)
        {
            // an original highway from a disc: 1:2 images, each tile as long as twice the board width
            float tile = BoardHalf * 2 * 2;
            HwAssets.EnsureSurfaceMesh(len / tile);
            R3.SetUv(new Vector2(0, (float)((t * Speed) / tile % 1.0)));
            R3.Draw(HwAssets.SurfaceBoard, R3.M(R3.S(BoardHalf * 2, 1, len), R3.T(0, -0.005f, 1.5f)), star ? new Color(170, 200, 255, 255) : Color.White, 0.2f, 18, 0, 0.0f, surf);
        }
        else
        {
            R3.SetUv(new Vector2(0, (float)((t * Speed) / 4f % 1.0)));
            R3.Draw(HwAssets.Board, R3.M(R3.S(BoardHalf * 2, 1, len), R3.T(0, -0.005f, 1.5f)), star ? new Color(170, 200, 255, 255) : Color.White, 0.35f, 18, 0, 0.0f, HwAssets.BoardTex);
        }
        R3.SetUv(Vector2.Zero);
        var line = new Color(235, 235, 240, 150);
        // thin lane dividers (between lanes) + crisp edges
        for (int i = 1; i < 5; i++)
        {
            float x = (i - 2.5f) * LaneW;
            R3.Draw(HwAssets.Bar, R3.M(R3.S(0.014f, 0.002f, len), R3.T(x, 0.0f, 1.5f)), line, 0, 1, 0, 0, null, true);
        }
        for (int s = -1; s <= 1; s += 2)
        {
            R3.Draw(HwAssets.Bar, R3.M(R3.S(0.03f, 0.004f, len), R3.T(s * BoardHalf, 0.0f, 1.5f)), new Color(245, 245, 250, 230), 0, 1, 0, 0, null, true);
            R3.Draw(HwAssets.Bar, R3.M(R3.S(0.05f, 0.05f, len), R3.T(s * (BoardHalf + 0.04f), -0.04f, 1.5f)), new Color(25, 25, 28, 255), 0.6f, 40, 0, 0.3f);
        }
        // beat lines: thin white, measure lines a little stronger
        foreach (var b in _p.Chart.Beats)
        {
            float z = Z(b.Time, t);
            if (z > 1.5f) continue;
            if (z < -Far) break;
            float th = b.Measure ? 0.028f : 0.014f;
            R3.Draw(HwAssets.Bar, R3.M(R3.S(BoardHalf * 2, 0.002f, th), R3.T(0, 0.001f, z + th / 2)), new Color(240, 240, 245, b.Measure ? 190 : 95), 0, 1, 0, 0, null, true);
        }
    }

    void DrawLaneLights(bool star)
    {
        Rlgl.DrawRenderBatchActive();
        Raylib.BeginBlendMode(BlendMode.Additive);
        Rlgl.DisableDepthMask();
        for (int l = 0; l < 5; l++)
        {
            float k = _laneLight[l];
            if (k < 0.01f) continue;
            var c = Gfx.A(Gfx.Bright(LaneCol(l, star), 0.8f), 0.55f * k);
            R3.Draw(HwAssets.Plane, R3.M(R3.S(LaneW - 0.02f, 1, Far * 0.75f), R3.T(LaneX(l), 0.003f, 0.25f)), c, 0, 1, 0, 0, HwAssets.LaneTex, true);
        }
        Rlgl.DrawRenderBatchActive();
        Rlgl.EnableDepthMask();
        Raylib.EndBlendMode();
    }

    /// <summary>The warm segmented light strip at the strike line.</summary>
    void DrawStrip(double t, bool star, bool glow)
    {
        Rlgl.DrawRenderBatchActive();
        Raylib.BeginBlendMode(BlendMode.Additive);
        Rlgl.DisableDepthMask();
        var amber = star ? new Color(120, 200, 255, 255) : new Color(255, 160, 55, 255);
        for (int l = 0; l < 5; l++)
        {
            float press = _laneHeld[l], flash = _laneFlash[l];
            var c = Gfx.Lerp(amber, LaneCol(l, star), Math.Min(1, press * 0.6f + flash * 0.8f));
            float k = (glow ? 0.7f : 1.0f) + press * 0.3f + flash * 0.5f;
            R3.Draw(HwAssets.Plane, R3.M(R3.S(LaneW * 0.98f, 1, 0.34f), R3.T(LaneX(l), 0.004f, -0.2f)), Gfx.A(c, Math.Min(1, k)), 0, 1, 0, 0, HwAssets.SegTex, true);
        }
        // soft spill of light onto the neck
        R3.Draw(HwAssets.Plane, R3.M(R3.S(BoardHalf * 2.3f, 1, 1.3f), R3.T(0, 0.002f, 0.25f)), Gfx.A(amber, glow ? 0.12f : 0.22f), 0, 1, 0, 0, HwAssets.GlowTex, true);
        Rlgl.DrawRenderBatchActive();
        Rlgl.EnableDepthMask();
        Raylib.EndBlendMode();
    }

    // ------------------------------------------------------------------ notes
    void DrawSustains(double t, bool star, bool glow)
    {
        var notes = _p.Notes;
        for (int i = Math.Max(0, _p.Next - 12); i < notes.Count; i++)
        {
            var n = notes[i];
            float z = Z(n.Time, t);
            if (z < -Far) break;
            if (!n.Sustain) continue;
            float zEnd = Math.Max(Z(n.EndTime, t), -Far);
            float zStart = n.Hit ? Math.Min(z, 0.05f) : z;
            if (zStart <= zEnd || (n.Hit && !n.Holding)) continue;
            bool live = n.Holding, dead = n.Missed || n.Released;
            if (glow && dead) continue;
            for (int l = 0; l < 5; l++)
            {
                if ((n.Mask & (1 << l)) == 0) continue;
                var c = dead ? new Color(70, 70, 76, 255) : LaneCol(l, star || n.Star);
                float x = LaneX(l);
                int segs = Math.Max(1, (int)((zStart - zEnd) / 0.15f));
                for (int s = 0; s < segs; s++)
                {
                    float za = zStart - (zStart - zEnd) * s / segs, zb = zStart - (zStart - zEnd) * (s + 1) / segs;
                    float amp = live ? 0.01f + 0.085f * _whSmooth : 0;
                    float wx = MathF.Sin((float)t * 26 + za * (2.4f + _whSmooth * 3)) * amp * MathF.Min(1, -za * 0.5f + 0.15f);
                    if (glow)
                        R3.Draw(HwAssets.Bar, R3.M(R3.S(0.09f, 0.01f, za - zb + 0.004f), R3.T(x + wx, 0.02f, za)), Gfx.Bright(c, live ? 1.2f : 0.6f), 0, 1, 0, 0, null, true);
                    else
                    {
                        R3.Draw(HwAssets.Bar, R3.M(R3.S(0.11f, 0.008f, za - zb + 0.004f), R3.T(x + wx, 0.008f, za)), Gfx.A(Gfx.Bright(c, 0.5f), 0.6f), 0, 1, 0, 0, null, true);
                        R3.Draw(HwAssets.Bar, R3.M(R3.S(0.045f, 0.012f, za - zb + 0.004f), R3.T(x + wx, 0.014f, za)), Gfx.Bright(c, live ? 1.4f : 1.0f), 0, 1, 0, 0, null, true);
                    }
                }
            }
        }
    }

    void DrawNotes(double t, bool star)
    {
        var notes = _p.Notes;
        int start = Math.Max(0, _p.Next - 12);
        for (int i = notes.Count - 1; i >= start; i--)
        {
            var n = notes[i];
            if (n.Hit) continue;
            float z = Z(n.Time, t);
            if (z < -Far || z > 1.6f) continue;
            if (HideNear > 0 && z > -HideNear && !n.Missed) continue;          // battle: "Sumiço"
            for (int l = 0; l < 5; l++)
                if ((n.Mask & (1 << l)) != 0) DrawNote(new Vector3(LaneX(l), 0.0f, z), l, n.Hopo, n.Star, star, n.Missed, ColorPerm);
            if (n.Box && !n.Missed)
            {
                // battle item box: a spinning rainbow cube floating over the note
                int l0 = System.Numerics.BitOperations.TrailingZeroCount(n.Mask);
                float spin = (float)t * 3 + i;
                var hue = Raylib.ColorFromHSV((float)(t * 120 % 360), 0.95f, 0.85f);
                var pos = new Vector3(LaneX(l0), 0.42f + 0.05f * MathF.Sin(spin * 2), z + 0.13f);
                var rot = R3.M(Raymath.MatrixRotateY(spin), Raymath.MatrixRotateX(0.5f), R3.T(pos));
                R3.Draw(HwAssets.Bar, R3.M(R3.T(0, -0.5f, 0.5f), R3.S(0.26f), rot), hue, 0.35f, 24, 0.15f, 0.5f);
                // dark edges so it reads as a box, not a blob of light
                var edge = new Color(20, 10, 30, 255);
                foreach (var (ex, ey) in new[] { (1, 1), (1, -1), (-1, 1), (-1, -1) })
                {
                    R3.Draw(HwAssets.Bar, R3.M(R3.T(0, -0.5f, 0.5f), R3.S(0.03f, 0.03f, 0.275f), R3.T(ex * 0.13f, ey * 0.13f, 0), rot), edge, 0.1f, 8, 0, 0);
                    R3.Draw(HwAssets.Bar, R3.M(R3.T(0, -0.5f, 0.5f), R3.S(0.275f, 0.03f, 0.03f), R3.T(0, ex * 0.13f, ey * 0.13f), rot), edge, 0.1f, 8, 0, 0);
                    R3.Draw(HwAssets.Bar, R3.M(R3.T(0, -0.5f, 0.5f), R3.S(0.03f, 0.275f, 0.03f), R3.T(ex * 0.13f, 0, ey * 0.13f), rot), edge, 0.1f, 8, 0, 0);
                }
            }
        }
        foreach (var (lane, time) in Fakes)
        {
            float z = Z(time, t);
            if (z < -Far || z > 0.4f) continue;
            if (HideNear > 0 && z > -HideNear) continue;
            DrawNote(new Vector3(LaneX(lane), 0.0f, z), lane, false, false, star, false, ColorPerm, 0.55f);
        }
    }

    static void DrawNote(Vector3 p, int lane, bool hopo, bool starNote, bool starMode, bool missed, int[] perm = null, float alpha = 1)
    {
        var col = starMode ? Gfx.StarBlue : starNote ? new Color(235, 245, 255, 255) : Gfx.Fret[perm != null ? perm[lane] : lane];
        if (alpha < 1) col = Gfx.A(col, alpha);
        if (missed) col = Gfx.Lerp(col, new Color(55, 55, 58, 255), 0.7f);
        float sc = hopo ? 0.88f : 1f;
        var xf = R3.M(R3.S(sc), R3.T(p));
        // soft contact shadow
        R3.Draw(HwAssets.Shadow, R3.M(R3.S(0.2f * sc, 1, 0.2f * sc), R3.T(p + new Vector3(0, 0.002f, 0))), new Color(0, 0, 0, 120), 0, 1, 0, 0, HwAssets.GlowTex, true);
        R3.Draw(HwAssets.BallBase, xf, new Color(14, 14, 16, 255), 0.8f, 60, 0, 0.2f);
        R3.Draw(HwAssets.Ball, xf, col, 1.3f, 110, missed ? 0 : 0.12f, 0.7f);
        if (hopo) R3.Draw(HwAssets.BallBand, xf, Color.White, 1, 80, missed ? 0.1f : 0.9f, 0);
        if (starNote && !starMode) R3.Draw(HwAssets.BallBand, xf, Gfx.Fret[lane], 1, 80, 0.8f, 0);
    }

    void DrawNoteGlows(double t, bool star)
    {
        Raylib.BeginBlendMode(BlendMode.Additive);
        var notes = _p.Notes;
        for (int i = Math.Max(0, _p.Next - 4); i < notes.Count; i++)
        {
            var n = notes[i];
            float z = Z(n.Time, t);
            if (z < -Far + 2) break;
            if (n.Hit || n.Missed || z > 1.2f) continue;
            for (int l = 0; l < 5; l++)
                if ((n.Mask & (1 << l)) != 0)
                {
                    var c = star ? Gfx.StarBlue : n.Star ? Color.White : Gfx.Fret[l];
                    Raylib.DrawBillboard(_cam, HwAssets.GlowTex, new Vector3(LaneX(l), 0.13f, z), n.Star || star ? 0.55f : 0.38f, Gfx.A(c, 0.55f));
                    if (n.Star && !star) Raylib.DrawBillboard(_cam, HwAssets.StarTex, new Vector3(LaneX(l), 0.16f, z), 0.5f + 0.1f * MathF.Sin((float)t * 8 + i), Gfx.A(Color.White, 0.9f));
                }
        }
        Raylib.EndBlendMode();
    }

    // ------------------------------------------------------------------ targets
    void DrawTargets(bool star, bool glow)
    {
        for (int l = 0; l < 5; l++)
        {
            float press = _laneHeld[l], flash = _laneFlash[l];
            var p = new Vector3(LaneX(l), 0.004f, 0.0f);
            var xf = R3.M(R3.S(1, 1 - press * 0.3f, 1), R3.T(p));
            var col = LaneCol(l, star);
            var rimCol = Gfx.Lerp(star ? new Color(140, 210, 255, 255) : new Color(255, 165, 60, 255), col, 0.35f + press * 0.65f);
            if (glow)
            {
                R3.Draw(HwAssets.PuckRim, xf, Gfx.Bright(rimCol, 0.8f + press * 0.6f + flash), 0, 1, 0, 0, null, true);
                if (press > 0.3f || flash > 0) R3.Draw(HwAssets.PuckInner, R3.M(R3.S(1.4f, 1, 1.4f), xf), Gfx.Bright(col, 0.5f + flash), 0, 1, 0, 0, null, true);
                continue;
            }
            R3.Draw(HwAssets.Puck, xf, new Color(6, 6, 7, 255), 0.55f, 220, 0, 0.12f);
            R3.Draw(HwAssets.PuckRim, xf, rimCol, 1.0f, 60, 0.9f + press * 0.8f + flash, 0.2f);
            if (press > 0.05f || flash > 0) R3.Draw(HwAssets.PuckInner, xf, Gfx.Bright(col, 0.35f + press * 0.8f), 0.4f, 20, press * 0.8f + flash, 0);
        }
    }

    // ------------------------------------------------------------------ effects
    public void OnHit(int lane, bool star)
    {
        _laneFlash[lane] = 1;
        float x = LaneX(lane);
        // fireball burst: a few large animated fire sprites
        for (int i = 0; i < 3; i++)
            _parts.Add(new Particle
            {
                P = new Vector3(x + (float)(_rng.NextDouble() - 0.5) * 0.1f, 0.15f, 0.1f),
                V = new Vector3((float)(_rng.NextDouble() - 0.5) * 0.4f, 0.9f + i * 0.5f, 0),
                Life = 0.38f, Max = 0.38f, C = star ? new Color(140, 200, 255, 255) : new Color(255, 215, 170, 255),
                Size = 0.5f + i * 0.16f, Kind = 1, Rot = (float)_rng.NextDouble() * 30 - 15
            });
        // sparks: fast streaks that fall with gravity
        for (int i = 0; i < 26; i++)
        {
            var dir = new Vector3((float)(_rng.NextDouble() - 0.5) * 3.2f, 1.2f + (float)_rng.NextDouble() * 3.4f, (float)(_rng.NextDouble() - 0.2) * 1.6f);
            var c = star ? new Color(170, 225, 255, 255) : (_rng.Next(3) == 0 ? new Color(255, 250, 210, 255) : new Color(255, 170, 60, 255));
            _parts.Add(new Particle { P = new Vector3(x, 0.15f, 0.08f), V = dir, Life = 0.35f + (float)_rng.NextDouble() * 0.35f, Max = 0.7f, C = c, Size = 0.02f + (float)_rng.NextDouble() * 0.015f, Kind = 0 });
        }
    }

    public void OnSustainSpark(int lane, bool star)
    {
        if (_rng.NextDouble() > 0.7) return;
        float x = LaneX(lane);
        var v = new Vector3((float)(_rng.NextDouble() - 0.5) * 1.6f, 1.2f + (float)_rng.NextDouble() * 1.8f, 0.3f);
        _parts.Add(new Particle { P = new Vector3(x, 0.15f, 0.08f), V = v, Life = 0.3f, Max = 0.4f, C = star ? Gfx.StarBlue : new Color(255, 190, 80, 255), Size = 0.018f, Kind = 0 });
        if (_rng.NextDouble() < 0.18)
            _parts.Add(new Particle { P = new Vector3(x, 0.12f, 0.1f), V = new Vector3(0, 0.8f, 0), Life = 0.3f, Max = 0.3f, C = star ? Gfx.StarBlue : new Color(255, 215, 170, 255), Size = 0.32f, Kind = 1 });
    }

    void StepParticles(float dt)
    {
        for (int i = _parts.Count - 1; i >= 0; i--)
        {
            var p = _parts[i];
            p.Life -= dt;
            if (p.Life <= 0) { _parts.RemoveAt(i); continue; }
            if (p.Kind == 0) { p.V.Y -= 9 * dt; p.V *= 1 - dt * 0.8f; }
            else p.V *= 1 - dt * 2.5f;
            p.P += p.V * dt;
            _parts[i] = p;
        }
    }

    void DrawParticles(bool glow)
    {
        Raylib.BeginBlendMode(BlendMode.Additive);
        Rlgl.DisableDepthMask();
        foreach (var p in _parts)
        {
            float k = p.Life / p.Max;
            if (p.Kind == 1)
            {
                int frame = Math.Clamp((int)((1 - k) * 16), 0, 15);
                var src = new Rectangle(frame % 4 * 128, frame / 4 * 128, 128, 128);
                float s = p.Size * (0.8f + (1 - k) * 0.5f);
                Raylib.DrawBillboardPro(_cam, HwAssets.FireTex, src, p.P + new Vector3(0, s * 0.35f, 0), Vector3.UnitY, new Vector2(s, s), new Vector2(s / 2, s / 2), p.Rot, Gfx.A(p.C, glow ? 0.22f * k : Math.Min(0.85f, k * 1.3f)));
            }
            else
            {
                var up = p.V.LengthSquared() > 0.01f ? Vector3.Normalize(p.V) : Vector3.UnitY;
                float len = 0.05f + p.V.Length() * 0.035f;
                Raylib.DrawBillboardPro(_cam, HwAssets.StreakTex, new Rectangle(0, 0, 16, 64), p.P, up, new Vector2(p.Size * (glow ? 2.5f : 1), len), new Vector2(p.Size / 2, len / 2), 0, Gfx.A(p.C, Math.Min(1, k * 1.5f)));
            }
        }
        Rlgl.DrawRenderBatchActive();
        Rlgl.EnableDepthMask();
        Raylib.EndBlendMode();
    }
}
