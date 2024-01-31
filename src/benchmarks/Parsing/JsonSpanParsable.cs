using System.Buffers;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BenchmarkDotNet.Configs;
using DotNext.Buffers;

[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory), CategoriesColumn]
public class JsonSpanParsable
{
    private const int ElementCount = 1000;
    private static readonly ReadOnlyMemory<byte> json = Encoding.UTF8.GetBytes('[' + string.Join(",", Enumerable.Repeat("""{"Value":"2020-01-01"}""", ElementCount)) + ']');
    private static readonly MyContainer[] containers = Enumerable.Repeat(new MyContainer(new DateTime(2000, 1, 1)), ElementCount).ToArray();

    private static readonly JsonSerializerOptions s_naiveOptions = new()
    {
        Converters = { new NaiveDateTimeConverter() }
    };

    private static readonly JsonSerializerOptions s_optimizedOptions = new()
    {
        Converters = { new DateTimeConverter() }
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

    private class NaiveDateTimeConverter : JsonConverter<DateTime>
    {
        private const string DateFormat = "yyyy-MM-dd";

        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            DateTime.ParseExact(reader.GetString()!, DateFormat, provider: CultureInfo.InvariantCulture);

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString(DateFormat,  provider: CultureInfo.InvariantCulture));
    }

    private class DateTimeConverter : JsonConverter<DateTime>
    {
        private const int StackallocThreshold = 256;
        private const string DateFormat = "yyyy-MM-dd";

        [SkipLocalsInit]
        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var length = reader.HasValueSequence ? checked((int)reader.ValueSequence.Length) : reader.ValueSpan.Length;
            // allocate a buffer on the stack if possible, otherwise use array pool
            using SpanOwner<char> chars = length <= StackallocThreshold ? new(stackalloc char[StackallocThreshold], length) : new(length);
            // copy the string data to the buffer, unescaping and validating it
            var written = reader.CopyString(chars.Span);
            // parse the date
            return DateTime.ParseExact(chars.Span[..written], DateFormat, provider: CultureInfo.InvariantCulture);
        }

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        {
            // in this case we know exactly the length the format string will yield
            // so it is safe to allocate its size and assume formatting must succeed
            var chars = (stackalloc char[DateFormat.Length]);
            var success = value.TryFormat(chars, out var written, DateFormat, provider: CultureInfo.InvariantCulture);
            writer.WriteStringValue(chars[..written]);
        }
    }

    private record MyContainer(DateTime Value);
}
