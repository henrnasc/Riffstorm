using System.Numerics;
using Raylib_cs;

namespace Riffstorm;

/// <summary>Lit/outlined mesh rendering: one GLSL 330 shader with half-lambert, specular, rim, point light, emissive, depth fade.</summary>
public static unsafe class R3
{
    public static Shader Shader;
    public static Material Mat;
    static int _lView, _lLightDir, _lLightCol, _lAmb, _lL2Pos, _lL2Col, _lSpec, _lShin, _lEmis, _lRim, _lUv, _lFadeN, _lFadeF, _lUnlit, _lOutline, _lAlphaCut;
    public static Texture2D White;

    const string Vs = @"#version 330
in vec3 vertexPosition; in vec2 vertexTexCoord; in vec3 vertexNormal; in vec4 vertexColor;
uniform mat4 mvp; uniform mat4 matModel; uniform mat4 matNormal; uniform mat4 matView; uniform mat4 matProjection; uniform float outlineWidth;
out vec3 fragPos; out vec2 fragTexCoord; out vec3 fragNormal; out vec4 fragColor;
void main(){
  vec4 wp = matModel * vec4(vertexPosition, 1.0);
  vec3 wn = normalize(vec3(matNormal * vec4(vertexNormal, 0.0)));
  wp.xyz += wn * outlineWidth;              // world-space ink outline
  fragPos = wp.xyz;
  fragTexCoord = vertexTexCoord; fragColor = vertexColor;
  fragNormal = wn;
  gl_Position = matProjection * matView * wp;
}";

    const string Fs = @"#version 330
in vec3 fragPos; in vec2 fragTexCoord; in vec3 fragNormal; in vec4 fragColor;
uniform sampler2D texture0; uniform vec4 colDiffuse;
uniform vec3 viewPos; uniform vec3 lightDir; uniform vec3 lightColor; uniform vec3 ambient;
uniform vec3 light2Pos; uniform vec3 light2Color;
uniform float specular; uniform float shininess; uniform float emissive; uniform float rim;
uniform vec2 uvOffset; uniform float fadeNear; uniform float fadeFar; uniform int unlit; uniform float alphaCut;
out vec4 finalColor;
void main(){
  vec4 base = texture(texture0, fragTexCoord + uvOffset) * colDiffuse * fragColor;
  if (alphaCut > 0.0 && base.a < alphaCut) discard;
  if (unlit == 1) { finalColor = base; }
  else {
    vec3 n = normalize(fragNormal); if (!gl_FrontFacing) n = -n;
    vec3 v = normalize(viewPos - fragPos); vec3 l = normalize(-lightDir);
    float d = max(dot(n, l), 0.0);
    float hl = d * 0.65 + 0.35;
    vec3 h = normalize(l + v); float s = pow(max(dot(n, h), 0.0), shininess) * specular;
    vec3 l2 = light2Pos - fragPos; float att = 1.0 / (1.0 + dot(l2, l2) * 0.015);
    float d2 = max(dot(n, normalize(l2)), 0.0) * att;
    float r = pow(1.0 - max(dot(n, v), 0.0), 3.0) * rim;
    vec3 c = base.rgb * (ambient + lightColor * hl + light2Color * d2) + vec3(s) * lightColor + r * light2Color + base.rgb * emissive;
    finalColor = vec4(c, base.a);
  }
  if (fadeFar < fadeNear) { float k = clamp((fragPos.z - fadeFar) / (fadeNear - fadeFar), 0.0, 1.0); finalColor.a *= k; }
}";

    public static void Init()
    {
        Shader = Raylib.LoadShaderFromMemory(Vs, Fs);
        Shader.Locs[(int)ShaderLocationIndex.MatrixModel] = Raylib.GetShaderLocation(Shader, "matModel");
        Shader.Locs[(int)ShaderLocationIndex.MatrixNormal] = Raylib.GetShaderLocation(Shader, "matNormal");
        Shader.Locs[(int)ShaderLocationIndex.VectorView] = Raylib.GetShaderLocation(Shader, "viewPos");
        Shader.Locs[(int)ShaderLocationIndex.MatrixView] = Raylib.GetShaderLocation(Shader, "matView");
        Shader.Locs[(int)ShaderLocationIndex.MatrixProjection] = Raylib.GetShaderLocation(Shader, "matProjection");
        int L(string n) => Raylib.GetShaderLocation(Shader, n);
        _lView = L("viewPos"); _lLightDir = L("lightDir"); _lLightCol = L("lightColor"); _lAmb = L("ambient");
        _lL2Pos = L("light2Pos"); _lL2Col = L("light2Color"); _lSpec = L("specular"); _lShin = L("shininess");
        _lEmis = L("emissive"); _lRim = L("rim"); _lUv = L("uvOffset"); _lFadeN = L("fadeNear"); _lFadeF = L("fadeFar");
        _lUnlit = L("unlit"); _lOutline = L("outlineWidth"); _lAlphaCut = L("alphaCut");
        Mat = Raylib.LoadMaterialDefault();
        Mat.Shader = Shader;
        var img = Raylib.GenImageColor(4, 4, Color.White);
        White = Raylib.LoadTextureFromImage(img);
        Raylib.UnloadImage(img);
        NoFade();
        SetUv(Vector2.Zero);
    }

