namespace FinCore.Benchmarks;

internal sealed record MeasurementOptions(int Samples, int Warmup)
{
    /// <summary>
    /// Parses the supported sample and warm-up command-line options.
    /// </summary>
    /// <param name="args">Arguments following the benchmark mode switch.</param>
    /// <param name="defaultSamples">Sample count used when no sample option is supplied.</param>
    /// <param name="defaultWarmup">Warm-up count used when no warm-up option is supplied.</param>
    /// <returns>The validated sample and warm-up configuration.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when an option is unsupported or sample/warm-up values are outside the accepted bounds.
    /// </exception>
    public static MeasurementOptions Parse(
        IReadOnlyList<string> args,
        int defaultSamples = 1_000,
        int defaultWarmup = 50)
    {
        int samples = defaultSamples;
        int warmup = defaultWarmup;

        for (int index = 0; index < args.Count; index++)
        {
            string value = args[index];
            if (value == "--samples" && index + 1 < args.Count &&
                int.TryParse(args[++index], out int parsedSamples))
            {
                samples = parsedSamples;
                continue;
            }

            if (value == "--warmup" && index + 1 < args.Count &&
                int.TryParse(args[++index], out int parsedWarmup))
            {
                warmup = parsedWarmup;
                continue;
            }

            throw new ArgumentException(
                "Supported options are --samples <count> and --warmup <count>.");
        }

        if (samples < 100)
        {
            throw new ArgumentException(
                "At least 100 measured samples are required to report percentiles.",
                nameof(args));
        }

        if (warmup < 0 || warmup > 10_000)
        {
            throw new ArgumentException(
                "Warm-up must be between 0 and 10,000 calls.",
                nameof(args));
        }

        return new MeasurementOptions(samples, warmup);
    }
}
