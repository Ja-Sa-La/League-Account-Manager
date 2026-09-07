using System.IO;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace League_Account_Manager.Misc;

internal static class TrafficPayloadDecoder
{
    private static readonly Regex JwtRegex = new(
        "(?<![A-Za-z0-9_-])eyJ[A-Za-z0-9_-]+\\.[A-Za-z0-9_-]+\\.[A-Za-z0-9_-]+(?![A-Za-z0-9_-])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    internal static string Decode(byte[] bytes, HttpContentHeaders? headers = null)
    {
        if (bytes.Length == 0)
            return string.Empty;

        try
        {
            foreach (var encoding in (headers?.ContentEncoding ?? []).Reverse())
                bytes = Decompress(bytes, encoding);
        }
        catch (InvalidDataException)
        {
            return $"[Encoded binary: {bytes.Length} bytes]{Environment.NewLine}{Convert.ToBase64String(bytes)}";
        }

        var charset = headers?.ContentType?.CharSet?.Trim('"');
        if (!string.IsNullOrWhiteSpace(charset))
            try
            {
                return Encoding.GetEncoding(charset).GetString(bytes);
            }
            catch (ArgumentException)
            {
            }

        try
        {
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return $"[Binary: {bytes.Length} bytes]{Environment.NewLine}{Convert.ToBase64String(bytes)}";
        }
    }

    internal static string DecodeJwtPayloads(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        return JwtRegex.Replace(text, match =>
        {
            var token = match.Value;
            var parts = token.Split('.');
            try
            {
                var payload = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
                using var document = JsonDocument.Parse(payload);
                return $"{token}{Environment.NewLine}[JWT payload]{Environment.NewLine}"
                       + JsonSerializer.Serialize(document.RootElement,
                           new JsonSerializerOptions { WriteIndented = true });
            }
            catch (Exception ex) when (ex is FormatException or JsonException or DecoderFallbackException)
            {
                return token;
            }
        });
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
        return Convert.FromBase64String(base64);
    }

    private static byte[] Decompress(byte[] bytes, string encoding)
    {
        using var input = new MemoryStream(bytes);
        using Stream decompressor = encoding.ToLowerInvariant() switch
        {
            "gzip" or "x-gzip" => new GZipStream(input, CompressionMode.Decompress),
            "deflate" => new DeflateStream(input, CompressionMode.Decompress),
            "br" => new BrotliStream(input, CompressionMode.Decompress),
            "identity" => input,
            _ => throw new InvalidDataException($"Unsupported content encoding: {encoding}")
        };
        using var output = new MemoryStream();
        decompressor.CopyTo(output);
        return output.ToArray();
    }
}