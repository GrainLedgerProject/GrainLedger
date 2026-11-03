using DotNetEnv;

namespace GrainLedger.Api.Configuration;

public static class LocalEnvironmentConfiguration
{
    public static void AddLocalEnvironmentFile(this IConfigurationBuilder configuration, string contentRoot, bool development)
    {
        var directory = new DirectoryInfo(contentRoot);
        if (development)
        {
            // Locate this repository, without loading unrelated parent-directory secrets.
            for (var candidate = directory; candidate is not null; candidate = candidate.Parent)
            {
                if (!File.Exists(Path.Combine(candidate.FullName, "GrainLedger.slnx"))) continue;
                directory = candidate;
                break;
            }
        }

        var path = Path.Combine(directory.FullName, ".env");
        if (!File.Exists(path)) return;
        try
        {
            var values = Env.NoEnvVars().Load(path).GroupBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase).Select(group => group.Last());
            configuration.AddInMemoryCollection(values.Select(pair =>
                new KeyValuePair<string, string?>(pair.Key.Replace("__", ":"), pair.Value)));
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // Parser errors may quote secrets from the input. Do not attach the original exception.
            throw new InvalidOperationException("Cannot load .env. Check file access and KEY='value' syntax.");
        }
    }
}
