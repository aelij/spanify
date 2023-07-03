global using BenchmarkDotNet.Attributes;

using System.Reflection;
using BenchmarkDotNet.Running;

BenchmarkSwitcher.FromAssembly(Assembly.GetEntryAssembly()!).Run(args);