    static void F(int loc, float v) => Raylib.SetShaderValue(Shader, loc, v, ShaderUniformDataType.Float);
    static void V3(int loc, Vector3 v) => Raylib.SetShaderValue(Shader, loc, v, ShaderUniformDataType.Vec3);
    static Vector3 Cv(Color c, float k = 1) => new(c.R / 255f * k, c.G / 255f * k, c.B / 255f * k);

    /// <summary>Scene lighting. light2 = coloured stage/point light.</summary>
    public static void Lights(Vector3 viewPos, Vector3 dir, Color key, float keyK, Color amb, float ambK, Vector3 l2Pos, Color l2, float l2K)
    {
        V3(_lView, viewPos); V3(_lLightDir, Vector3.Normalize(dir)); V3(_lLightCol, Cv(key, keyK)); V3(_lAmb, Cv(amb, ambK));
        V3(_lL2Pos, l2Pos); V3(_lL2Col, Cv(l2, l2K));
    }

    public static void SetFade(float near, float far) { F(_lFadeN, near); F(_lFadeF, far); }
    public static void NoFade() => SetFade(0, 1);
    public static void AlphaCut(float v) => F(_lAlphaCut, v);
    public static void SetUv(Vector2 uv) => Raylib.SetShaderValue(Shader, _lUv, uv, ShaderUniformDataType.Vec2);

    /// <summary>Draw a mesh lit. spec 0..1, shin = exponent, emis = self-illumination, rim = rim-light strength.</summary>
    public static void Draw(Mesh m, Matrix4x4 xf, Color c, float spec = 0.4f, float shin = 32, float emis = 0, float rim = 0.3f, Texture2D? tex = null, bool unlit = false)
    {
        F(_lSpec, spec); F(_lShin, shin); F(_lEmis, emis); F(_lRim, rim);
        Raylib.SetShaderValue(Shader, _lUnlit, unlit ? 1 : 0, ShaderUniformDataType.Int);
        F(_lOutline, 0);
        Mat.Maps[0].Color = c;
        Mat.Maps[0].Texture = tex ?? White;
        Raylib.DrawMesh(m, Mat, xf);
    }

    /// <summary>Cartoon ink outline (inverted hull) then the lit mesh – the "2000s character" look.</summary>
    public static void DrawInked(Mesh m, Matrix4x4 xf, Color c, float width = 0.012f, float spec = 0.25f, float shin = 16, float rim = 0.35f, Texture2D? tex = null)
    {
        Rlgl.DrawRenderBatchActive();
        Rlgl.SetCullFace(0);   // cull front faces -> only the expanded back shell shows
        Raylib.SetShaderValue(Shader, _lUnlit, 1, ShaderUniformDataType.Int);
        F(_lOutline, width);
        Mat.Maps[0].Color = new Color(8, 6, 8, 255);
        Mat.Maps[0].Texture = White;
        Raylib.DrawMesh(m, Mat, xf);
        Rlgl.DrawRenderBatchActive();
        Rlgl.SetCullFace(1);
        Draw(m, xf, c, spec, shin, 0, rim, tex);
    }

