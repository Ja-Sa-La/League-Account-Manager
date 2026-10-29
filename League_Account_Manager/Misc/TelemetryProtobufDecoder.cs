using System.IO;
using System.Text;
using ProtoBuf;

namespace League_Account_Manager.Misc;

/// <summary>
///     Turns the OpenTelemetry protobuf bodies the Riot client posts to its telemetry hosts into
///     readable text. The collector services (logs, metrics, traces) are decoded with their real
///     schema; anything else falls back to a schema-less field dump so an unknown message still
///     shows its contents instead of raw bytes.
/// </summary>
internal static class TelemetryProtobufDecoder
{
    internal static bool IsProtobuf(string? contentType) =>
        contentType is not null && contentType.Contains("protobuf", StringComparison.OrdinalIgnoreCase);

    internal static string? TryDecode(byte[] body, string path)
    {
        if (body.Length == 0)
            return null;

        try
        {
            if (path.Contains("/v1/logs", StringComparison.OrdinalIgnoreCase))
                return Format(Serializer.Deserialize<ExportLogsServiceRequest>(new MemoryStream(body)));
            if (path.Contains("/v1/metrics", StringComparison.OrdinalIgnoreCase))
                return Format(Serializer.Deserialize<ExportMetricsServiceRequest>(new MemoryStream(body)));
            if (path.Contains("/v1/traces", StringComparison.OrdinalIgnoreCase))
                return Format(Serializer.Deserialize<ExportTraceServiceRequest>(new MemoryStream(body)));
        }
        catch (ProtoException)
        {
        }

        return DumpFields(body);
    }

    private static string Format(ExportLogsServiceRequest request)
    {
        var text = new StringBuilder();
        foreach (var resource in request.ResourceLogs)
        {
            text.AppendLine("[resource]");
            AppendAttributes(text, resource.Resource?.Attributes, "  ");
            foreach (var scope in resource.ScopeLogs)
            {
                text.Append("  [scope] ").AppendLine(scope.Scope?.Name);
                foreach (var record in scope.LogRecords)
                {
                    text.Append("    ").Append(UnixTime(record.TimeUnixNano)).Append(' ')
                        .Append(record.SeverityText);
                    if (!string.IsNullOrEmpty(record.Body?.StringValue))
                        text.Append("  ").Append(record.Body.StringValue);
                    text.AppendLine();
                    AppendAttributes(text, record.Attributes, "      ");
                }
            }
        }

        return text.ToString().TrimEnd();
    }

    private static string Format(ExportMetricsServiceRequest request)
    {
        var text = new StringBuilder();
        foreach (var resource in request.ResourceMetrics)
        {
            text.AppendLine("[resource]");
            AppendAttributes(text, resource.Resource?.Attributes, "  ");
            foreach (var scope in resource.ScopeMetrics)
            {
                text.Append("  [scope] ").AppendLine(scope.Scope?.Name);
                foreach (var metric in scope.Metrics)
                {
                    text.Append("    ").Append(metric.Name);
                    if (!string.IsNullOrEmpty(metric.Unit))
                        text.Append(" (").Append(metric.Unit).Append(')');
                    text.AppendLine();
                    foreach (var point in metric.Gauge?.DataPoints ?? [])
                        text.Append("      gauge ").AppendLine(Number(point));
                    foreach (var point in metric.Sum?.DataPoints ?? [])
                        text.Append("      sum ").AppendLine(Number(point));
                }
            }
        }

        return text.ToString().TrimEnd();
    }

    private static string Format(ExportTraceServiceRequest request)
    {
        var text = new StringBuilder();
        foreach (var resource in request.ResourceSpans)
        {
            text.AppendLine("[resource]");
            AppendAttributes(text, resource.Resource?.Attributes, "  ");
            foreach (var scope in resource.ScopeSpans)
                foreach (var span in scope.Spans)
                {
                    text.Append("  ").Append(span.Name).Append("  trace=")
                        .Append(Convert.ToHexString(span.TraceId ?? [])).AppendLine();
                    AppendAttributes(text, span.Attributes, "    ");
                }
        }

        return text.ToString().TrimEnd();
    }

