# Buffers and streams

## Using `IBufferWriter<T>`

`IBufferWriter<T>` is somewhat similar to a write-only `Stream`, where the caller controls when to advance the position. It's used in modern APIs, such as `Utf8JsonWriter`, `Encoding` and `Pipe`.

### Implementations

* `ArrayBufferWriter<T>` - based on arrays, and works similarly to `List<T>` - when the size is too small, the data is copied to a new array.
* `PoolingArrayBufferWriter<T>` (from dotNext) - uses `ArrayPool<T>`.
* `PipeWriter` - a `IBufferWriter<byte>` implementation that is used by `Pipe` and can convert from and to `Stream` (`PipeWriter.Create` and `PipeWriter.AsStream` methods, respectively.)

> [!TIP]
> If we know the expected capacity, we can set it during initialization to minimize resizing.

### Example: JSON serialization and `Socket`

As we don't know the size of the JSON string in advance, `IBufferWriter<byte>` allows allocating bytes as needed.

> [!IMPORTANT]
> As with any pooled object, returning the object is essential for performance. Like `SpanOwner`, returning in `PoolingArrayBufferWriter` is implemented using the disposable pattern.

> [!TIP]
> It's important to dispose the `Utf8JsonWriter` before using the buffer in order to flush all bytes.

```cs
void SerializeToSocket<T>(Socket socket, T value)
{
    using var bufferWriter = new PoolingArrayBufferWriter<byte>();

    using (var jsonWriter = new Utf8JsonWriter(bufferWriter))
    {
        JsonSerializer.Serialize(jsonWriter, value);
    }

    socket.Send(bufferWriter.WrittenMemory.Span);
}
```