    // ---- matrix helpers (raylib convention)
    public static Matrix4x4 T(float x, float y, float z) => Raymath.MatrixTranslate(x, y, z);
    public static Matrix4x4 T(Vector3 p) => Raymath.MatrixTranslate(p.X, p.Y, p.Z);
    public static Matrix4x4 S(float x, float y, float z) => Raymath.MatrixScale(x, y, z);
    public static Matrix4x4 S(float s) => Raymath.MatrixScale(s, s, s);
    public static Matrix4x4 Rx(float a) => Raymath.MatrixRotateX(a);
    public static Matrix4x4 Ry(float a) => Raymath.MatrixRotateY(a);
    public static Matrix4x4 Rz(float a) => Raymath.MatrixRotateZ(a);
    public static Matrix4x4 I => Raymath.MatrixIdentity();
    /// <summary>a then b (a applied first).</summary>
    public static Matrix4x4 M(Matrix4x4 a, Matrix4x4 b) => Raymath.MatrixMultiply(a, b);
    public static Matrix4x4 M(params Matrix4x4[] ms) { var r = ms[0]; for (int i = 1; i < ms.Length; i++) r = Raymath.MatrixMultiply(r, ms[i]); return r; }
    public static Vector3 Apply(Matrix4x4 m, Vector3 p) => Raymath.Vector3Transform(p, m);

    /// <summary>Matrix that maps the unit Y axis (0..1) onto the segment a→b with radius scale r.</summary>
    public static Matrix4x4 Seg(Vector3 a, Vector3 b, float rx, float rz)
    {
        var d = b - a; float len = d.Length();
        if (len < 1e-5f) return M(S(rx, 0.0001f, rz), T(a));
        var y = d / len;
        var helper = MathF.Abs(y.Y) < 0.95f ? Vector3.UnitY : Vector3.UnitX;
        var x = Vector3.Normalize(Vector3.Cross(helper, y));
        var z = Vector3.Cross(x, y);
        // raylib Matrix4x4: columns are stored as M11..M41? Build via Raymath-compatible layout.
        var rot = new Matrix4x4(
            x.X, y.X, z.X, 0,
            x.Y, y.Y, z.Y, 0,
            x.Z, y.Z, z.Z, 0,
            0, 0, 0, 1);
        return M(S(rx, len, rz), rot, T(a));
    }

    /// <summary>Basis matrix from axes (columns).</summary>
    public static Matrix4x4 Basis(Vector3 x, Vector3 y, Vector3 z) => new(
        x.X, y.X, z.X, 0,
        x.Y, y.Y, z.Y, 0,
        x.Z, y.Z, z.Z, 0,
        0, 0, 0, 1);
}

/// <summary>Builds meshes procedurally. Triangles are auto-oriented to face the supplied outward normal.</summary>
public sealed class MeshBuilder
{
    readonly List<Vector3> _p = new(), _n = new();
    readonly List<Vector2> _uv = new();
    readonly List<Color> _c = new();
    readonly List<ushort> _i = new();
    public Color Col = Color.White;
    public Matrix4x4 Xf = Matrix4x4.Identity;
    bool _hasXf;

    public MeshBuilder With(Matrix4x4 xf) { Xf = xf; _hasXf = true; return this; }
    public MeshBuilder NoXf() { _hasXf = false; return this; }

    int V(Vector3 p, Vector3 n, Vector2 uv)
    {
        if (_hasXf)
        {
            p = R3.Apply(Xf, p);
            var o = R3.Apply(Xf, Vector3.Zero);
            n = Vector3.Normalize(R3.Apply(Xf, n) - o);
        }
        _p.Add(p); _n.Add(n); _uv.Add(uv); _c.Add(Col);
        return _p.Count - 1;
    }

    void Tri(int a, int b, int c, Vector3 outward)
    {
        // use the (already transformed) vertex normals so orientation survives With(...) rotations
        var nsum = _n[a] + _n[b] + _n[c];
        if (nsum.LengthSquared() > 1e-6f) outward = nsum;
        var cr = Vector3.Cross(_p[b] - _p[a], _p[c] - _p[a]);
        if (Vector3.Dot(cr, outward) < 0) (b, c) = (c, b);
        _i.Add((ushort)a); _i.Add((ushort)b); _i.Add((ushort)c);
    }

