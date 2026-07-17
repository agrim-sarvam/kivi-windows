using System.Diagnostics.CodeAnalysis;

namespace Kivi.App;

/// <summary>
/// Minimal .env loader (no external dependency). Reads KEY=VALUE lines from a local
/// <c>.env</c> file and sets them as process environment variables, so the standard
/// <c>AddEnvironmentVariables()</c> configuration source picks them up.
/// Never logs values (keys can be secrets). The .env file is git-ignored.
/// </summary>
public static class DotEnv
{
    /// <summary>
    /// Loads the nearest <c>.env</c> file, searching the current directory and walking up
    /// to the repo/solution root. Existing environment variables are NOT overwritten
    /// (a real process/CI env var wins over the file). Missing file is a no-op.
    /// </summary>
    public static void Load(string fileName = ".env")
    {
        if (!TryFindEnvFile(fileName, out var path))
            return;

        foreach (var rawLine in File.ReadAllLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line[0] == '#')
                continue;

            var eq = line.IndexOf('=');
            if (eq <= 0)
                continue; // no key, or no '=' — skip malformed line

            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();

            // Strip a single layer of surrounding quotes, if present.
            if (value.Length >= 2 &&
                ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
            {
                value = value[1..^1];
            }

            if (key.Length == 0)
                continue;

            // Do not clobber a value already set in the real environment.
            if (Environment.GetEnvironmentVariable(key) is null)
                Environment.SetEnvironmentVariable(key, value);
        }
    }

    private static bool TryFindEnvFile(string fileName, [NotNullWhen(true)] out string? path)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, fileName);
            if (File.Exists(candidate))
            {
                path = candidate;
                return true;
            }
            dir = dir.Parent;
        }

        // Also try the current working directory (e.g. `dotnet run` from the repo root).
        var cwdCandidate = Path.Combine(Directory.GetCurrentDirectory(), fileName);
        if (File.Exists(cwdCandidate))
        {
            path = cwdCandidate;
            return true;
        }

        path = null;
        return false;
    }
}
