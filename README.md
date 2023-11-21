# `Span`-ify your code

.NET has introduced low(er)-level memory management APIs over the years that can dramatically reduce the number of allocations and improve performance, and first among them is `Span<T>`.

This repo demonstrates common usage patterns for some of those APIs.

> [!IMPORTANT]
> Always [benchmark](https://github.com/dotnet/BenchmarkDotNet) and test the code for correctness, especially when using low-level/unsafe APIs.


## Table of contents

### [Basics](docs/basics.md)
Introduction to APIs and stack allocation
### [Strings](docs/strings.md)
Creating and manipulating strings
### [Parsing and formatting](docs/parsing-formatting.md)
Parsing and formatting values, by way of writing `JsonConverter`s
### [Buffers](docs/buffers.md)
Writing to buffers and streams
### [Collections](docs/collections.md)
Optimize access to collections
### [Console](docs/console.md)
Writing to the `Console` using UTF-8
