using EksimSafeCopy.Benchmark.Runner;

var datasetPath = args.Length > 0 ? args[0] : string.Empty;
var outputDir = args.Length > 1 ? args[1] : Path.Combine(Directory.GetCurrentDirectory(), "benchmark-output");

Console.WriteLine($"Dataset: {(string.IsNullOrWhiteSpace(datasetPath) ? "(auto-locate)" : datasetPath)}");
Console.WriteLine($"Output: {outputDir}");

var runner = BenchmarkRunner.CreateDefault();
try
{
    var result = runner.Run(datasetPath);
    BenchmarkRunner.PrintConsoleSummary(result);
    BenchmarkRunner.WriteOutputs(result, outputDir);
    Console.WriteLine($"Results written to {outputDir}/benchmark-results.json and benchmark-report.md");
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Benchmark failed: {ex.Message}");
    Console.Error.WriteLine(ex.StackTrace);
    Environment.Exit(1);
}
