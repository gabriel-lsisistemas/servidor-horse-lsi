namespace ServidorHorseLsi.Api.Infrastructure;

public static class IniConfigurationReader
{
    public static bool SectionExists(string iniPath, string section)
    {
        if (!File.Exists(iniPath))
            return false;

        foreach (var rawLine in File.ReadAllLines(iniPath))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#'))
                continue;

            if (!line.StartsWith('[') || !line.EndsWith(']'))
                continue;

            var name = line[1..^1].Trim();
            if (name.Equals(section, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public static IReadOnlyDictionary<string, string> ReadSection(string iniPath, string section)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(iniPath))
            return result;

        var inSection = false;
        foreach (var rawLine in File.ReadAllLines(iniPath))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#'))
                continue;

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                var name = line[1..^1].Trim();
                inSection = name.Equals(section, StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inSection)
                continue;

            var eq = line.IndexOf('=');
            if (eq <= 0)
                continue;

            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();
            result[key] = value;
        }

        return result;
    }

    public static string BuildSqlConnectionString(string server, string database, string user, string password) =>
        SqlConnectionStringHelper.Build(server, database, user, password);
}
