using System.Text;

namespace Elynt.Core;

/// <summary>Writes the app's JSON state files so a crash leaves either the old file or the new one, never half of one.</summary>
internal static class AtomicFile
{
    /// <summary>
    /// Writes <paramref name="bytes"/> to a temporary file next to <paramref name="path"/>, flushes it to disk, then
    /// moves it over <paramref name="path"/>. Creates the folder when needed. Returns only once the data is on disk.
    /// </summary>
    public static void Write(string path, ReadOnlySpan<byte> bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }
        File.Move(temp, path, overwrite: true);
    }

    public static void Write(string path, string text) => Write(path, Encoding.UTF8.GetBytes(text));
}
