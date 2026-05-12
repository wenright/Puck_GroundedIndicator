using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace GroundedIndicator;

public class ModSettings
{
  public float StickOpacity { get; set; } = 0.5f;
  public float StickGroundOffset { get; set; } = 0.42f;

  static string ConfigurationFileName = $"{Plugin.MOD_NAME}.json";

  public static ModSettings Load()
  {
    var path = GetConfigPath();
    var dir = Path.GetDirectoryName(path);

    if (!Directory.Exists(dir))
    {
      Directory.CreateDirectory(dir);
      Debug.Log($"Created missing /config directory");
    }

    if (File.Exists(path))
    {
      try
      {
        var json = File.ReadAllText(path);
        var settings = JsonConvert.DeserializeObject<ModSettings>(json);
        return settings ?? new ModSettings();
      }
      catch (JsonException je)
      {
        Debug.Log($"Corrupt config JSON, using defaults: {je.Message}");
        return new ModSettings();
      }
    }

    var defaults = new ModSettings();
    File.WriteAllText(path,
        JsonConvert.SerializeObject(defaults, Formatting.Indented));

    Debug.Log($"Config file `{path}` did not exist, created with defaults.");
    return defaults;
  }

  public void Save()
  {
    var path = GetConfigPath();
    var dir = Path.GetDirectoryName(path);

    if (!Directory.Exists(dir))
      Directory.CreateDirectory(dir);

    File.WriteAllText(path,
        JsonConvert.SerializeObject(this, Formatting.Indented));
  }

  public static string GetConfigPath()
  {
    string rootPath = Path.GetFullPath(".");
    string configPath = Path.Combine(rootPath, "config", ConfigurationFileName);
    return configPath;
  }
}
