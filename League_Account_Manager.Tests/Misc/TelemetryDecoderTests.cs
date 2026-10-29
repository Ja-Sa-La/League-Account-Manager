using League_Account_Manager.Misc;
using ProtoBuf;

namespace League_Account_Manager.Tests.Misc;

[TestClass]
public sealed class TelemetryDecoderTests
{
    [TestMethod]
    public void TryDecode_ReadsLogBodyAndAttributes()
    {
        var body = Logs(new ResourceLogs
        {
            ScopeLogs =
            {
                new ScopeLogs
                {
                    LogRecords =
                    {
                        new LogRecord
                        {
                            SeverityText = "INFO",
                            Body = new AnyValue { StringValue = "client started" },
                            Attributes = { new KeyValue { Key = "region", Value = new AnyValue { StringValue = "EUW" } } }
                        }
                    }
                }
            }
        });

        var text = TelemetryProtobufDecoder.TryDecode(body, "/v1/logs");

        Assert.IsNotNull(text);
        StringAssert.Contains(text, "INFO");
        StringAssert.Contains(text, "client started");
        StringAssert.Contains(text, "region: EUW");
    }

    [TestMethod]
    public void TryDecode_ReadsMetricNameAndValue()
    {
        var body = Metrics(new ResourceMetrics
        {
            ScopeMetrics =
            {
                new ScopeMetrics
                {
                    Metrics =
                    {
                        new Metric
                        {
                            Name = "fps",
                            Unit = "1/s",
                            Gauge = new Gauge { DataPoints = { new NumberDataPoint { AsInt = 144 } } }
                        }
                    }
                }
            }
        });

        var text = TelemetryProtobufDecoder.TryDecode(body, "/v1/metrics");

        Assert.IsNotNull(text);
        StringAssert.Contains(text, "fps (1/s)");
        StringAssert.Contains(text, "gauge 144");
    }

    [TestMethod]
    public void TryDecode_DumpsUnknownMessageInsteadOfFailing()
    {
        using var stream = new MemoryStream();
        Serializer.Serialize(stream, new Unknown { Label = "hello", Count = 7 });

        var text = TelemetryProtobufDecoder.TryDecode(stream.ToArray(), "/v1/other");

        Assert.IsNotNull(text);
        StringAssert.Contains(text, "hello");
        StringAssert.Contains(text, "7");
    }

    private static byte[] Logs(ResourceLogs logs)
    {
        using var stream = new MemoryStream();
        Serializer.Serialize(stream, new ExportLogs { ResourceLogs = { logs } });
        return stream.ToArray();
    }

    private static byte[] Metrics(ResourceMetrics metrics)
    {
        using var stream = new MemoryStream();
        Serializer.Serialize(stream, new ExportMetrics { ResourceMetrics = { metrics } });
        return stream.ToArray();
    }

    [ProtoContract]
    private sealed class ExportLogs
    {
        [ProtoMember(1)] public List<ResourceLogs> ResourceLogs { get; } = [];
    }

    [ProtoContract]
    private sealed class ResourceLogs
    {
        [ProtoMember(2)] public List<ScopeLogs> ScopeLogs { get; } = [];
    }

    [ProtoContract]
    private sealed class ScopeLogs
    {
        [ProtoMember(2)] public List<LogRecord> LogRecords { get; } = [];
    }

    [ProtoContract]
    private sealed class LogRecord
    {
        [ProtoMember(2)] public string SeverityText { get; set; } = string.Empty;
        [ProtoMember(3)] public AnyValue? Body { get; set; }
        [ProtoMember(4)] public List<KeyValue> Attributes { get; } = [];
    }

    [ProtoContract]
    private sealed class ExportMetrics
    {
        [ProtoMember(1)] public List<ResourceMetrics> ResourceMetrics { get; } = [];
    }

    [ProtoContract]
    private sealed class ResourceMetrics
    {
        [ProtoMember(2)] public List<ScopeMetrics> ScopeMetrics { get; } = [];
    }

    [ProtoContract]
    private sealed class ScopeMetrics
    {
        [ProtoMember(2)] public List<Metric> Metrics { get; } = [];
    }

    [ProtoContract]
    private sealed class Metric
    {
        [ProtoMember(1)] public string Name { get; set; } = string.Empty;
        [ProtoMember(3)] public string Unit { get; set; } = string.Empty;
        [ProtoMember(5)] public Gauge? Gauge { get; set; }
    }

    [ProtoContract]
    private sealed class Gauge
    {
        [ProtoMember(1)] public List<NumberDataPoint> DataPoints { get; } = [];
    }

    [ProtoContract]
    private sealed class NumberDataPoint
    {
        [ProtoMember(4)] public long AsInt { get; set; }
    }

    [ProtoContract]
    private sealed class AnyValue
    {
        [ProtoMember(1)] public string? StringValue { get; set; }
    }

    [ProtoContract]
    private sealed class KeyValue
    {
        [ProtoMember(1)] public string Key { get; set; } = string.Empty;
        [ProtoMember(2)] public AnyValue? Value { get; set; }
    }

    [ProtoContract]
    private sealed class Unknown
    {
        [ProtoMember(1)] public string Label { get; set; } = string.Empty;
        [ProtoMember(2)] public int Count { get; set; }
    }
}
