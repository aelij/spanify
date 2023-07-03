[MemoryDiagnoser]
public class StringParse
{
    const string text = "12345";

    [Benchmark]
    public int ParseSubstring() => int.Parse(text[1..3]);

    [Benchmark(Baseline = true)]
    public int ParseAsSpan() => int.Parse(text.AsSpan()[1..3]);

}