    private static void AppendAttributes(StringBuilder text, List<KeyValue>? attributes, string indent)
    {
        if (attributes is null)
            return;

        foreach (var attribute in attributes)
            text.Append(indent).Append(attribute.Key).Append(": ").AppendLine(Value(attribute.Value));
    }

    private static string Value(AnyValue? value) => value switch
    {
        null => string.Empty,
        { StringValue: not null } => value.StringValue,
        { HasBool: true } => value.BoolValue.ToString(),
        { HasInt: true } => value.IntValue.ToString(),
        { HasDouble: true } => value.DoubleValue.ToString(),
        _ => string.Empty
    };

    private static string Number(NumberDataPoint point) =>
        point.HasAsInt ? point.AsInt.ToString() :
        point.HasAsDouble ? point.AsDouble.ToString() : "0";

    private static string UnixTime(ulong nanos)
    {
        if (nanos == 0)
            return "-";
        try
        {
            return DateTimeOffset.FromUnixTimeMilliseconds((long)(nanos / 1_000_000)).ToLocalTime()
                .ToString("HH:mm:ss.fff");
        }
        catch (ArgumentOutOfRangeException)
        {
            return nanos.ToString();
        }
    }

    /// <summary>Reads protobuf fields without a schema, so an unrecognised message is still readable.</summary>
    private static string? DumpFields(byte[] body)
    {
        var text = new StringBuilder();
        var state = ProtoReader.State.Create(new MemoryStream(body), null);
        try
        {
            int field;
            while ((field = state.ReadFieldHeader()) > 0)
            {
                text.Append("field ").Append(field).Append(" (").Append(state.WireType).Append("): ");
                switch (state.WireType)
                {
                    case WireType.Varint:
                        text.AppendLine(state.ReadInt64().ToString());
                        break;
                    case WireType.Fixed64:
                        text.AppendLine(state.ReadDouble().ToString());
                        break;
                    case WireType.Fixed32:
                        text.AppendLine(state.ReadSingle().ToString());
                        break;
                    case WireType.String:
                        var bytes = state.AppendBytes(null) ?? [];
                        text.AppendLine(LooksLikeText(bytes)
                            ? Encoding.UTF8.GetString(bytes)
                            : $"<{bytes.Length} bytes>");
                        break;
                    default:
                        state.SkipField();
                        text.AppendLine("(skipped)");
                        break;
                }
            }
        }
        catch (Exception ex) when (ex is ProtoException or EndOfStreamException or IOException)
        {
            return text.Length == 0 ? null : text.ToString().TrimEnd();
        }

        return text.Length == 0 ? null : text.ToString().TrimEnd();
    }

    private static bool LooksLikeText(byte[] bytes)
    {
        if (bytes.Length == 0)
            return false;
        foreach (var b in bytes)
            if (b < 0x09 || (b > 0x0D && b < 0x20))
                return false;
        return true;
    }

    [ProtoContract]
    private sealed class ExportLogsServiceRequest
    {
        [ProtoMember(1)] public List<ResourceLogs> ResourceLogs { get; } = [];
    }

    [ProtoContract]
    private sealed class ResourceLogs
    {
        [ProtoMember(1)] public Resource? Resource { get; set; }
        [ProtoMember(2)] public List<ScopeLogs> ScopeLogs { get; } = [];
    }

    [ProtoContract]
    private sealed class ScopeLogs
    {
        [ProtoMember(1)] public InstrumentationScope? Scope { get; set; }
        [ProtoMember(2)] public List<LogRecord> LogRecords { get; } = [];
    }

    [ProtoContract]
    private sealed class LogRecord
    {
        [ProtoMember(1)] public ulong TimeUnixNano { get; set; }
        [ProtoMember(2)] public string SeverityText { get; set; } = string.Empty;
        [ProtoMember(3)] public AnyValue? Body { get; set; }
        [ProtoMember(4)] public List<KeyValue> Attributes { get; } = [];
    }

    [ProtoContract]
    private sealed class ExportMetricsServiceRequest
    {
        [ProtoMember(1)] public List<ResourceMetrics> ResourceMetrics { get; } = [];
    }