    /// <summary>Surface of revolution around Y. profile = (radius, y) bottom→top. Hard edges where the profile turns sharply.</summary>
    public MeshBuilder Lathe(IList<Vector2> prof, int seg = 24, float sx = 1, float sz = 1, float vScale = 1)
    {
        int nP = prof.Count;
        var segN = new Vector2[nP - 1];
        for (int k = 0; k < nP - 1; k++)
        {
            var d = prof[k + 1] - prof[k];
            var nn = new Vector2(d.Y, -d.X);
            segN[k] = nn.LengthSquared() > 0 ? Vector2.Normalize(nn) : new Vector2(1, 0);
        }
        float vAcc = 0;
        for (int k = 0; k < nP - 1; k++)
        {
            Vector2 n0 = segN[k], n1 = segN[k];
            if (k > 0 && Vector2.Dot(segN[k - 1], segN[k]) > 0.75f) n0 = Vector2.Normalize(segN[k - 1] + segN[k]);
            if (k < nP - 2 && Vector2.Dot(segN[k + 1], segN[k]) > 0.75f) n1 = Vector2.Normalize(segN[k + 1] + segN[k]);
            float segLen = (prof[k + 1] - prof[k]).Length();
            int baseIdx = _p.Count;
            for (int s = 0; s <= seg; s++)
            {
                float a = s / (float)seg * MathF.Tau;
                float ca = MathF.Cos(a), sa = MathF.Sin(a);
                for (int e = 0; e < 2; e++)
                {
                    var pp = prof[k + e]; var nn = e == 0 ? n0 : n1;
                    var pos = new Vector3(pp.X * ca * sx, pp.Y, pp.X * sa * sz);
                    var nor = Vector3.Normalize(new Vector3(nn.X * ca / sx, nn.Y, nn.X * sa / sz));
                    V(pos, nor, new Vector2(s / (float)seg, (vAcc + (e == 0 ? 0 : segLen)) * vScale));
                }
            }
            for (int s = 0; s < seg; s++)
            {
                int a0 = baseIdx + s * 2, a1 = a0 + 1, b0 = a0 + 2, b1 = a0 + 3;
                var mid = (_p[a0] + _p[b1]) / 2;
                var outward = (_n[a0] + _n[b1]);
                if (outward.LengthSquared() < 1e-6f) outward = mid;
                Tri(a0, b0, b1, outward); Tri(a0, b1, a1, outward);
            }
            vAcc += segLen;
        }
        return this;
    }

    public MeshBuilder Box(Vector3 c, Vector3 size)
    {
        var h = size / 2;
        Vector3[] ns = { Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitZ };
        foreach (var n in ns)
        {
            var u = MathF.Abs(n.Y) > 0.5f ? Vector3.UnitX : Vector3.UnitY;
            var v = Vector3.Cross(n, u);
            var fc = c + n * new Vector3(h.X, h.Y, h.Z) * new Vector3(MathF.Abs(n.X) > 0 ? 1 : 0, MathF.Abs(n.Y) > 0 ? 1 : 0, MathF.Abs(n.Z) > 0 ? 1 : 0);
            var hu = u * h; var hv = v * h;
            hu = new Vector3(u.X * h.X, u.Y * h.Y, u.Z * h.Z); hv = new Vector3(v.X * h.X, v.Y * h.Y, v.Z * h.Z);
            int a = V(fc - hu - hv, n, new(0, 0)), b = V(fc + hu - hv, n, new(1, 0)), cc = V(fc + hu + hv, n, new(1, 1)), d = V(fc - hu + hv, n, new(0, 1));
            Tri(a, b, cc, n); Tri(a, cc, d, n);
        }
        return this;
    }

