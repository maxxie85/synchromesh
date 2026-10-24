using System.Globalization;

namespace Synchro.Core.Servers;

/// <summary>
/// Checks a server address as typed: a host name, an IPv4 address or an IPv6 address, each by its RFC.
/// <list type="bullet">
/// <item>IPv4 (RFC 3986 §3.2.2 "IPv4address"): exactly four decimal numbers 0–255 without leading zeros. Stricter
/// than <c>IPAddress.TryParse</c>, which also takes inet_aton's shorthand (<c>1.2.3</c>, <c>0x7f.1</c>, <c>010.0.0.1</c>).</item>
/// <item>IPv6 (RFC 4291 §2.2): eight groups of 1–4 hex digits, at most one <c>::</c>, optionally ending in an IPv4
/// address; optionally in brackets (RFC 3986 "IP-literal") and with a zone such as <c>%en0</c> (RFC 4007 §11,
/// RFC 6874).</item>
/// <item>Host names (RFC 1123 §2.1 on RFC 952, lengths from RFC 1035 §2.3.4): dot-separated labels of letters, digits
/// and hyphens, 1–63 characters, not starting or ending with a hyphen, at most 253 characters, an optional final
/// dot. The last label can't be all digits (RFC 3696 §2), so a mistyped IPv4 address is never taken for a name.
/// Internationalised names (RFC 5890) are checked in their ASCII form.</item>
/// </list>
/// </summary>
public static class HostAddress
{
    private const int MaxNameLength = 253;
    private const int MaxLabelLength = 63;

    private static readonly IdnMapping Idn = new();

    /// <summary>A plain-language problem with <paramref name="text"/>, or null when it is a valid address (or empty).</summary>
    public static string? Validate(string? text)
    {
        var host = text?.Trim() ?? "";
        if (host.Length == 0) return null;

        if (host.Contains("://", StringComparison.Ordinal) || host.Contains('/'))
            return "Enter only the host name or IP address, without http:// or a path.";
        if (host.Any(char.IsWhiteSpace))
            return "An address can't contain spaces.";

        if (host.StartsWith('[') || host.Contains(':'))
            return ValidateIPv6(host);

        var parts = host.Split('.');
        if (parts.All(IsDigits))
            return ValidateIPv4(parts);

        return ValidateName(host);
    }

    public static bool IsValid(string? text) => !string.IsNullOrWhiteSpace(text) && Validate(text) is null;

    private static string? ValidateIPv4(string[] parts)
    {
        const string Example = "An IPv4 address has four numbers from 0 to 255, separated by dots, like 192.168.1.20.";
        if (parts.Length != 4) return Example;

        foreach (var part in parts)
        {
            if (part.Length > 1 && part[0] == '0')
                return $"Write {part} without leading zeros. " + Example;
            if (part.Length > 3 || int.Parse(part, CultureInfo.InvariantCulture) > 255)
                return $"{part} is too large for an IPv4 address. " + Example;
        }

        return null;
    }

    private static bool IsIPv4(string text)
    {
        var parts = text.Split('.');
        return parts.All(IsDigits) && ValidateIPv4(parts) is null;
    }

    private static string? ValidateIPv6(string host)
    {
        const string Example = "That isn't a valid IPv6 address. Use a form like fd00::1, or fe80::1%en0 with a zone.";

        var address = host;
        if (address.StartsWith('['))
        {
            if (!address.EndsWith(']')) return Example;
            address = address[1..^1];
        }
        else if (address.Count(c => c == ':') == 1 && IsDigits(address[(address.IndexOf(':') + 1)..]))
        {
            // nas.local:9091 or 192.168.1.20:9091
            return "Enter the port in the Port field, not after the address.";
        }

        var zoneStart = address.IndexOf('%');
        if (zoneStart >= 0)
        {
            var zone = address[(zoneStart + 1)..];
            if (zone.Length == 0 || !zone.All(IsZoneChar)) return Example;
            address = address[..zoneStart];
        }

        return IsIPv6(address) ? null : Example;
    }

    // RFC 4291 §2.2: x:x:x:x:x:x:x:x, "::" for one run of zero groups, and a final IPv4 address counting as two groups.
    private static bool IsIPv6(string address)
    {
        if (address.Length == 0) return false;

        var compressed = address.IndexOf("::", StringComparison.Ordinal);
        if (compressed >= 0 && address.IndexOf("::", compressed + 1, StringComparison.Ordinal) >= 0) return false;

        var head = compressed >= 0 ? address[..compressed] : address;
        var tail = compressed >= 0 ? address[(compressed + 2)..] : "";
        var headGroups = head.Length == 0 ? [] : head.Split(':');
        var tailGroups = tail.Length == 0 ? [] : tail.Split(':');

        var all = headGroups.Concat(tailGroups).ToArray();
        var count = 0;
        for (var i = 0; i < all.Length; i++)
        {
            var group = all[i];
            if (i == all.Length - 1 && group.Contains('.'))
            {
                if (!IsIPv4(group)) return false;
                count += 2;
            }
            else if (group.Length is >= 1 and <= 4 && group.All(char.IsAsciiHexDigit))
            {
                count++;
            }
            else
            {
                return false;
            }
        }

        return compressed >= 0 ? count <= 7 : count == 8;
    }

    private static string? ValidateName(string host)
    {
        string ascii;
        try
        {
            ascii = host.All(char.IsAscii) ? host : Idn.GetAscii(host);
        }
        catch (ArgumentException)
        {
            return "That isn't a valid host name.";
        }

        var name = ascii.EndsWith('.') ? ascii[..^1] : ascii;
        if (name.Length > MaxNameLength)
            return $"A host name can be at most {MaxNameLength} characters.";

        var labels = name.Split('.');
        foreach (var label in labels)
        {
            if (label.Length == 0)
                return "The address has an empty part between dots.";
            if (label.Length > MaxLabelLength)
                return $"Each part of a host name can be at most {MaxLabelLength} characters.";
            if (label.FirstOrDefault(c => !char.IsAsciiLetterOrDigit(c) && c != '-') is var bad and not '\0')
                return $"A host name can only contain letters, digits, hyphens and dots; \"{bad}\" isn't allowed.";
            if (label.StartsWith('-') || label.EndsWith('-'))
                return $"A part of a host name can't start or end with a hyphen (\"{label}\").";
        }

        if (IsDigits(labels[^1]))
            return "The last part of a host name can't be only digits. An IP address needs four numbers, like 192.168.1.20.";

        return null;
    }

    private static bool IsDigits(string text) => text.Length > 0 && text.All(char.IsAsciiDigit);

    // RFC 6874: a zone in a URI is made of unreserved characters.
    private static bool IsZoneChar(char c) => char.IsAsciiLetterOrDigit(c) || c is '-' or '.' or '_' or '~';
}
