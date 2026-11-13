# Parsing and formatting

In this section we'll show examples of parsing and formatting values, by way of writing `JsonConverter`s. `System.Text.Json` excels at memory management. It extensively uses `Span<T>` and object pooling to avoid allocations, and we can use these techniques ourselves.

## Using `Utf8Parser` and `Utf8Formatter`

Suppose we want to create a converter that takes strings and converts them from/to a record:

```cs
record MyValue(long Value);
```

When reading the value, the naive approach is to convert it to a string use `Int64.Parse`. For writing, the naive approach is to use `ToString()`.

```cs
class NaiveMyValueConverter : JsonConverter<MyValue>
{
    public override MyValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        new MyValue(long.Parse(reader.GetString()));

    public override void Write(Utf8JsonWriter writer, MyValue value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value.ToString());
}
```

Instead of allocating a string, we can parse the UTF-8 value directly (similarly to how methods like `Utf8JsonReader.GetInt64` are implemented). Note that if the value is a `Sequence` rather than a `Span`, we'll allocate an array, but that should rarely happen.

For writing values we'll use `stackalloc` to avoid the allocation, and the UTF-8 formatter to avoid going through `String`'s UTF-16.

> [!IMPORTANT]
> The following method will not work for values that require unescaping (for example, `\uXXXX`). We can use `CopyString` to get the unescaped string if needed.

```cs
class MyValueConverter : JsonConverter<MyValue>
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
```

With 1,000 values of `1234567890123456789`, BenchmarkDotNet shows that the UTF-8 converter avoids 64,000 bytes of temporary-string allocations:

| Operation   | Converter               | Mean     | Ratio | Allocated |
|------------ |------------------------ |---------:|------:|----------:|
| Deserialize | `NaiveMyValueConverter` | 99.47 us |  1.20 |  241096 B |
| Deserialize | `MyValueConverter`      | 83.05 us |  1.00 |  177096 B |
| Serialize   | `NaiveMyValueConverter` | 71.49 us |  1.10 |   64440 B |
| Serialize   | `MyValueConverter`      | 64.90 us |  1.00 |     440 B |

## Using `JsonSerializer`

We can call `JsonSerializer` with different options inside the converters to efficiently convert values.

> [!IMPORTANT]
> Make sure to instantiate `JsonSerializerOptions` only once, as it's used to cache serialization metadata.

```cs
class MyValueConverter : JsonConverter<MyValue>
{
    private static readonly JsonSerializerOptions s_options = new() { NumberHandling = JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString };

    public override MyValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        new(JsonSerializer.Deserialize<long>(ref reader, s_options));

    public override void Write(Utf8JsonWriter writer, MyValue value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, value.Value, s_options);
}
```

## Using `Encoding` and `ISpanFormattable`

`Utf8Parser` and `Utf8Formatter` only work for [standard formats](https://learn.microsoft.com/en-us/dotnet/standard/base-types/formatting-types), specified using a single character (for example, `x` for hexadecimal numbers). For a type without UTF-8 support for the required format, we can use methods that encode/decode UTF-16 `char`s rather than UTF-8 `byte`s. We can still avoid most allocations.

> [!TIP]
> `IUtf8SpanParsable` and `IUtf8SpanFormattable` can be used with custom formats.

> [!IMPORTANT]
> Do NOT use `ValueSpan` or `ValueSequence` directly to get **string data** from `Utf8JsonReader` - it may be not be well-formed, and may require unescaping. Use `CopyString` instead.

```cs
class DateTimeConverter : JsonConverter<DateTime>
{
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
        Debug.Assert(success);
        writer.WriteStringValue(chars[..written]);
    }
}
```

BenchmarkDotNet results for 1,000 dates:

| Operation   | Converter                | Mean      | Ratio | Allocated |
|------------ |------------------------- |----------:|------:|----------:|
| Deserialize | `NaiveDateTimeConverter` | 146.56 us |  0.99 |  201096 B |
| Deserialize | `DateTimeConverter`      | 148.16 us |  1.00 |  153096 B |
| Serialize   | `NaiveDateTimeConverter` |  79.24 us |  1.06 |   48440 B |
| Serialize   | `DateTimeConverter`      |  74.69 us |  1.00 |     440 B |

## Partial numeric parsing

`INumberBase<TSelf>.TryParsePartial` accepts UTF-8 or UTF-16 spans and reports how much input was consumed. This lets a parser read a numeric prefix without allocating a substring or first finding the delimiter. Call the interface member through a constrained type parameter, as the integer types implement it explicitly:

```cs
static T ParseIntegerBeforeComma<T>(ReadOnlySpan<byte> source) where T : IBinaryInteger<T>
{
    if (!T.TryParsePartial(source, NumberStyles.Integer, CultureInfo.InvariantCulture,
            out T? value, out int consumed) ||
        consumed >= source.Length || source[consumed] != (byte)',')
    {
        throw new FormatException("Expected an integer followed by a comma.");
    }

    return value;
}

int value = ParseIntegerBeforeComma<int>("42,next"u8); // 42
```

> [!IMPORTANT]
> Partial parsing permits trailing input, so validate what follows the number (or check `consumed == source.Length`).

## Base64 directly into spans

`Base64` supports `EncodeToChars` and `DecodeFromChars`, along with span overloads and `Try*` methods for UTF-8. We can choose UTF-16 output for a character-based consumer without an intermediate Base64 string:

```cs
ReadOnlySpan<byte> source = "span"u8;
Span<char> encoded = stackalloc char[Base64.GetEncodedLength(source.Length)];
int charsWritten = Base64.EncodeToChars(source, encoded);

Span<byte> decoded = stackalloc byte[Base64.GetMaxDecodedLength(charsWritten)];
int bytesWritten = Base64.DecodeFromChars(encoded[..charsWritten], decoded);
Debug.Assert(decoded[..bytesWritten].SequenceEqual(source));
```

## Writing large strings in segments

When writing large string values to JSON, we may want to avoid buffering the entire content in memory. `Utf8JsonWriter` provides segment-writing methods that allow streaming content in chunks:

* `WriteStringValueSegment` - writes a portion of a string value (UTF-8 or UTF-16)
* `WriteBase64StringSegment` - writes a portion of base64-encoded binary data

The `isFinalSegment` parameter indicates when the value is complete.

```cs
void WriteLargeString(Utf8JsonWriter writer, IEnumerable<ReadOnlyMemory<byte>> chunks)
{
    bool isFirst = true;
    ReadOnlyMemory<byte> lastChunk = default;
    
    foreach (var chunk in chunks)
    {
        if (!isFirst)
        {
            writer.WriteStringValueSegment(lastChunk.Span, isFinalSegment: false);
        }
        lastChunk = chunk;
        isFirst = false;
    }
    
    writer.WriteStringValueSegment(lastChunk.Span, isFinalSegment: true);
}
```

## Reusing a JSON writer with different options

`Utf8JsonWriter.Reset` accepts both a new output target (`Stream` or `IBufferWriter<byte>`) and `JsonWriterOptions`. This lets us reuse a writer even when the next document needs different formatting:

```cs
var first = new ArrayBufferWriter<byte>();
var second = new ArrayBufferWriter<byte>();
using var writer = new Utf8JsonWriter(first);
writer.WriteNumberValue(1);
writer.Flush();

writer.Reset(second, new JsonWriterOptions { Indented = true });
writer.WriteStartArray();
writer.WriteNumberValue(2);
writer.WriteEndArray();
writer.Flush();
```
