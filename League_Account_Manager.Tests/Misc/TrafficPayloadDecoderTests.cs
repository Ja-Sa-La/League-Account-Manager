using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text;
using League_Account_Manager.Misc;

namespace League_Account_Manager.Tests.Misc;

[TestClass]
public class TrafficPayloadDecoderTests
{
    [TestMethod]
    public void Decode_UsesDeclaredCharset()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var encoding = Encoding.GetEncoding("windows-1252");
        var headers = new ByteArrayContent([]).Headers;
        headers.ContentType = MediaTypeHeaderValue.Parse("text/plain; charset=windows-1252");

        var decoded = TrafficPayloadDecoder.Decode(encoding.GetBytes("café"), headers);

        Assert.AreEqual("café", decoded);
    }

    [TestMethod]
    public void Decode_DecompressesGzipPayload()
    {
        var source = Encoding.UTF8.GetBytes("{\"message\":\"decoded\"}");
        using var compressed = new MemoryStream();
        using (var gzip = new GZipStream(compressed, CompressionMode.Compress, true))
            gzip.Write(source);
        var headers = new ByteArrayContent([]).Headers;
        headers.ContentEncoding.Add("gzip");
        headers.ContentType = MediaTypeHeaderValue.Parse("application/json; charset=utf-8");

        var decoded = TrafficPayloadDecoder.Decode(compressed.ToArray(), headers);

        Assert.AreEqual("{\"message\":\"decoded\"}", decoded);
    }

    [TestMethod]
    public void Decode_UsesBase64ForBinaryPayload()
    {
        byte[] source = [0xff, 0xfe, 0xfd];

        var decoded = TrafficPayloadDecoder.Decode(source);

        StringAssert.StartsWith(decoded, "[Binary: 3 bytes]");
        StringAssert.Contains(decoded, Convert.ToBase64String(source));
    }
}