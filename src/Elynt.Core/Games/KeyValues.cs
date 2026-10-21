using System.Text;

namespace Elynt.Core.Games;

/// <summary>
/// Reads Valve's text KeyValues format (libraryfolders.vdf, appmanifest_*.acf): quoted keys followed by a quoted
/// value or a braced block. Malformed input yields whatever was read before the problem, never an exception.
/// </summary>
internal sealed class KeyValues
{
    public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, KeyValues> Blocks { get; } = new(StringComparer.OrdinalIgnoreCase);

    public string? this[string key] => Values.GetValueOrDefault(key);

    public KeyValues? Block(string key) => Blocks.GetValueOrDefault(key);

    public static KeyValues Parse(string text)
    {
        var tokens = Tokenize(text).GetEnumerator();
        var root = new KeyValues();
        ReadBlock(tokens, root);
        return root;
    }

    private static void ReadBlock(IEnumerator<string?> tokens, KeyValues block)
    {
        // A null token is a brace: "{" opens a block, a null after a key's value position closes one.
        while (tokens.MoveNext())
        {
            if (tokens.Current is not { } key)
                return; // "}"
            if (!tokens.MoveNext())
                return;
            if (tokens.Current is { } value)
            {
                block.Values[key] = value;
                continue;
            }
            var child = new KeyValues();
            ReadBlock(tokens, child);
            block.Blocks[key] = child;
        }
    }

    /// <summary>Quoted strings as text; "{" and "}" both as null (a block opens after a key, closes otherwise).</summary>
    private static IEnumerable<string?> Tokenize(string text)
    {
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (c == '"')
            {
                var value = new StringBuilder();
                i++;
                while (i < text.Length && text[i] != '"')
                {
                    if (text[i] == '\\' && i + 1 < text.Length)
                    {
                        i++;
                        value.Append(text[i] switch { 'n' => '\n', 't' => '\t', _ => text[i] });
                    }
                    else
                    {
                        value.Append(text[i]);
                    }
                    i++;
                }
                i++;
                yield return value.ToString();
            }
            else if (c is '{' or '}')
            {
                i++;
                yield return null;
            }
            else if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                while (i < text.Length && text[i] != '\n')
                    i++;
            }
            else
            {
                i++;
            }
        }
    }
}
