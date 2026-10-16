using System.Text.RegularExpressions;
using Elynt.Core.Repair;

namespace Elynt.Core.Tests;

/// <summary>An in-memory PC for repair tools: scripted program output and a set of files per folder.</summary>
public sealed class FakeRepairSystem : IRepairSystem
{
    private readonly List<(string Prefix, int ExitCode, string[] Lines)> _scripts = [];
    private readonly HashSet<string> _folders = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> _files = new(StringComparer.OrdinalIgnoreCase);

    public string SystemDrive { get; set; } = "C:";

    /// <summary>Every program run, as "program arg1 arg2", and every folder cleared, as "clear ...", in order.</summary>
    public List<string> Actions { get; } = [];

    /// <summary>Runs whose command line starts with <paramref name="commandPrefix"/> print these lines and exit with this code. Unscripted runs print nothing and exit 0.</summary>
    public void Script(string commandPrefix, int exitCode, params string[] lines) => _scripts.Insert(0, (commandPrefix, exitCode, lines));

    /// <summary>Runs whose command line starts with this prefix throw, as if the program were missing.</summary>
    public void Missing(string commandPrefix) => _scripts.Insert(0, (commandPrefix, int.MinValue, []));

    /// <summary>The next run with this prefix blocks until cancelled.</summary>
    public void Hang(string commandPrefix) => _scripts.Insert(0, (commandPrefix, int.MaxValue, []));

    public void AddFile(RepairFolder root, string path, bool inUse = false)
    {
        _files[Key(root, path)] = inUse;
        var folder = Path.GetDirectoryName(path.Replace('\\', '/'))?.Replace('/', '\\') ?? "";
        while (true)
        {
            _folders.Add(Key(root, folder));
            if (folder.Length == 0)
                break;
            folder = Path.GetDirectoryName(folder.Replace('\\', '/'))?.Replace('/', '\\') ?? "";
        }
    }

    public bool FileExists(RepairFolder root, string path) => _files.ContainsKey(Key(root, path));

    public async Task<int> RunAsync(ProgramRun run, Action<string> output, CancellationToken cancellation)
    {
        var commandLine = run.ToString();
        Actions.Add(commandLine);
        var script = _scripts.FirstOrDefault(s => commandLine.StartsWith(s.Prefix, StringComparison.OrdinalIgnoreCase));
        if (script.Prefix is null)
            return 0;
        if (script.ExitCode == int.MinValue)
            throw new FileNotFoundException($"{run.Program} wasn't found.");
        if (script.ExitCode == int.MaxValue)
        {
            await Task.Delay(Timeout.Infinite, cancellation);
        }
        foreach (var line in script.Lines)
            output(line);
        return script.ExitCode;
    }

    public bool FolderExists(RepairFolder root, string relativePath, string user) => _folders.Contains(Key(root, relativePath));

    public FolderClearResult ClearFolder(RepairFolder root, string relativePath, string pattern, bool includeSubfolders, string user)
    {
        Actions.Add($@"clear {root}\{relativePath}\{pattern}{(includeSubfolders ? " (with subfolders)" : "")}");
        var prefix = Key(root, relativePath) + @"\";
        var match = new Regex("^" + Regex.Escape(pattern).Replace(@"\*", ".*") + "$", RegexOptions.IgnoreCase);
        int removed = 0, inUse = 0;
        foreach (var (file, locked) in _files.ToList())
        {
            if (!file.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;
            var rest = file[prefix.Length..];
            if (rest.Contains('\\') && !includeSubfolders)
                continue;
            if (!match.IsMatch(rest.Split('\\')[^1]))
                continue;
            if (locked)
            {
                inUse++;
                continue;
            }
            _files.Remove(file);
            removed++;
        }
        return new(removed, inUse);
    }

    private static string Key(RepairFolder root, string path) => path.Length == 0 ? $"{root}" : $@"{root}\{path}";
}