    /// <summary>Extrude a 2D outline (XY) between z0..z1 with an optional bevel ring on the front face.</summary>
    public MeshBuilder Extrude(IList<Vector2> poly, float z0, float z1, float bevel = 0)
    {
        var pts = poly.ToList();
        if (SignedArea(pts) < 0) pts.Reverse();
        var tris = EarClip(pts);
        float minX = pts.Min(p => p.X), maxX = pts.Max(p => p.X), minY = pts.Min(p => p.Y), maxY = pts.Max(p => p.Y);
        Vector2 UV(Vector2 p) => new((p.X - minX) / (maxX - minX), 1 - (p.Y - minY) / (maxY - minY));
        var inner = bevel > 0 ? Inset(pts, bevel) : pts;
        float zf = z1 + (bevel > 0 ? bevel * 0.6f : 0);
        // front + back caps
        foreach (var (a, b, c) in tris)
        {
            int fa = V(new(inner[a], zf), Vector3.UnitZ, UV(pts[a])), fb = V(new(inner[b], zf), Vector3.UnitZ, UV(pts[b])), fc = V(new(inner[c], zf), Vector3.UnitZ, UV(pts[c]));
            Tri(fa, fb, fc, Vector3.UnitZ);
            int ba = V(new(pts[a], z0), -Vector3.UnitZ, UV(pts[a])), bb = V(new(pts[b], z0), -Vector3.UnitZ, UV(pts[b])), bc = V(new(pts[c], z0), -Vector3.UnitZ, UV(pts[c]));
            Tri(ba, bb, bc, -Vector3.UnitZ);
        }
        // sides (+ bevel band)
        int n = pts.Count;
        for (int i = 0; i < n; i++)
        {
            var p0 = pts[i]; var p1 = pts[(i + 1) % n];
            var e = p1 - p0; var nn2 = Vector2.Normalize(new Vector2(e.Y, -e.X));
            var nrm = new Vector3(nn2, 0);
            int a = V(new(p0, z0), nrm, new(0, 0)), b = V(new(p1, z0), nrm, new(1, 0)), c = V(new(p1, z1), nrm, new(1, 1)), d = V(new(p0, z1), nrm, new(0, 1));
            Tri(a, b, c, nrm); Tri(a, c, d, nrm);
            if (bevel > 0)
            {
                var q0 = inner[i]; var q1 = inner[(i + 1) % n];
                var bn = Vector3.Normalize(nrm + Vector3.UnitZ);
                int e0 = V(new(p0, z1), bn, new(0, 0)), e1 = V(new(p1, z1), bn, new(1, 0)), e2 = V(new(q1, zf), bn, new(1, 1)), e3 = V(new(q0, zf), bn, new(0, 1));
                Tri(e0, e1, e2, bn); Tri(e0, e2, e3, bn);
            }
        }
        return this;
    }

    static List<Vector2> Inset(List<Vector2> pts, float d)
    {
        int n = pts.Count; var r = new List<Vector2>(n);
        for (int i = 0; i < n; i++)
        {
            var a = pts[(i + n - 1) % n]; var b = pts[i]; var c = pts[(i + 1) % n];
            var n1 = Vector2.Normalize(new Vector2((b - a).Y, -(b - a).X));
            var n2 = Vector2.Normalize(new Vector2((c - b).Y, -(c - b).X));
            var m = n1 + n2; if (m.LengthSquared() < 1e-6f) m = n1;
            m = Vector2.Normalize(m);
            float k = Math.Clamp(1 / MathF.Max(0.3f, Vector2.Dot(m, n1)), 1, 3);
            r.Add(b - m * d * k);   // outward normal is (dy,-dx) for CCW, so subtract to go inward
        }
        return r;
    }

    static float SignedArea(List<Vector2> p) { float a = 0; for (int i = 0; i < p.Count; i++) { var q = p[i]; var r = p[(i + 1) % p.Count]; a += q.X * r.Y - r.X * q.Y; } return a / 2; }

    static List<(int, int, int)> EarClip(List<Vector2> p)
    {
        var idx = Enumerable.Range(0, p.Count).ToList(); var res = new List<(int, int, int)>();
        int guard = 0;
        while (idx.Count > 3 && guard++ < 10000)
        {
            bool cut = false;
            for (int i = 0; i < idx.Count; i++)
            {
                int a = idx[(i + idx.Count - 1) % idx.Count], b = idx[i], c = idx[(i + 1) % idx.Count];
                var ab = p[b] - p[a]; var ac = p[c] - p[a];
                if (ab.X * ac.Y - ab.Y * ac.X <= 1e-7f) continue;
                bool inside = false;
                foreach (var j in idx)
                {
                    if (j == a || j == b || j == c) continue;
                    if (InTri(p[j], p[a], p[b], p[c])) { inside = true; break; }
                }
                if (inside) continue;
                res.Add((a, b, c)); idx.RemoveAt(i); cut = true; break;
            }
            if (!cut) break;
        }
        if (idx.Count == 3) res.Add((idx[0], idx[1], idx[2]));
        return res;
    }

    static bool InTri(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float d1 = (p.X - b.X) * (a.Y - b.Y) - (a.X - b.X) * (p.Y - b.Y);
        float d2 = (p.X - c.X) * (b.Y - c.Y) - (b.X - c.X) * (p.Y - c.Y);
        float d3 = (p.X - a.X) * (c.Y - a.Y) - (c.X - a.X) * (p.Y - a.Y);
        bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
        return !(neg && pos);
    }

