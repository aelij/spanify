# Buffers and streams

## Using `IBufferWriter<T>`

`IBufferWriter<T>` is somewhat similar to a write-only `Stream`, where the caller controls when to advance the position. It's used in modern APIs, such as `Utf8JsonWriter`, `Encoding` and `Pipe`.

### Implementations

* `ArrayBufferWriter<T>` - based on arrays, and works similarly to `List<T>` - when the size is too small, the data is copied to a new array.
* `PooledArrayBufferWriter<T>` (from dotNext) - uses `ArrayPool<T>`.
* `PipeWriter` - a `IBufferWriter<byte>` implementation that is used by `Pipe` and can convert from and to `Stream` (`PipeWriter.Create` and `PipeWriter.AsStream` methods, respectively.)

💡 If we know the expected capacity, we can set it during initialization to minimize resizing.

### Example: Encoding a string to UTF-8 **-- TODO -- FIND A BETTER EXAMPLE**

The advantage of using it here is that we don't have to use `Encoding.GetByteCount` and just pass the writer directly to `Encoding.GetBytes`.

⚠️ As with any pooled object, returning the object is essential for performance. Like `MemoryRental`, returning in `PooledArrayBufferWriter` is implemented using the disposable pattern.

```cs
static string GetSha256(this string s)
{
    using var writer = new PooledArrayBufferWriter<byte>();
    Encoding.UTF8.GetBytes(s, writer);
    var hash = (stackalloc byte[32]);
    SHA256.HashData(writer.WrittenMemory.Span, hash);
    return Convert.ToHexString(hash);
}
```
