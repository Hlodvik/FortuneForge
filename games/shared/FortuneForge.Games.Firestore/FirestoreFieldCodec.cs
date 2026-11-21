using System.Buffers;
using System.Collections;
using System.Text;
using System.Text.Json;
using Google.Cloud.Firestore;
using ProtoTimestamp = Google.Protobuf.WellKnownTypes.Timestamp;

namespace FortuneForge.Games.Firestore;

/// <summary>Freezes Firestore document values for a prepared operation. Transaction transforms must be created at commit.</summary>
public static class FirestoreFieldCodec
{
    private const int Version = 1;
    private const int MaximumNesting = 64;
    private const int MaximumJsonDepth = MaximumNesting * 2 + 4;

    public static string Serialize(IReadOnlyDictionary<string, object> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { MaxDepth = MaximumJsonDepth }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", Version);
            writer.WritePropertyName("fields");
            WriteMap(writer, fields, 0);
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    public static Dictionary<string, object> Deserialize(string json)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            MaxDepth = MaximumJsonDepth,
            AllowDuplicateProperties = false,
        });
        try
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 2 ||
                !root.TryGetProperty("version", out var version) || version.GetInt32() != Version ||
                !root.TryGetProperty("fields", out var fields))
                throw new JsonException("Unknown Firestore field encoding version.");
            return ReadMap(fields, 0);
        }
        catch (Exception error) when (error is InvalidOperationException or FormatException or ArgumentException or OverflowException)
        {
            throw new JsonException("Invalid Firestore field encoding.", error);
        }
    }

    private static void WriteMap(Utf8JsonWriter writer, IEnumerable<KeyValuePair<string, object>> fields, int depth)
    {
        writer.WriteStartObject();
        foreach (var (name, value) in fields.OrderBy(field => field.Key, StringComparer.Ordinal))
        {
            writer.WritePropertyName(name);
            WriteValue(writer, value, depth + 1);
        }
        writer.WriteEndObject();
    }

    private static void WriteValue(Utf8JsonWriter writer, object? value, int depth)
    {
        if (depth > MaximumNesting) throw new ArgumentException("The Firestore field nesting limit was exceeded.");

        // Objects represent maps. Tagged arrays represent scalars or a list, so arbitrary map keys
        // cannot be mistaken for a timestamp, numeric type or transaction transform.
        switch (value)
        {
            case null: writer.WriteNullValue(); break;
            case string text: writer.WriteStringValue(text); break;
            case bool flag: writer.WriteBooleanValue(flag); break;
            case byte number: WriteTagged(writer, "byte", number); break;
            case sbyte number: WriteTagged(writer, "sbyte", number); break;
            case short number: WriteTagged(writer, "short", number); break;
            case ushort number: WriteTagged(writer, "ushort", number); break;
            case int number: WriteTagged(writer, "int", number); break;
            case uint number: WriteTagged(writer, "uint", number); break;
            case long number: WriteTagged(writer, "long", number); break;
            case ulong number when number <= long.MaxValue: WriteTagged(writer, "ulong", number); break;
            case float number: WriteTagged(writer, "float", BitConverter.SingleToInt32Bits(number)); break;
            case double number: WriteTagged(writer, "double", BitConverter.DoubleToInt64Bits(number)); break;
            case byte[] bytes: WriteTagged(writer, "bytes", Convert.ToBase64String(bytes)); break;
            case Blob blob: WriteTagged(writer, "blob", blob.ByteString.ToBase64()); break;
            case Timestamp timestamp: WriteTimestamp(writer, timestamp); break;
            case DateTime at when at.Kind == DateTimeKind.Utc: WriteTimestamp(writer, Timestamp.FromDateTime(at)); break;
            case DateTimeOffset at when at.Offset == TimeSpan.Zero: WriteTimestamp(writer, Timestamp.FromDateTimeOffset(at)); break;
            case IReadOnlyDictionary<string, object> map: WriteMap(writer, map, depth); break;
            case IDictionary<string, object> map: WriteMap(writer, map, depth); break;
            case IEnumerable list when value is not IDictionary:
                writer.WriteStartArray();
                writer.WriteStringValue("array");
                writer.WriteStartArray();
                foreach (var element in list) WriteValue(writer, element, depth + 1);
                writer.WriteEndArray();
                writer.WriteEndArray();
                break;
            default:
                throw new ArgumentException($"Unsupported Firestore field value type '{value.GetType().Name}'.");
        }
    }

    private static void WriteTagged<T>(Utf8JsonWriter writer, string tag, T value)
    {
        writer.WriteStartArray();
        writer.WriteStringValue(tag);
        JsonSerializer.Serialize(writer, value);
        writer.WriteEndArray();
    }

    private static void WriteTimestamp(Utf8JsonWriter writer, Timestamp timestamp)
    {
        var proto = timestamp.ToProto();
        WriteTagged(writer, "timestamp", new long[] { proto.Seconds, proto.Nanos });
    }

    private static Dictionary<string, object> ReadMap(JsonElement fields, int depth) => fields.EnumerateObject()
        .ToDictionary(field => field.Name, field => ReadValue(field.Value, depth + 1)!, StringComparer.Ordinal);

    private static object? ReadValue(JsonElement field, int depth)
    {
        if (depth > MaximumNesting) throw new JsonException("The Firestore field nesting limit was exceeded.");
        return field.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => field.GetString(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Object => ReadMap(field, depth),
            JsonValueKind.Array when field.GetArrayLength() == 2 => ReadTagged(field[0].GetString(), field[1], depth),
            _ => throw new JsonException("Invalid Firestore field value."),
        };
    }

    private static object ReadTagged(string? tag, JsonElement value, int depth) => tag switch
    {
        "byte" => value.GetByte(),
        "sbyte" => value.GetSByte(),
        "short" => value.GetInt16(),
        "ushort" => value.GetUInt16(),
        "int" => value.GetInt32(),
        "uint" => value.GetUInt32(),
        "long" => value.GetInt64(),
        "ulong" => checked((ulong)value.GetInt64()),
        "float" => BitConverter.Int32BitsToSingle(value.GetInt32()),
        "double" => BitConverter.Int64BitsToDouble(value.GetInt64()),
        "bytes" => value.GetBytesFromBase64(),
        "blob" => Blob.CopyFrom(value.GetBytesFromBase64()),
        "timestamp" when value.GetArrayLength() == 2 => ReadTimestamp(value[0].GetInt64(), value[1].GetInt32()),
        "array" => value.EnumerateArray().Select(item => ReadValue(item, depth + 1)!).ToList(),
        _ => throw new JsonException("Unknown Firestore field value type."),
    };

    private static Timestamp ReadTimestamp(long seconds, int nanos)
    {
        if (seconds is < -62_135_596_800 or > 253_402_300_799 || nanos is < 0 or > 999_999_999)
            throw new JsonException("Invalid Firestore timestamp.");
        return Timestamp.FromProto(new ProtoTimestamp { Seconds = seconds, Nanos = nanos });
    }
}
