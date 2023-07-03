[MemoryDiagnoser]
public class StringConcat
{
    const string text = "abcdefghijklmnopqrstuvwxyz";

    [Benchmark]
    public string ConcatSubstring() => text[10..] + "---" + text[..5];

    [Benchmark(Baseline = true)]
    public string ConcatAsSpan() => string.Concat(text.AsSpan()[10..], "---", text.AsSpan()[..5]);

}