    [ProtoContract]
    private sealed class ResourceMetrics
    {
        [ProtoMember(1)] public Resource? Resource { get; set; }
        [ProtoMember(2)] public List<ScopeMetrics> ScopeMetrics { get; } = [];
    }

    [ProtoContract]
    private sealed class ScopeMetrics
    {
        [ProtoMember(1)] public InstrumentationScope? Scope { get; set; }
        [ProtoMember(2)] public List<Metric> Metrics { get; } = [];
    }

    [ProtoContract]
    private sealed class Metric
    {
        [ProtoMember(1)] public string Name { get; set; } = string.Empty;
        [ProtoMember(3)] public string Unit { get; set; } = string.Empty;
        [ProtoMember(5)] public Gauge? Gauge { get; set; }
        [ProtoMember(7)] public Sum? Sum { get; set; }
    }

    [ProtoContract]
    private sealed class Gauge
    {
        [ProtoMember(1)] public List<NumberDataPoint> DataPoints { get; } = [];
    }

    [ProtoContract]
    private sealed class Sum
    {
        [ProtoMember(1)] public List<NumberDataPoint> DataPoints { get; } = [];
    }

    [ProtoContract]
    private sealed class NumberDataPoint
    {
        [ProtoMember(1)] public List<KeyValue> Attributes { get; } = [];
        [ProtoMember(4)]
        private long AsIntValue
        {
            get => AsInt;
            set { AsInt = value; HasAsInt = true; }
        }

        [ProtoMember(6)] public ulong TimeUnixNano { get; set; }

        [ProtoMember(7)]
        private double AsDoubleValue
        {
            get => AsDouble;
            set { AsDouble = value; HasAsDouble = true; }
        }

        internal long AsInt { get; private set; }
        internal double AsDouble { get; private set; }
        internal bool HasAsInt { get; private set; }
        internal bool HasAsDouble { get; private set; }
    }

    [ProtoContract]
    private sealed class ExportTraceServiceRequest
    {
        [ProtoMember(1)] public List<ResourceSpans> ResourceSpans { get; } = [];
    }

    [ProtoContract]
    private sealed class ResourceSpans
    {
        [ProtoMember(1)] public Resource? Resource { get; set; }
        [ProtoMember(2)] public List<ScopeSpans> ScopeSpans { get; } = [];
    }

    [ProtoContract]
    private sealed class ScopeSpans
    {
        [ProtoMember(1)] public InstrumentationScope? Scope { get; set; }
        [ProtoMember(2)] public List<Span> Spans { get; } = [];
    }

    [ProtoContract]
    private sealed class Span
    {
        [ProtoMember(1)] public byte[]? TraceId { get; set; }
        [ProtoMember(5)] public string Name { get; set; } = string.Empty;
        [ProtoMember(9)] public List<KeyValue> Attributes { get; } = [];
    }

    [ProtoContract]
    private sealed class Resource
    {
        [ProtoMember(1)] public List<KeyValue> Attributes { get; } = [];
    }

    [ProtoContract]
    private sealed class InstrumentationScope
    {
        [ProtoMember(1)] public string Name { get; set; } = string.Empty;
    }

    [ProtoContract]
    private sealed class KeyValue
    {
        [ProtoMember(1)] public string Key { get; set; } = string.Empty;
        [ProtoMember(2)] public AnyValue? Value { get; set; }
    }

    [ProtoContract]
    private sealed class AnyValue
    {
        [ProtoMember(1)] public string? StringValue { get; set; }

        [ProtoMember(2)]
        private bool BoolWire
        {
            get => BoolValue;
            set { BoolValue = value; HasBool = true; }
        }

        [ProtoMember(3)]
        private long IntWire
        {
            get => IntValue;
            set { IntValue = value; HasInt = true; }
        }

        [ProtoMember(4)]
        private double DoubleWire
        {
            get => DoubleValue;
            set { DoubleValue = value; HasDouble = true; }
        }

        internal bool BoolValue { get; private set; }
        internal long IntValue { get; private set; }
        internal double DoubleValue { get; private set; }
        internal bool HasBool { get; private set; }
        internal bool HasInt { get; private set; }
        internal bool HasDouble { get; private set; }
    }
}
