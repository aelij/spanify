using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using DotNext.Buffers;

public static class StringExtensions
{
    private const int ByteStackallocThreshold = 256;

    public static string Reverse(this string s) =>
        string.Create(s.Length, s, static (span, str) =>
        {
            str.AsSpan().CopyTo(span);
            span.Reverse();
        });

    public static string ReplaceNonAlphanumeric(this ReadOnlySpan<char> s, char replacement)
    {
        return string.Create(s.Length, new ReplaceNonAlphanumericData(s, replacement), static (span, state) =>
        {
            for (int i = 0; i < state.S.Length; i++)
            {
                var c = state.S[i];
                span[i] = char.IsLetterOrDigit(c) ? c : state.Replacement;
            }
        });
    }

    private readonly ref struct ReplaceNonAlphanumericData(ReadOnlySpan<char> s, char replacement)
    {
        public readonly char Replacement = replacement;
        public readonly ReadOnlySpan<char> S = s;
    }

    [SkipLocalsInit]
    public static string GetSha256(this ReadOnlySpan<char> s)
    {
        int inputByteCount = Encoding.UTF8.GetByteCount(s);
        using SpanOwner<byte> encodedBytes = inputByteCount <= ByteStackallocThreshold ? new(stackalloc byte[ByteStackallocThreshold], inputByteCount) : new(inputByteCount);
        int encodedByteCount = Encoding.UTF8.GetBytes(s, encodedBytes.Span);
        var hash = (stackalloc byte[32]);
        SHA256.HashData(encodedBytes.Span[..encodedByteCount], hash);
        return Convert.ToHexString(hash);
    }
}
