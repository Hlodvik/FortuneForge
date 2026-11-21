using System;
using System.Collections.Generic;
using System.Text.Json;
using FortuneForge.Games.Abstractions.Analytics;
using FortuneForge.Games.Firestore;
using Google.Cloud.Firestore;
using ProtoTimestamp = Google.Protobuf.WellKnownTypes.Timestamp;

namespace FortuneForge.Games.Tests.Firestore;

public sealed class FirestoreFieldCodecTests
{
    [Fact]
    public void Round_trip_preserves_exact_scalars_nested_values_and_timestamp_nanoseconds()
    {
        var timestamp = Timestamp.FromProto(new ProtoTimestamp { Seconds = 1_790_021_234, Nanos = 123_456_789 });
        var signedZero = BitConverter.Int64BitsToDouble(long.MinValue);
        var fields = new Dictionary<string, object>
        {
            ["long"] = long.MaxValue,
            ["short"] = short.MinValue,
            ["float"] = 2.25f,
            ["signedZero"] = signedZero,
            ["nonfinite"] = double.PositiveInfinity,
            ["bytes"] = new byte[] { 1, 3, 7 },
            ["blob"] = Blob.CopyFrom([9, 8, 7]),
            ["map"] = new Dictionary<string, object>
            {
                ["timestamp"] = timestamp,
                ["list"] = new object[] { "timestamp", timestamp, true, null!, new Dictionary<string, object> { ["\"quoted\""] = 6 } },
            },
        };

        var encoded = FirestoreFieldCodec.Serialize(fields);
        var restored = FirestoreFieldCodec.Deserialize(encoded);
        Assert.Equal(long.MaxValue, Assert.IsType<long>(restored["long"]));
        Assert.Equal(short.MinValue, Assert.IsType<short>(restored["short"]));
        Assert.Equal(2.25f, Assert.IsType<float>(restored["float"]));
        Assert.Equal(long.MinValue, BitConverter.DoubleToInt64Bits(Assert.IsType<double>(restored["signedZero"])));
        Assert.Equal(double.PositiveInfinity, Assert.IsType<double>(restored["nonfinite"]));
        Assert.Equal(new byte[] { 1, 3, 7 }, Assert.IsType<byte[]>(restored["bytes"]));
        Assert.Equal(fields["blob"], Assert.IsType<Blob>(restored["blob"]));
        var map = Assert.IsType<Dictionary<string, object>>(restored["map"]);
        Assert.Equal(timestamp, Assert.IsType<Timestamp>(map["timestamp"]));
        var list = Assert.IsType<List<object>>(map["list"]);
        Assert.Equal("timestamp", list[0]);
        Assert.Equal(timestamp, Assert.IsType<Timestamp>(list[1]));
        Assert.True(Assert.IsType<bool>(list[2]));
        Assert.Null(list[3]);
        Assert.Equal(6, Assert.IsType<int>(Assert.IsType<Dictionary<string, object>>(list[4])["\"quoted\""]));
        Assert.Equal(encoded, FirestoreFieldCodec.Serialize(restored));
    }

    [Fact]
    public void Dictionary_order_is_canonical_and_transforms_are_rejected_at_every_level()
    {
        Assert.Equal(FirestoreFieldCodec.Serialize(new Dictionary<string, object> { ["a"] = 1, ["z"] = 2 }),
            FirestoreFieldCodec.Serialize(new Dictionary<string, object> { ["z"] = 2, ["a"] = 1 }));
        foreach (var sentinel in new[] { FieldValue.ServerTimestamp, FieldValue.Delete, FieldValue.Increment(2L), FieldValue.ArrayUnion("x") })
        {
            Assert.Throws<ArgumentException>(() => FirestoreFieldCodec.Serialize(new Dictionary<string, object> { ["at"] = sentinel }));
            Assert.Throws<ArgumentException>(() => FirestoreFieldCodec.Serialize(new Dictionary<string, object>
            {
                ["nested"] = new object[] { new Dictionary<string, object> { ["at"] = sentinel } },
            }));
        }
    }

    [Fact]
    public void Analytics_utc_dates_use_the_existing_firestore_timestamp_encoding()
    {
        var start = DateTime.UnixEpoch.AddTicks(19);
        var finish = start.AddSeconds(2).AddTicks(29);
        var outbox = GameAnalyticsOutbox.Create(new("texas-holdem", "test-round", "alice", "paid", true,
            finish, start, 500, 700, "creditHoldemMatches", "test-match"))!;
        var startedAt = Timestamp.FromDateTime(start);
        var completedAt = Timestamp.FromDateTime(finish);

        var encoded = FirestoreFieldCodec.Serialize(outbox.Fields);
        var restored = FirestoreFieldCodec.Deserialize(encoded);
        Assert.Equal(startedAt, Assert.IsType<Timestamp>(restored["startedAt"]));
        Assert.Equal(completedAt, Assert.IsType<Timestamp>(restored["completedAt"]));
        Assert.Equal(encoded, FirestoreFieldCodec.Serialize(restored));
        Assert.Equal(FirestoreFieldCodec.Serialize(new Dictionary<string, object> { ["at"] = completedAt }),
            FirestoreFieldCodec.Serialize(new Dictionary<string, object> { ["at"] = new DateTimeOffset(finish) }));
        Assert.Throws<ArgumentException>(() => FirestoreFieldCodec.Serialize(new Dictionary<string, object>
        {
            ["at"] = new DateTimeOffset(finish).ToOffset(TimeSpan.FromHours(2)),
        }));
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void Non_utc_dates_are_rejected(DateTimeKind kind)
    {
        var at = DateTime.SpecifyKind(DateTime.UnixEpoch, kind);
        Assert.Throws<ArgumentException>(() => FirestoreFieldCodec.Serialize(new Dictionary<string, object> { ["at"] = at }));
    }

    [Theory]
    [InlineData("{\"version\":2,\"fields\":{}}")]
    [InlineData("{\"version\":1,\"fields\":{\"a\":true,\"a\":false}}")]
    [InlineData("{\"version\":1,\"fields\":{\"a\":[\"timestamp\",[0,1000000000]]}}")]
    [InlineData("{\"version\":1,\"fields\":{\"a\":[\"increment\",3]}}")]
    public void Decoder_rejects_unrecognized_or_damaged_encodings(string encoded) =>
        Assert.Throws<JsonException>(() => FirestoreFieldCodec.Deserialize(encoded));
}
