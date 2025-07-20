using System;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace GroundedIndicator;

[HarmonyPatch(typeof(StickPositioner))]
public class StickPositionerPatch : IPuckMod
{
  public enum SurfaceType
  {
    Opaque,
    Transparent
  }

  static readonly Harmony harmony = new Harmony("wenright.GroundedIndicator");
  public static Shader transparentShader;
  public static Texture stickTexture;
  public static Color stickColor;

  [HarmonyPostfix]
  [HarmonyPatch("Awake")]
  public static void PostfixAwake(StickPositioner __instance)
  {
    if (!__instance.IsLocalPlayer) return;
    
    stickTexture = null;
    transparentShader = Shader.Find("Universal Render Pipeline/Lit");
  }

  [HarmonyPostfix]
  [HarmonyPatch("OnGrounded")]
  public static void PostfixOnGrounded(StickPositioner __instance)
  {
    if (!__instance.IsLocalPlayer) return;

    SetTransparency(__instance.Stick, 1.0f);
  }

// TODO: This gets called every frame. Maybe just do a raycast in an update?
  [HarmonyPostfix]
  [HarmonyPatch("OnUngrounded")]
  public static void PostfixOnUngrounded(StickPositioner __instance)
  {
    if (!__instance.IsLocalPlayer) return;

    SetTransparency(__instance.Stick, 0.55f);
  }

  private static void SetTransparency(Stick stick, float alpha)
  {
    MeshRenderer MeshRenderer = Traverse.Create(stick.StickMesh).Field("stickMeshRenderer").GetValue() as MeshRenderer;
    Material material = MeshRenderer.material;
    Color color = material.color;
    color.a = alpha;
    material.color = color;

    if (stickTexture == null)
    {
      stickTexture = material.GetTexture("_Texture");
    }

    material.shader = transparentShader;
    material.SetTexture("_BaseMap", stickTexture);

    if (alpha <= 0.95)
    {
      material.SetOverrideTag("RenderType", "Transparent");
      material.SetFloat("_Surface", (float)SurfaceType.Transparent);
      material.SetFloat("_Blend", (float)BlendMode.SrcAlpha);
      material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
      material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
      material.SetInt("_ZWrite", 0);
      material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
      material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
    }
    else
    {
      material.SetOverrideTag("RenderType", "");
      material.SetFloat("_Surface", (float)SurfaceType.Opaque);
      material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
      material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
      material.SetInt("_ZWrite", 1);
      material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
      material.renderQueue = -1;
    }

    bool alphaClip = material.GetFloat("_AlphaClip") == 1;
    if (alphaClip)
    {
      material.EnableKeyword("_ALPHATEST_ON");
    }
    else
    {
      material.DisableKeyword("_ALPHATEST_ON");
    }
  }

  public bool OnEnable()
  {
    try
    {
      harmony.PatchAll();
    }
    catch (Exception e)
    {
      Debug.LogError($"Harmony patch failed: {e.Message}");

      return false;
    }

    return true;
  }

  public bool OnDisable()
  {
    try
    {
      harmony.UnpatchSelf();
    }
    catch (Exception e)
    {
      Debug.LogError($"Harmony unpatch failed: {e.Message}");

      return false;
    }

    return true;
  }
}