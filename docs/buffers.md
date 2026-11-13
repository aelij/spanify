# Buffers and streams

## Using `IBufferWriter<T>`

`IBufferWriter<T>` is somewhat similar to a write-only `Stream`, where the caller controls when to advance the position. It's used in modern APIs, such as `Utf8JsonWriter`, `Encoding` and `Pipe`.

### Implementations

* `ArrayBufferWriter<T>` - based on arrays, and works similarly to `List<T>` - when the size is too small, the data is copied to a new array.
* `PoolingArrayBufferWriter<T>` (from dotNext) - uses `ArrayPool<T>`.
* `PipeWriter` - a `IBufferWriter<byte>` implementation that is used by `Pipe` and can convert from and to `Stream` (`PipeWriter.Create` and `PipeWriter.AsStream` methods, respectively.)
* `RecyclableMemoryStream` - another `Stream` implementation that uses memory pools.

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

### Example: Using `RecyclableMemoryStream` as a buffer writer

```cs
static readonly RecyclableMemoryStreamManager manager = new();

var bigInt = BigInteger.Parse("123456789013374299100987654321");

using (var stream = manager.GetStream())
{
    var buffer = stream.GetSpan(bigInt.GetByteCount());
    bigInt.TryWriteBytes(buffer, out int bytesWritten);
    stream.Advance(bytesWritten);
}
```

## In-memory stream adapters

When an API requires `Stream`, adapters let us expose existing memory without copying it into a `MemoryStream` or flattening a sequence with `ToArray()`:

| Adapter | Input | Use |
| --- | --- | --- |
| `System.IO.ReadOnlyMemoryStream` | `ReadOnlyMemory<byte>` | Read existing contiguous bytes |
| `System.IO.WritableMemoryStream` | `Memory<byte>` | Read/write a fixed-capacity buffer |
| `System.Buffers.ReadOnlySequenceStream` | `ReadOnlySequence<byte>` | Read segmented bytes without flattening |
| `System.IO.StringStream` | `string` or `ReadOnlyMemory<char>`, plus an `Encoding` | Encode text as it is read, without an intermediate encoded `byte[]` |

```cs
static async Task CopySequenceAsync(
    ReadOnlySequence<byte> source, Stream destination, CancellationToken cancellationToken)
{
    using var stream = new ReadOnlySequenceStream(source);
    await stream.CopyToAsync(destination, cancellationToken);
}
```

The adapter objects still allocate, but avoid the intermediate data copy. `WritableMemoryStream` cannot grow beyond the supplied memory; use a growable or pooled writer when the output size is unknown.

> [!IMPORTANT]
> These adapters do not take ownership of the backing memory. Keep it valid until the consumer has finished. In particular, do not return a rented array or advance a `PipeReader` past a sequence while its stream is still in use.

## Span-based compression

`System.IO.Compression` provides `DeflateEncoder` / `DeflateDecoder`, `ZLibEncoder` / `ZLibDecoder`, and `GZipEncoder` / `GZipDecoder`. Like the Brotli APIs, they compress/decompress spans without wrapping buffers in streams:

```cs
ReadOnlySpan<byte> source = "span span span"u8;
Span<byte> destination = stackalloc byte[128];
if (!GZipEncoder.TryCompress(source, destination, out int bytesWritten))
{
    throw new InvalidOperationException("Compression destination is too small.");
}

ReadOnlySpan<byte> compressed = destination[..bytesWritten];
```

> [!TIP]
> Use `GetMaxCompressedLength` to size the destination. For streaming data, use the instance `Compress`/`Decompress` methods, and `Reset()` to reuse an encoder or decoder. `ZstandardEncoder` / `ZstandardDecoder` provide the same pattern for Zstandard.
