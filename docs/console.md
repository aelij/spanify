# Console

## Writing UTF-8 strings to the Console

`Console.Write` methods use `System.String` which is UTF-16. In some scenarios we already have UTF-8 data that we want to write to the console (for example, JSON), and on some systems the default `Console.OutputStream` is UTF-8. It would be wasteful to allocate a string just to convert it back to UTF-8 bytes, so instead we can write the bytes directly to the output stream.

:warning: It's up to the caller to ensure that the value is valid UTF-8. We could validate it using `Encoding.GetByteCount`, but it might be wasteful if the API used to produce the bytes already does that.

```cs
public static class Utf8Console
{
    private static readonly Stream s_outputStream = Console.OpenStandardOutput();
    private static readonly ReadOnlyMemory<byte> s_newLine = Encoding.UTF8.GetBytes(Environment.NewLine);

    private static bool IsUtf8 => Console.OutputEncoding is UTF8Encoding;

    public static void Write(ReadOnlySpan<byte> value)
    {
        if (IsUtf8)
        {
            lock (Console.Out)
            {
                s_outputStream.Write(value);
            }
        }
        else
        {
            Console.Write(Encoding.UTF8.GetString(value));
        }
    }

    public static void WriteLine(ReadOnlySpan<byte> value)
    {
        if (IsUtf8)
        {
            lock (Console.Out)
            {
                s_outputStream.Write(value);
                s_outputStream.Write(s_newLine.Span);
            }
        }
        else
        {
            Console.WriteLine(Encoding.UTF8.GetString(value));
        }
    }
}
```

Examples:

```cs
Utf8Console.WriteLine("Hello, World"u8);
Utf8Console.WriteLine(JsonSerializer.SerializeToUtf8Bytes(new { a = 1, b = "2" }));
```
