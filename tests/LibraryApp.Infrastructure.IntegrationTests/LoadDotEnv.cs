using System.Runtime.CompilerServices;

namespace LibraryApp.Infrastructure.IntegrationTests;

/// <summary>Loads the repo-root .env for local runs. Variables already set (CI) win; no .env means nothing happens.</summary>
internal static class LoadDotEnv
{
    [ModuleInitializer]
    internal static void Load() => DotNetEnv.Env.TraversePath().NoClobber().Load();
}