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

:warning: The following method will not work for values that require unescaping (for example, `\uXXXX`). We can use `CopyString` to get the unescaped string if needed.

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

## Using `JsonSerializer`

We can call `JsonSerializer` with different options inside the converters to efficiently convert values.

:warning: Make sure to instantiate `JsonSerializerOptions` only once, as it's used to cache serialization metadata.

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

`Utf8Parser` and `Utf8Formatter` only work for [standard formats](https://learn.microsoft.com/en-us/dotnet/standard/base-types/formatting-types), specified using a single character (for example, `x` for hexadecimal numbers). If we want to use custom formats, we'll have to use methods that encode/decode UTF-16 `char`s rather than UTF-8 `byte`s. But fear not - we can still avoid most allocations.

:bulb: :eight: `IUtf8SpanParsable` and `IUtf8SpanFormattable` can be used with custom formats.

:warning: Do NOT use `ValueSpan` or `ValueSequence` directly to get **string data** from `Utf8JsonReader` - it may be not be well-formed, and may require unescaping. Use `CopyString` instead.

```cs
class DateTimeConverter : JsonConverter<DateTime>
{
    private const string DateFormat = "yyyy-MM-dd";
    
    [SkipLocalsInit]
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var length = reader.HasValueSequence ? checked((int)reader.ValueSequence.Length) : reader.ValueSpan.Length;
        // allocate a buffer on the stack if possible, otherwise use array pool
        using MemoryRental<char> chars = length <= StackallocThreshold ? new(stackalloc char[StackallocThreshold], length) : new(length);
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
