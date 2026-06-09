namespace Atelier.Build.Utils;

public static class ShellTokenizer
{
    public static IReadOnlyList<string> Tokenize(string command)
    {
        var tokens = new List<string>();
        var current = new List<char>();
        var inToken = false;
        var quote = '\0';
        var escaped = false;

        foreach (var c in command)
        {
            if (escaped)
            {
                current.Add(c);
                inToken = true;
                escaped = false;
                continue;
            }

            if (quote == '\'')
            {
                if (c == '\'')
                {
                    quote = '\0';
                }
                else
                {
                    current.Add(c);
                }

                continue;
            }

            if (quote == '"')
            {
                if (c == '\\')
                {
                    escaped = true;
                }
                else if (c == '"')
                {
                    quote = '\0';
                }
                else
                {
                    current.Add(c);
                }

                continue;
            }

            if (c == '\\')
            {
                escaped = true;
                inToken = true;
                continue;
            }

            if (c == '\'' || c == '"')
            {
                quote = c;
                inToken = true;
                continue;
            }

            if (c == ' ' || c == '\t' || c == '\r'
                || c == '\n')
            {
                if (inToken)
                {
                    tokens.Add(new string(current.ToArray()));
                    current.Clear();
                    inToken = false;
                }

                continue;
            }

            current.Add(c);
            inToken = true;
        }

        if (inToken)
        {
            tokens.Add(new string(current.ToArray()));
        }

        return tokens;
    }
}
