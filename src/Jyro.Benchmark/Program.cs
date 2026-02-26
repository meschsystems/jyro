using System.CommandLine;
using System.Diagnostics;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using Mesch.Jyro;
using Mesch.Jyro.Http;
using Microsoft.FSharp.Core;

namespace Jyro.Benchmark;

internal sealed class TestCase
{
    public required string Group { get; init; }
    public required string Name { get; init; }
    public required string Script { get; init; }
    public required byte[] Compiled { get; init; }
    public string? DataJson { get; init; }
}

internal sealed class TestResult
{
    public required string Group { get; init; }
    public required string Name { get; init; }
    public double SrcMedianMs { get; set; }
    public double CmpMedianMs { get; set; }
    public double DiffMs { get; set; }
    public double Speedup { get; set; }
    public double SrcStdDev { get; set; }
    public double CmpStdDev { get; set; }
    public bool SrcPassed { get; set; }
    public bool CmpPassed { get; set; }
    public double[] SrcTimes { get; set; } = [];
    public double[] CmpTimes { get; set; } = [];
}

internal static class Program
{
    private static int Main(string[] args)
    {
        var testsDirOption = new Option<DirectoryInfo?>(
            aliases: ["--tests-dir", "-t"],
            description: "Path to the tests directory containing plaintext/ and binaries/ folders.")
        {
            IsRequired = false
        };

        var categoryOption = new Option<string?>(
            aliases: ["--category", "-c"],
            description: "Run only tests in the specified category folder name.");

        var iterationsOption = new Option<int>(
            aliases: ["--iterations", "-n"],
            description: "Number of measured iterations per test.",
            getDefaultValue: () => 30);

        var warmupOption = new Option<int>(
            aliases: ["--warmup", "-w"],
            description: "Number of warmup iterations (discarded) before measured runs.",
            getDefaultValue: () => 2);

        var timeoutOption = new Option<int>(
            aliases: ["--timeout"],
            description: "Per-test timeout in seconds.",
            getDefaultValue: () => 10);

        var rootCommand = new RootCommand("Jyro in-process benchmark: source vs compiled performance comparison.")
        {
            testsDirOption,
            categoryOption,
            iterationsOption,
            warmupOption,
            timeoutOption
        };

        rootCommand.SetHandler(Run, testsDirOption, categoryOption, iterationsOption, warmupOption, timeoutOption);
        return rootCommand.Invoke(args);
    }

