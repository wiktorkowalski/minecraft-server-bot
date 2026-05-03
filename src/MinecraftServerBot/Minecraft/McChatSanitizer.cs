namespace MinecraftServerBot.Minecraft;

public static class McChatSanitizer
{
    public static string OneLine(string raw, int maxLen)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return string.Empty;
        }

        var line = raw.Replace('\r', ' ').Replace('\n', ' ').Trim().Trim('"', '\'', '`');
        if (line.Length > maxLen)
        {
            line = line[..maxLen];
        }

        return line;
    }
}
