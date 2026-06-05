using BenchmarkDotNet.Running;
using Dasim.Radio.Audio.Benchmarks;

// Pass BenchmarkDotNet CLI args through, e.g. `-- --filter *EncodeSingle* --job short`.
BenchmarkSwitcher.FromAssembly(typeof(OpusEncodeBenchmarks).Assembly).Run(args);