    private static void Run(DirectoryInfo? testsDirInfo, string? category, int iterations, int warmup, int timeoutSeconds)
    {
        // Resolve tests directory
        var testsDir = testsDirInfo?.FullName
            ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "tests"));

        var plaintextDir = Path.Combine(testsDir, "plaintext");
        var binariesDir = Path.Combine(testsDir, "binaries");

        if (!Directory.Exists(plaintextDir))
        {
            WriteError($"Plaintext directory not found: {plaintextDir}");
            return;
        }

        if (!Directory.Exists(binariesDir))
        {
            WriteError($"Binaries directory not found: {binariesDir}");
            Console.WriteLine("Run Compile-All.ps1 first to compile .jyro files to .jyrx.");
            return;
        }

        // -- Test Discovery --
        var tests = DiscoverTests(plaintextDir, binariesDir, category);

        if (tests.Count == 0)
        {
            WriteError($"No test triplets found under {testsDir}");
            if (category != null)
                Console.WriteLine($"  (filtered by category: {category})");
            return;
        }

        // -- Preload all files into memory --
        Console.WriteLine();
        Console.Write("  Preloading files...");
        var testCases = PreloadTests(tests, plaintextDir, binariesDir);
        Console.WriteLine($" {testCases.Count} tests loaded.");

        // -- Run Benchmark --
        var timeout = TimeSpan.FromSeconds(timeoutSeconds);
        RunBenchmark(testCases, iterations, warmup, timeout, testsDir);
    }

    private static List<(string Group, string Name, string ScriptPath, string CompiledPath, string? DataPath)>
        DiscoverTests(string plaintextDir, string binariesDir, string? category)
    {
        var tests = new List<(string Group, string Name, string ScriptPath, string CompiledPath, string? DataPath)>();

        foreach (var scriptFile in Directory.EnumerateFiles(plaintextDir, "*.jyro", SearchOption.AllDirectories))
        {
            var dir = Path.GetDirectoryName(scriptFile)!;
            var baseName = Path.GetFileNameWithoutExtension(scriptFile);
            var group = Path.GetFileName(dir);

            // Category filter
            if (category != null && !string.Equals(group, category, StringComparison.OrdinalIgnoreCase))
                continue;

            // Require expected output file
            var outFile = Path.Combine(dir, $"O-{baseName}.json");
            if (!File.Exists(outFile))
            {
                WriteWarning($"Skipping {group}/{baseName} - missing O-{baseName}.json");
                continue;
            }

            // Find corresponding .jyrx
            var rel = Path.GetRelativePath(plaintextDir, dir);
            var jyrxFile = Path.Combine(binariesDir, rel, baseName + ".jyrx");
            if (!File.Exists(jyrxFile))
            {
                WriteWarning($"Skipping {group}/{baseName} - missing compiled {jyrxFile}");
                continue;
            }

            // Optional data file
            var dataFile = Path.Combine(dir, $"D-{baseName}.json");
            string? dataPath = File.Exists(dataFile) ? dataFile : null;

            tests.Add((group, baseName, scriptFile, jyrxFile, dataPath));
        }

        tests.Sort((a, b) =>
        {
            var cmp = string.Compare(a.Group, b.Group, StringComparison.Ordinal);
            return cmp != 0 ? cmp : string.Compare(a.Name, b.Name, StringComparison.Ordinal);
        });

        return tests;
    }

    private static List<TestCase> PreloadTests(
        List<(string Group, string Name, string ScriptPath, string CompiledPath, string? DataPath)> tests,
        string plaintextDir, string binariesDir)
    {
        var cases = new List<TestCase>(tests.Count);

        foreach (var (group, name, scriptPath, compiledPath, dataPath) in tests)
        {
            cases.Add(new TestCase
            {
                Group = group,
                Name = name,
                Script = File.ReadAllText(scriptPath),
                Compiled = File.ReadAllBytes(compiledPath),
                DataJson = dataPath != null ? File.ReadAllText(dataPath) : null
            });
        }

        return cases;
    }

    private static void RunBenchmark(List<TestCase> tests, int iterations, int warmup, TimeSpan timeout, string testsDir)
    {
        var results = new List<TestResult>(tests.Count);
        var currentGroup = "";

        // -- Header --
        Console.WriteLine();
        WriteColored("  Jyro Benchmark: Source vs Compiled (in-process)", ConsoleColor.Cyan);
        WriteColored("  " + new string('=', 58), ConsoleColor.Cyan);
        Console.WriteLine($"  Tests:      {tests.Count}");
        Console.WriteLine($"  Iterations: {iterations} (+ {warmup} warmup)");
        Console.WriteLine($"  Timeout:    {timeout.TotalSeconds}s");
        Console.WriteLine($"  Statistic:  median");
        Console.WriteLine();
        WriteColored("    " + "Test".PadRight(36) + "  .jyro    .jyrx    Diff     Speedup    StdDev σ", ConsoleColor.DarkGray);
        WriteColored("    " + "".PadRight(36) + " (median) (median)                   (src/cmp)", ConsoleColor.DarkGray);
        WriteColored("    " + new string('-', 86), ConsoleColor.DarkGray);

        foreach (var test in tests)
        {
            // -- Warmup (alternating) --
            for (var i = 0; i < warmup; i++)
            {
                ExecuteSource(test, timeout);
                ExecuteCompiled(test, timeout);
            }

            // -- Measured runs (interleaved to eliminate ordering bias) --
            var srcTimes = new double[iterations];
            var cmpTimes = new double[iterations];
            var srcPassed = true;
            var cmpPassed = true;
            string? srcError = null;
            string? cmpError = null;

            for (var i = 0; i < iterations; i++)
            {
                // Clean slate: collect garbage and suppress GC during measurement
                GC.Collect(2, GCCollectionMode.Forced, true, true);
                GC.WaitForPendingFinalizers();

                GC.TryStartNoGCRegion(16 * 1024 * 1024);
                try
                {
                    var (srcElapsed, srcOk, srcErr) = ExecuteSource(test, timeout);
                    srcTimes[i] = srcElapsed;
                    if (!srcOk) { srcPassed = false; srcError ??= srcErr; }

                    var (cmpElapsed, cmpOk, cmpErr) = ExecuteCompiled(test, timeout);
                    cmpTimes[i] = cmpElapsed;
                    if (!cmpOk) { cmpPassed = false; cmpError ??= cmpErr; }
                }
                finally
                {
                    try { GC.EndNoGCRegion(); } catch (InvalidOperationException) { }
                }
            }

            // -- Skip error-handling tests (both fail = test is designed to fail) --
            if (!srcPassed && !cmpPassed)
                continue;

            // -- Group header (deferred until we have a passing test) --
            if (test.Group != currentGroup)
            {
                currentGroup = test.Group;
                Console.WriteLine();
                WriteColored($"  {currentGroup}", ConsoleColor.Yellow);
            }

            Console.Write("    " + test.Name.PadRight(36));

            // -- Statistics --
            var srcMedian = Math.Round(Median(srcTimes), 1);
            var cmpMedian = Math.Round(Median(cmpTimes), 1);
            var diff = Math.Round(srcMedian - cmpMedian, 1);
            var speedup = cmpMedian > 0 ? Math.Round(srcMedian / cmpMedian, 2) : 0;
            var srcSd = Math.Round(StdDev(srcTimes), 2);
            var cmpSd = Math.Round(StdDev(cmpTimes), 2);

            // -- Format output --
            var srcStr = $"{srcMedian}ms".PadLeft(8);
            var cmpStr = $"{cmpMedian}ms".PadLeft(8);
            var diffSign = diff >= 0 ? "+" : "";
            var diffStr = $"{diffSign}{diff}ms".PadLeft(8);
            var speedupStr = $"{speedup}x".PadLeft(8);
            var sdStr = $"±{srcSd}/{cmpSd}".PadLeft(14);

            var srcColor = !srcPassed ? ConsoleColor.Red
                : srcMedian <= cmpMedian ? ConsoleColor.Green : ConsoleColor.Red;
            var cmpColor = !cmpPassed ? ConsoleColor.Red
                : cmpMedian <= srcMedian ? ConsoleColor.Green : ConsoleColor.Red;
            // When both are equal, show both as white
            if (srcPassed && cmpPassed && srcMedian == cmpMedian)
            {
                srcColor = ConsoleColor.White;
                cmpColor = ConsoleColor.White;
            }
            var diffColor = diff > 0 ? ConsoleColor.Green : diff < 0 ? ConsoleColor.Red : ConsoleColor.Gray;
            var speedupColor = speedup >= 1 ? ConsoleColor.Green : ConsoleColor.Red;

            WriteColoredInline(srcStr, srcColor);
            WriteColoredInline(cmpStr, cmpColor);
            WriteColoredInline(diffStr, diffColor);
            WriteColoredInline(speedupStr, speedupColor);
            WriteColoredInline(sdStr, ConsoleColor.DarkGray);
            if (!srcPassed || !cmpPassed)
            {
                var failLabel = !srcPassed && !cmpPassed ? " [BOTH FAIL]"
                    : !srcPassed ? " [SRC FAIL]" : " [CMP FAIL]";
                WriteColoredInline(failLabel, ConsoleColor.Red);
            }
            Console.WriteLine();
            if (srcError != null)
                WriteColored($"      SRC: {srcError}", ConsoleColor.DarkRed);
            if (cmpError != null)
                WriteColored($"      CMP: {cmpError}", ConsoleColor.DarkRed);

            results.Add(new TestResult
            {
                Group = test.Group,
                Name = test.Name,
                SrcMedianMs = srcMedian,
                CmpMedianMs = cmpMedian,
                DiffMs = diff,
                Speedup = speedup,
                SrcStdDev = srcSd,
                CmpStdDev = cmpSd,
                SrcPassed = srcPassed,
                CmpPassed = cmpPassed,
                SrcTimes = srcTimes,
                CmpTimes = cmpTimes
            });
        }

        // -- Summary --
        PrintSummary(results);

        // -- Log --
        WriteLog(results, iterations, warmup, testsDir);
    }

    private static (double ElapsedMs, bool Passed, string? Error) ExecuteSource(TestCase test, TimeSpan timeout)
    {
        var data = ParseData(test.DataJson);
        var builder = new JyroBuilder()
            .UseStdlib()
            .WithSource(test.Script)
            .WithData(data)
            .UseHttpFunctions();

        return ExecuteWithTimeout(builder, timeout);
    }

    private static (double ElapsedMs, bool Passed, string? Error) ExecuteCompiled(TestCase test, TimeSpan timeout)
    {
        var data = ParseData(test.DataJson);
        var builder = new JyroBuilder()
            .UseStdlib()
            .WithCompiledBytes(test.Compiled)
            .WithData(data)
            .UseHttpFunctions();

        return ExecuteWithTimeout(builder, timeout);
    }

    private static (double ElapsedMs, bool Passed, string? Error) ExecuteWithTimeout(JyroBuilder builder, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        builder.WithCancellationToken(cts.Token);

        var sw = Stopwatch.StartNew();
        var result = builder.Execute();
        sw.Stop();

        var error = !result.IsSuccess
            ? string.Join("; ", result.Messages.Select(m => m.Message))
            : null;
        return (sw.Elapsed.TotalMilliseconds, result.IsSuccess, error);
    }

    private static JyroValue ParseData(string? dataJson)
    {
        if (string.IsNullOrWhiteSpace(dataJson))
            return JyroValue.FromJson("{}", FSharpOption<JsonSerializerOptions>.None);
        return JyroValue.FromJson(dataJson, FSharpOption<JsonSerializerOptions>.None);
    }

    private static double Median(double[] values)
    {
        var sorted = (double[])values.Clone();
        Array.Sort(sorted);
        var n = sorted.Length;
        if (n == 0) return 0;
        if (n % 2 == 1) return sorted[n / 2];
        return (sorted[n / 2 - 1] + sorted[n / 2]) / 2.0;
    }

    private static double StdDev(double[] values)
    {
        if (values.Length < 2) return 0;
        var mean = values.Average();
        var sumSq = values.Sum(v => (v - mean) * (v - mean));
        return Math.Sqrt(sumSq / (values.Length - 1));
    }

    private static void PrintSummary(List<TestResult> results)
    {
        Console.WriteLine();
        WriteColored("  " + new string('=', 58), ConsoleColor.Cyan);
        WriteColored("  Summary", ConsoleColor.Cyan);

        var totalSrcMs = Math.Round(results.Sum(r => r.SrcMedianMs), 1);
        var totalCmpMs = Math.Round(results.Sum(r => r.CmpMedianMs), 1);
        var totalDiff = Math.Round(totalSrcMs - totalCmpMs, 1);
        var avgSpeedup = totalCmpMs > 0 ? Math.Round(totalSrcMs / totalCmpMs, 2) : 0;

        Console.WriteLine($"  Total .jyro median: {totalSrcMs}ms");
        Console.WriteLine($"  Total .jyrx median: {totalCmpMs}ms");

        var diffSign = totalDiff >= 0 ? "+" : "";
        var diffColor = totalDiff > 0 ? ConsoleColor.Green : totalDiff < 0 ? ConsoleColor.Red : ConsoleColor.Gray;
        WriteColored($"  Time saved:       {diffSign}{totalDiff}ms", diffColor);
        WriteColored($"  Overall speedup:  {avgSpeedup}x", avgSpeedup >= 1 ? ConsoleColor.Green : ConsoleColor.Red);

        // Per-category breakdown
        Console.WriteLine();
        WriteColored("  Per-category:", ConsoleColor.DarkGray);
        var groups = results.GroupBy(r => r.Group).OrderBy(g => g.Key);
        foreach (var g in groups)
        {
            var gSrc = Math.Round(g.Sum(r => r.SrcMedianMs), 1);
            var gCmp = Math.Round(g.Sum(r => r.CmpMedianMs), 1);
            var gSpd = gCmp > 0 ? Math.Round(gSrc / gCmp, 2) : 0;
            var gColor = gSpd >= 1 ? ConsoleColor.Green : ConsoleColor.Red;
            WriteColored($"    {g.Key.PadRight(24)}{gSrc}ms -> {gCmp}ms  ({gSpd}x)", gColor);
        }

        // Top 5 biggest speedups
        var sorted = results.OrderByDescending(r => r.Speedup).ToList();
        if (sorted.Count > 0)
        {
            Console.WriteLine();
            WriteColored("  Biggest speedups:", ConsoleColor.DarkGray);
            foreach (var t in sorted.Take(5))
            {
                WriteColored($"    {$"{t.Group}/{t.Name}".PadRight(44)}{t.Speedup}x", ConsoleColor.Green);
            }
        }

        // Regressions (compiled slower)
        var regressions = results.Where(r => r.Speedup < 1).ToList();
        if (regressions.Count > 0)
        {
            Console.WriteLine();
            WriteColored("  Regressions (compiled slower):", ConsoleColor.Red);
            foreach (var t in regressions)
            {
                WriteColored($"    {$"{t.Group}/{t.Name}".PadRight(44)}{t.Speedup}x", ConsoleColor.Red);
            }
        }

        WriteColored("  " + new string('=', 58), ConsoleColor.Cyan);
    }

    private static void WriteLog(List<TestResult> results, int iterations, int warmup, string testsDir)
    {
        var logDir = Path.Combine(testsDir, "logs");
        Directory.CreateDirectory(logDir);

        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
        var logFile = Path.Combine(logDir, $"benchmark-{timestamp}.json");

        var totalSrcMs = Math.Round(results.Sum(r => r.SrcMedianMs), 1);
        var totalCmpMs = Math.Round(results.Sum(r => r.CmpMedianMs), 1);
        var totalDiff = Math.Round(totalSrcMs - totalCmpMs, 1);
        var avgSpeedup = totalCmpMs > 0 ? Math.Round(totalSrcMs / totalCmpMs, 2) : 0;

        var groups = results.GroupBy(r => r.Group).OrderBy(g => g.Key);

        var log = new
        {
            timestamp = DateTime.Now.ToString("o"),
            mode = "in-process",
            iterations,
            warmup,
            statistic = "median",
            testCount = results.Count,
            totalSrcMs,
            totalCmpMs,
            totalDiffMs = totalDiff,
            overallSpeedup = avgSpeedup,
            categories = groups.Select(g =>
            {
                var gSrc = Math.Round(g.Sum(r => r.SrcMedianMs), 1);
                var gCmp = Math.Round(g.Sum(r => r.CmpMedianMs), 1);
                return new
                {
                    category = g.Key,
                    srcMs = gSrc,
                    cmpMs = gCmp,
                    speedup = gCmp > 0 ? Math.Round(gSrc / gCmp, 2) : 0
                };
            }).ToArray(),
            tests = results.Select(r => new
            {
                group = r.Group,
                name = r.Name,
                srcMedianMs = r.SrcMedianMs,
                cmpMedianMs = r.CmpMedianMs,
                diffMs = r.DiffMs,
                speedup = r.Speedup,
                srcStdDev = r.SrcStdDev,
                cmpStdDev = r.CmpStdDev,
                srcTimes = r.SrcTimes,
                cmpTimes = r.CmpTimes
            }).ToArray()
        };

        var json = JsonSerializer.Serialize(log, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });

        File.WriteAllText(logFile, json);
        WriteColored($"  Log: {logFile}", ConsoleColor.DarkGray);
        Console.WriteLine();
    }

    private static void WriteColored(string text, ConsoleColor color)
    {
        Console.ForegroundColor = color;
        Console.WriteLine(text);
        Console.ResetColor();
    }

    private static void WriteColoredInline(string text, ConsoleColor color)
    {
        Console.ForegroundColor = color;
        Console.Write(text);
        Console.ResetColor();
    }

    private static void WriteError(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"ERROR: {message}");
        Console.ResetColor();
    }

    private static void WriteWarning(string message)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"WARNING: {message}");
        Console.ResetColor();
    }
}
