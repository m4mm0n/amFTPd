/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           ScriptConfig.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-16 07:39:06
 *  Last Modified:  2025-12-09 19:20:10
 *  CRC32:          0x5C0BFE08
 *  
 *  Description:
 *      Represents the configuration for a script, including the path to the rules file.
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ==================================================================================================== */





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
        /// <summary>
        /// Loads a <see cref="ScriptConfig"/> instance from the specified file path.
        /// </summary>
        /// <remarks>If the specified file does not exist, a default configuration is created with the
        /// rules path set to "config/rules" and saved to the specified path. If the file exists but is invalid or
        /// cannot be read, the method falls back to returning a default configuration.</remarks>
        /// <param name="path">The path to the configuration file. If the file does not exist, a default configuration is created and saved
        /// to this path.</param>
        /// <returns>A <see cref="ScriptConfig"/> instance loaded from the specified file. If the file is invalid or an error
        /// occurs during loading, a default configuration is returned.</returns>
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
        /// <summary>
        /// Saves the specified <see cref="ScriptConfig"/> object to a file in JSON format.
        /// </summary>
        /// <remarks>The method serializes the <paramref name="cfg"/> object to JSON with indented
        /// formatting and writes it to the specified file.</remarks>
        /// <param name="path">The file path where the configuration will be saved. If the directory does not exist, it will be created.</param>
        /// <param name="cfg">The <see cref="ScriptConfig"/> object to save.</param>
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
