using System.Text;

namespace Elynt.Core.Diagnostics;

/// <summary>A kind of log to include: the newest file in <see cref="Folder"/> matching <see cref="Pattern"/>.</summary>
public sealed record LogSource(string Label, string Folder, string Pattern);

/// <summary>Builds the text of a bug report from the newest log of each kind, for "Copy app log". No telemetry: the user sends it.</summary>
public static class BugReport
{
    public static string Build(IEnumerable<LogSource> sources, int maxCharsPerLog = 200_000)
    {
        var report = new StringBuilder();
        foreach (var source in sources)
        {
            var newest = Directory.Exists(source.Folder)
                ? new DirectoryInfo(source.Folder).EnumerateFiles(source.Pattern).OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault()
                : null;
            if (newest is null)
            {
                report.AppendLine($"== {source.Label}: none found in {source.Folder}").AppendLine();
                continue;
            }

            report.AppendLine($"== {source.Label}: {newest.FullName}");
            try
            {
                var text = ReadShared(newest.FullName);
                if (text.Length > maxCharsPerLog)
                    text = "(earlier lines left out)" + Environment.NewLine + text[^maxCharsPerLog..];
                report.AppendLine(text);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                report.AppendLine($"Couldn't read it: {ex.Message}");
            }
            report.AppendLine();
        }
        return report.ToString();
    }

    /// <summary>Reads a log the logger still has open for writing.</summary>
    private static string ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
