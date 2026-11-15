using System.Text;

namespace Riffstorm;

public sealed class DtbArray
{
    public readonly List<object> Items = new();
    public int Count => Items.Count;
    public object this[int i] => Items[i];
    public string Sym(int i) => Items[i] switch { string s => s, DtbString d => d.V, _ => null };
    public DtbArray Arr(int i) => Items[i] as DtbArray;
    public float Num(int i) => Items[i] switch { int v => v, float f => f, _ => 0 };

    /// <summary>Finds child array whose first element is the symbol <paramref name="key"/>.</summary>
    public DtbArray Find(string key)
    {
        foreach (var o in Items)
            if (o is DtbArray a && a.Count > 0 && a[0] is string s && s == key) return a;
        return null;
    }
    public IEnumerable<DtbArray> Arrays() { foreach (var o in Items) if (o is DtbArray a) yield return a; }
}

public sealed class DtbString { public string V; public override string ToString() => V; }

/// <summary>Harmonix DTB (encrypted config script). Cipher recovered from SLUS_212.24 (0x14b028 init / 0x14b140 next).</summary>
public static class Dtb
{
    public static byte[] Decrypt(byte[] d)
    {
        uint s = BitConverter.ToUInt32(d, 0);
        var t = new uint[256];
        for (int i = 0; i < 256; i++)
        {
            s = s * 0x41C64E6D + 12345; uint hi = s >> 16;
            s = s * 0x41C64E6D + 12345; t[i] = hi | (s & 0x7FFF0000);
        }
        int i1 = 0, i2 = 103;
        var o = new byte[d.Length - 4];
        for (int i = 0; i < o.Length; i++)
        {
            uint r = t[i1] ^ t[i2]; t[i1] = r;
            i1 = (i1 + 1) % 249; i2 = (i2 + 1) % 249;
            o[i] = (byte)(d[i + 4] ^ r);
        }
        return o;
    }

    public static DtbArray Load(byte[] encrypted)
    {
        var d = Decrypt(encrypted);
        int p = 1;
        return ReadArr(d, ref p);
    }

    static DtbArray ReadArr(byte[] d, ref int p)
    {
        int cnt = BitConverter.ToUInt16(d, p); p += 6;
        var a = new DtbArray();
        for (int i = 0; i < cnt; i++)
        {
            uint t = BitConverter.ToUInt32(d, p); p += 4;
            switch (t)
            {
                case 0: a.Items.Add(BitConverter.ToInt32(d, p)); p += 4; break;
                case 1: a.Items.Add(BitConverter.ToSingle(d, p)); p += 4; break;
                case 0x10: case 0x11: case 0x13: a.Items.Add(ReadArr(d, ref p)); break;
                default:
                    int n = BitConverter.ToInt32(d, p); p += 4;
                    string s = Encoding.Latin1.GetString(d, p, n); p += n;
                    a.Items.Add(t == 0x12 ? new DtbString { V = s } : s);
                    break;
            }
        }
        return a;
    }
}
