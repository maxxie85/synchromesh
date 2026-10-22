using System.Text;

namespace Synchro.Tests;

/// <summary>Builds small .torrent files in memory: a Bencode encoder for test data.</summary>
internal static class TorrentBuilder
{
    public static byte[] Encode(object value)
    {
        using var stream = new MemoryStream();
        Write(stream, value);
        return stream.ToArray();
    }

    /// <summary>A v1 multi-file torrent. Each file is (path segments, length).</summary>
    public static byte[] MultiFile(string name, params (string[] Path, long Length)[] files) =>
        Encode(new Dictionary<string, object>
        {
            ["announce"] = "udp://tracker.example:6969/announce",
            ["info"] = new Dictionary<string, object>
            {
                ["name"] = name,
                ["piece length"] = 16384L,
                ["pieces"] = new byte[20],
                ["files"] = files.Select(f => (object)new Dictionary<string, object>
                {
                    ["length"] = f.Length,
                    ["path"] = f.Path.Cast<object>().ToList(),
                }).ToList(),
            },
        });

    public static byte[] SingleFile(string name, long length) =>
        Encode(new Dictionary<string, object>
        {
            ["info"] = new Dictionary<string, object>
            {
                ["name"] = name, ["length"] = length, ["piece length"] = 16384L, ["pieces"] = new byte[20],
            },
        });

    private static void Write(Stream s, object value)
    {
        switch (value)
        {
            case long n: Ascii(s, $"i{n}e"); break;
            case int n: Ascii(s, $"i{n}e"); break;
            case string text: Bytes(s, Encoding.UTF8.GetBytes(text)); break;
            case byte[] bytes: Bytes(s, bytes); break;
            case IDictionary<string, object> dict:
                Ascii(s, "d");
                foreach (var (key, item) in dict.OrderBy(p => p.Key, StringComparer.Ordinal))
                {
                    Bytes(s, Encoding.UTF8.GetBytes(key));
                    Write(s, item);
                }
                Ascii(s, "e");
                break;
            case System.Collections.IEnumerable list:
                Ascii(s, "l");
                foreach (var item in list) Write(s, item);
                Ascii(s, "e");
                break;
            default: throw new ArgumentException($"Can't bencode {value.GetType()}");
        }
    }

    private static void Bytes(Stream s, byte[] bytes)
    {
        Ascii(s, $"{bytes.Length}:");
        s.Write(bytes);
    }

    private static void Ascii(Stream s, string text) => s.Write(Encoding.ASCII.GetBytes(text));
}
