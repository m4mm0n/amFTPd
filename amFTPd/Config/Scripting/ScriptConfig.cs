using System.Text.Json;

namespace amFTPd.Config.Scripting
{
    /// <summary>
    /// Represents the configuration for a script, including the path to the rules file.
    /// </summary>
    /// <remarks>This record is immutable and provides methods to load and save configuration data in JSON
    /// format. The default rules path is "config/rules" if no valid configuration is provided.</remarks>
    /// <param name="RulesPath">The relative or absolute path to the rules file used by the script.</param>
    public sealed record ScriptConfig(string RulesPath)
    {
        public static ScriptConfig Load(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    // Default: relative to app base dir
                    var def = new ScriptConfig("config/rules");
                    Save(path, def);
                    return def;
                }

                var json = File.ReadAllText(path);
                var cfg = JsonSerializer.Deserialize<ScriptConfig>(json);
                if (cfg is null || string.IsNullOrWhiteSpace(cfg.RulesPath))
                    return new ScriptConfig("config/rules");

                return cfg;
            }
            catch
            {
                // On any error, fall back to a safe default
                return new ScriptConfig("config/rules");
            }
        }

        public static void Save(string path, ScriptConfig cfg)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(cfg, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(path, json);
        }
    }
}
