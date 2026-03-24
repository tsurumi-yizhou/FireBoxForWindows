using System.IO;
using Core.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Core.Com;

public static class ServiceRuntimeLog
{
    public static void WriteInfo(IServiceProvider? serviceProvider, string area, string message) =>
        WriteCore(serviceProvider, "INFO", area, message, null);

    public static void WriteError(IServiceProvider? serviceProvider, string area, Exception exception, string? message = null) =>
        WriteCore(serviceProvider, "ERROR", area, message, exception);

    private static void WriteCore(IServiceProvider? serviceProvider, string level, string area, string? message, Exception? exception)
    {
        try
        {
            var logPath = ResolveLogPath(serviceProvider);
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);

            var text = $"{DateTimeOffset.Now:O} [{level}] [{area}] {message ?? string.Empty}".TrimEnd();
            if (exception is not null)
                text += Environment.NewLine + exception + Environment.NewLine;
            else
                text += Environment.NewLine;

            File.AppendAllText(logPath, text);
        }
        catch
        {
        }
    }

    private static string ResolveLogPath(IServiceProvider? serviceProvider)
    {
        try
        {
            if (serviceProvider is not null)
            {
                var options = serviceProvider.GetService<FireBoxServiceOptions>();
                if (options is not null)
                    return options.ResolveComErrorLogPath();
            }
        }
        catch
        {
        }

        var baseDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FireBox");
        return Path.Combine(baseDir, "service-com-errors.log");
    }
}
