using System.Text;

namespace Synchro.Core.Metainfo;

/// <summary>
/// Minimal Bencode decoder (BEP 3) for reading .torrent files. Values become long, byte[], List&lt;object&gt; or
/// Dictionary&lt;string, object&gt; (keys decoded as UTF-8). Malformed input throws <see cref="FormatException"/>.
/// </summary>
internal static class Bencode
{
    private const int MaxDepth = 64; // real torrents nest a handful of levels; this only stops hostile input

    public static object Decode(ReadOnlySpan<byte> data)
    {
        var position = 0;
        var value = ReadValue(data, ref position, 0);
        if (position != data.Length)
            throw new FormatException("Unexpected data after the end of the torrent.");
        return value;
    }

    private static object ReadValue(ReadOnlySpan<byte> data, ref int position, int depth)
    {
        if (depth > MaxDepth) throw new FormatException("The torrent is nested too deeply.");
        if (position >= data.Length) throw new FormatException("The torrent ends unexpectedly.");

        switch (data[position])
        {
            case (byte)'i':
            {
                position++;
                var end = IndexOf(data, (byte)'e', position);
                var number = long.Parse(Encoding.ASCII.GetString(data[position..end]), System.Globalization.CultureInfo.InvariantCulture);
                position = end + 1;
                return number;
            }
            case (byte)'l':
            {
                position++;
                var list = new List<object>();
                while (Peek(data, position) != 'e')
                    list.Add(ReadValue(data, ref position, depth + 1));
                position++;
                return list;
            }
            case (byte)'d':
            {
                position++;
                var dict = new Dictionary<string, object>(StringComparer.Ordinal);
                while (Peek(data, position) != 'e')
                {
                    var key = Encoding.UTF8.GetString(ReadBytes(data, ref position));
                    dict[key] = ReadValue(data, ref position, depth + 1);
                }
                position++;
                return dict;
            }
            case >= (byte)'0' and <= (byte)'9':
                return ReadBytes(data, ref position);
            default:
                throw new FormatException("The torrent contains an unknown value type.");
        }
    }

    private static byte[] ReadBytes(ReadOnlySpan<byte> data, ref int position)
    {
        var colon = IndexOf(data, (byte)':', position);
        if (!int.TryParse(Encoding.ASCII.GetString(data[position..colon]), System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var length) || length > data.Length - colon - 1)
            throw new FormatException("The torrent contains a string with an invalid length.");

        var start = colon + 1;
        position = start + length;
        return data[start..position].ToArray();
    }

    private static char Peek(ReadOnlySpan<byte> data, int position) =>
        position < data.Length ? (char)data[position] : throw new FormatException("The torrent ends unexpectedly.");

    private static int IndexOf(ReadOnlySpan<byte> data, byte value, int from)
    {
        var index = data[from..].IndexOf(value);
        return index < 0 ? throw new FormatException("The torrent ends unexpectedly.") : from + index;
    }
}
