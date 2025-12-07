using System.Text.Json;
using System.Text.Json.Serialization;
using OpenFXC.Ir;

namespace OpenFXC.Ir;

internal static class Program
{
    private const int InternalErrorExitCode = 1;
    private const int SuccessExitCode = 0;

    public static int Main(string[] args)
    {
        try
        {
            return Run(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return InternalErrorExitCode;
        }
    }

    private static int Run(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return InternalErrorExitCode;
        }

        var verb = args[0];
        var rest = args[1..];

        return verb.ToLowerInvariant() switch
        {
            "lower" => RunLower(rest),
            "optimize" => RunOptimize(rest),
            _ => FailWithUsage()
        };
    }

    private static int FailWithUsage()
    {
        PrintUsage();
        return InternalErrorExitCode;
    }

    private static int RunLower(string[] args)
    {
        var options = ParseLowerOptions(args);
        if (!options.IsValid(out var error))
        {
            Console.Error.WriteLine(error);
            PrintUsage();
            return InternalErrorExitCode;
        }

        var semanticJson = ReadAllInput(options.InputPath);
        var pipeline = new LoweringPipeline();
        var request = new LoweringRequest(semanticJson, options.Profile, options.Entry ?? "main");
        var module = pipeline.Lower(request);

        return WriteModuleAndExit(module);
    }

    private static int RunOptimize(string[] args)
    {
        var options = ParseOptimizeOptions(args);
        if (!options.IsValid(out var error))
        {
            Console.Error.WriteLine(error);
            PrintUsage();
            return InternalErrorExitCode;
        }

        var irJson = ReadAllInput(options.InputPath);
        var pipeline = new OptimizePipeline();
        var request = new OptimizeRequest(irJson, options.Passes, options.Profile);
        var module = pipeline.Optimize(request);

        return WriteModuleAndExit(module);
    }

    private static int WriteModuleAndExit(IrModule module)
    {
        var writerOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        Console.Out.Write(JsonSerializer.Serialize(module, writerOptions));
        Console.Out.WriteLine();
        return SuccessExitCode;
    }

    private static LowerOptions ParseLowerOptions(string[] args)
    {
        string? profile = null;
        string? entry = null;
        string? input = null;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--profile":
                case "-p":
                    profile = NextValue(args, ref i);
                    break;
                case "--entry":
                case "-e":
                    entry = NextValue(args, ref i);
                    break;
                case "--input":
                case "-i":
                    input = NextValue(args, ref i);
                    break;
                default:
                    break;
            }
        }

        return new LowerOptions(profile, entry, input);
    }

    private static OptimizeOptions ParseOptimizeOptions(string[] args)
    {
        string? profile = null;
        string? input = null;
        string? passes = null;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--profile":
                case "-p":
                    profile = NextValue(args, ref i);
                    break;
                case "--input":
                case "-i":
                    input = NextValue(args, ref i);
                    break;
                case "--passes":
                    passes = NextValue(args, ref i);
                    break;
                default:
                    break;
            }
        }

        return new OptimizeOptions(profile, input, passes);
    }

    private static string? NextValue(string[] args, ref int index)
    {
        if (index + 1 >= args.Length)
        {
            return null;
        }

        index++;
        return args[index];
    }

    private static string ReadAllInput(string? inputPath)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            return Console.In.ReadToEnd();
        }

        return File.ReadAllText(inputPath);
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine("Usage: openfxc-ir lower [--profile <name>] [--entry <name>] [--input <path>] < input.sem.json > output.ir.json");
    }

    private sealed record LowerOptions(string? Profile, string? Entry, string? InputPath)
    {
        public bool IsValid(out string? error)
        {
            if (!string.IsNullOrWhiteSpace(InputPath) && !File.Exists(InputPath))
            {
                error = $"Input file not found: {InputPath}";
                return false;
            }

            error = null;
            return true;
        }
    }

    private sealed record OptimizeOptions(string? Profile, string? InputPath, string? Passes)
    {
        public bool IsValid(out string? error)
        {
            if (!string.IsNullOrWhiteSpace(InputPath) && !File.Exists(InputPath))
            {
                error = $"Input file not found: {InputPath}";
                return false;
            }

            error = null;
            return true;
        }
    }
}
