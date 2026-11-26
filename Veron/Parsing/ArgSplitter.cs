using System;
using System.Collections.Generic;
using System.Text;

namespace Veron;

/// <summary>
/// Splits a command-line string into arguments using POSIX-shell-like rules:
/// whitespace separates arguments, '…' is literal, "…" allows \" and \\ escapes,
/// and a backslash outside quotes escapes the next character.
/// </summary>
public static class ArgSplitter
{
    public static List<string> Split(string input)
    {
        var result  = new List<string>();
        var current = new StringBuilder();
        bool inToken = false;
        int i = 0;

        while (i < input.Length)
        {
            char c = input[i];

            if (char.IsWhiteSpace(c))
            {
                if (inToken) { result.Add(current.ToString()); current.Clear(); inToken = false; }
                i++;
                continue;
            }

            inToken = true;

            if (c == '\'')
            {
                int end = input.IndexOf('\'', i + 1);
                if (end < 0) throw new FormatException("Unbalanced single quote in: " + input);
                current.Append(input, i + 1, end - i - 1);
                i = end + 1;
            }
            else if (c == '"')
            {
                i++;
                bool closed = false;
                while (i < input.Length)
                {
                    char d = input[i];
                    if (d == '"') { closed = true; i++; break; }
                    if (d == '\\' && i + 1 < input.Length && (input[i + 1] == '"' || input[i + 1] == '\\'))
                    { current.Append(input[i + 1]); i += 2; continue; }
                    current.Append(d);
                    i++;
                }
                if (!closed) throw new FormatException("Unbalanced double quote in: " + input);
            }
            else if (c == '\\' && i + 1 < input.Length)
            {
                current.Append(input[i + 1]);
                i += 2;
            }
            else
            {
                current.Append(c);
                i++;
            }
        }

        if (inToken) result.Add(current.ToString());
        return result;
    }
}
