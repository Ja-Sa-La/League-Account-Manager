using System.IO;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text;

namespace League_Account_Manager.Misc;

internal static class TrafficPayloadDecoder
{
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