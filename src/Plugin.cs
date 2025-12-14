using System;
using HarmonyLib;
using UnityEngine;

namespace GroundedIndicator;

public class Plugin : IPuckMod
{
  public static string MOD_NAME = "GroundedIndicator";
  public static string MOD_VERSION = "0.3.0";
  public static string MOD_GUID = "wenright.GroundedIndicator";

  static readonly Harmony harmony = new Harmony(MOD_GUID);

  public static ModSettings modSettings;

  public bool OnEnable()
  {
    try
    {
      harmony.PatchAll();

      Debug.Log($"Enabled {MOD_GUID}");

      modSettings = ModSettings.Load();
      modSettings.Save();
      StickPositionerPatch.ConfigureSettings();

      return true;
    }
    catch (Exception e)
    {
      Debug.LogError($"Failed enabling {MOD_GUID}: {e}");
      return false;
    }
  }

  public bool OnDisable()
  {
    try
    {
      harmony.UnpatchSelf();
      return true;
    }
    catch (Exception e)
    {
      Debug.LogError($"Failed to disable {MOD_GUID}: {e.Message}!");
      return false;
    }
  }
}