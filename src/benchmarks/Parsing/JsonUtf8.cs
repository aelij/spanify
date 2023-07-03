using System.Buffers;
using System.Buffers.Text;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BenchmarkDotNet.Configs;

[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory), CategoriesColumn]
public class JsonUtf8
{
    private const int ElementCount = 1000;
    private static readonly ReadOnlyMemory<byte> json = Encoding.UTF8.GetBytes('[' + string.Join(",", Enumerable.Repeat("""{"Value":"42"}""", ElementCount)) + ']');
    private static readonly MyContainer[] containers = Enumerable.Repeat(new MyContainer(new MyValue(42)), ElementCount).ToArray();

    private static readonly JsonSerializerOptions s_naiveOptions = new()
    {
        Converters = { new NaiveMyValueConverter() }
    };

    private static readonly JsonSerializerOptions s_optimizedOptions = new()
    {
        Converters = { new MyValueConverter() }
    };

    [Benchmark, BenchmarkCategory("Serialize")]
    public void SerializeUtf16()
    {
        using var _ = JsonSerializer.SerializeToDocument(containers, s_naiveOptions);
    }

    [Benchmark(Baseline = true), BenchmarkCategory("Serialize")]
    public void SerializeUtf8()
    {
        using var _ = JsonSerializer.SerializeToDocument(containers, s_optimizedOptions);
    }

    [Benchmark, BenchmarkCategory("Deserialize")]
    public void DeerializeUtf16()
    {
        _ = JsonSerializer.Deserialize<MyContainer[]>(json.Span, s_naiveOptions);
    }

    [Benchmark(Baseline = true), BenchmarkCategory("Deserialize")]
    public void DeerializeUtf8()
    {
        _ = JsonSerializer.Deserialize<MyContainer[]>(json.Span, s_optimizedOptions);
    }

    private class NaiveMyValueConverter : JsonConverter<MyValue>
    {
        public override MyValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            new(long.Parse(reader.GetString()!));

        public override void Write(Utf8JsonWriter writer, MyValue value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.Value.ToString());
    }

    private class MyValueConverter : JsonConverter<MyValue>
    {
        public override MyValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            (reader.HasValueSequence ? reader.ValueSequence.ToArray() : reader.ValueSpan) is var source &&
            Utf8Parser.TryParse(source, out long value, out var consumed) && consumed == source.Length
                ? new MyValue(value)
                : throw new FormatException();

        [SkipLocalsInit]
        public override void Write(Utf8JsonWriter writer, MyValue value, JsonSerializerOptions options)
        {
            var bytes = (stackalloc byte[128]);
            if (Utf8Formatter.TryFormat(value.Value, bytes, out var written))
            {
                writer.WriteStringValue(bytes[..written]);
            }
            else
            {
                // this would happen if the stackalloc'd size isn't enough (impossible here as any long should fit)
                throw new FormatException();
            }
        }
    }

    private record MyValue(long Value);
    private record MyContainer(MyValue Value);
}