    /// <summary>Flat quad in XZ plane (y=0) facing +Y, with UV tiling.</summary>
    public MeshBuilder PlaneXZ(float w, float l, float uRep, float vRep)
    {
        int a = V(new(-w / 2, 0, 0), Vector3.UnitY, new(0, 0)), b = V(new(w / 2, 0, 0), Vector3.UnitY, new(uRep, 0)),
            c = V(new(w / 2, 0, -l), Vector3.UnitY, new(uRep, vRep)), d = V(new(-w / 2, 0, -l), Vector3.UnitY, new(0, vRep));
        Tri(a, b, c, Vector3.UnitY); Tri(a, c, d, Vector3.UnitY);
        return this;
    }

    public static IList<Vector2> Circle(float r, int n, float sx = 1, float sy = 1, float phase = 0)
    {
        var l = new List<Vector2>(); for (int i = 0; i < n; i++) { float a = phase + i / (float)n * MathF.Tau; l.Add(new(MathF.Cos(a) * r * sx, MathF.Sin(a) * r * sy)); }
        return l;
    }

    public unsafe Mesh Build()
    {
        var m = new Mesh { VertexCount = _p.Count, TriangleCount = _i.Count / 3 };
        m.AllocVertices(); m.AllocNormals(); m.AllocTexCoords(); m.AllocColors(); m.AllocIndices();
        var vs = m.VerticesAs<Vector3>(); var ns = m.NormalsAs<Vector3>(); var ts = m.TexCoordsAs<Vector2>(); var cs = m.ColorsAs<Color>(); var ids = m.IndicesAs<ushort>();
        for (int i = 0; i < _p.Count; i++) { vs[i] = _p[i]; ns[i] = _n[i]; ts[i] = _uv[i]; cs[i] = _c[i]; }
        for (int i = 0; i < _i.Count; i++) ids[i] = _i[i];
        Raylib.UploadMesh(ref m, false);
        return m;
    }

    public int VertexCount => _p.Count;
}

/// <summary>Procedural textures (generated at startup, nothing taken from the disc).</summary>
public static unsafe class Tex
{
    public static Texture2D Make(int w, int h, Func<int, int, Color> f, bool mip = true, bool repeat = true)
    {
        var img = Raylib.GenImageColor(w, h, Color.Black);
        var px = (Color*)img.Data;
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) px[y * w + x] = f(x, y);
        var t = Raylib.LoadTextureFromImage(img);
        Raylib.UnloadImage(img);
        if (mip) { Raylib.GenTextureMipmaps(ref t); Raylib.SetTextureFilter(t, TextureFilter.Trilinear); }
        else Raylib.SetTextureFilter(t, TextureFilter.Bilinear);
        Raylib.SetTextureWrap(t, repeat ? TextureWrap.Repeat : TextureWrap.Clamp);
        return t;
    }

    // value noise
    static float Hash(int x, int y, int s) { unchecked { int h = x * 374761393 + y * 668265263 + s * 982451653; h = (h ^ (h >> 13)) * 1274126177; return ((h ^ (h >> 16)) & 0xFFFF) / 65535f; } }
    public static float Noise(float x, float y, int s, int wrapX = 0, int wrapY = 0)
    {
        int x0 = (int)MathF.Floor(x), y0 = (int)MathF.Floor(y); float fx = x - x0, fy = y - y0;
        int X(int v) => wrapX > 0 ? ((v % wrapX) + wrapX) % wrapX : v;
        int Y(int v) => wrapY > 0 ? ((v % wrapY) + wrapY) % wrapY : v;
        float a = Hash(X(x0), Y(y0), s), b = Hash(X(x0 + 1), Y(y0), s), c = Hash(X(x0), Y(y0 + 1), s), d = Hash(X(x0 + 1), Y(y0 + 1), s);
        fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
        return a + (b - a) * fx + (c - a) * fy + (a - b - c + d) * fx * fy;
    }
    public static float Fbm(float x, float y, int s, int oct = 4, int wrapX = 0, int wrapY = 0)
    {
        float v = 0, amp = 0.5f, f = 1;
        for (int i = 0; i < oct; i++) { v += Noise(x * f, y * f, s + i, wrapX * (int)f, wrapY * (int)f) * amp; amp *= 0.5f; f *= 2; }
        return v;
    }
    public static Color C(float r, float g, float b, float a = 1) => new((int)Math.Clamp(r * 255, 0, 255), (int)Math.Clamp(g * 255, 0, 255), (int)Math.Clamp(b * 255, 0, 255), (int)Math.Clamp(a * 255, 0, 255));
}
