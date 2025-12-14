using System;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace GroundedIndicator;

[HarmonyPatch(typeof(StickPositioner))]
public class StickPositionerPatch
{
  public enum SurfaceType
  {
    Opaque,
    Transparent
  }
  
  private static float stickOpacity = 0.5f;
  private static float stickGroundOffset = 0.42f;

  static readonly Harmony harmony = new Harmony("wenright.GroundedIndicator");
  public static Shader originalSimpleShader;
  public static Shader originalComplexShader;
  public static Shader transparentShader;
  public static Texture stickTexture;
  public static bool localStickOnGround = false;
  public static Material materialReference;
  public static bool useComplexShader = false;

  [HarmonyPostfix]
  [HarmonyPatch("Awake")]
  public static void PostfixAwake()
  {
    materialReference = null;
    transparentShader = Shader.Find("Universal Render Pipeline/Lit");
    originalSimpleShader = Shader.Find("Shader Graphs/Stick Simple");
    originalComplexShader = Shader.Find("Shader Graphs/Stick Complex");
  }

  [HarmonyPostfix]
  [HarmonyPatch("FixedUpdate")]
  public static void PostfixFixedUpdate(StickPositioner __instance)
  {
    if (!__instance.IsOwner) return;

    LayerMask layerMask = LayerMask.GetMask("Ice");
    Vector3 start = __instance.Stick.BladeHandlePosition + __instance.transform.up * 0.25f;
    Vector3 direction = -__instance.Stick.transform.up;
    float distance = stickGroundOffset;
    if (Physics.Raycast(start, direction, distance, layerMask))
    {
      if (localStickOnGround) return;

      localStickOnGround = true;
      SetTransparency(__instance.Stick, 1f);
    }
    else
    {
      if (!localStickOnGround) return;

      localStickOnGround = false;
      SetTransparency(__instance.Stick, stickOpacity);
    }
  }

  private static void SetTransparency(Stick stick, float alpha)
  {
    if (materialReference == null)
    {
      MeshRenderer MeshRenderer = Traverse.Create(stick.StickMesh).Field("stickMeshRenderer").GetValue() as MeshRenderer;
      materialReference = MeshRenderer.material;
      useComplexShader = materialReference.shader.name.Contains("Complex");
    }

    if (alpha <= 0.95)
    {
      materialReference.shader = transparentShader;
      materialReference.SetTexture("_BaseMap", stickTexture);
      materialReference.SetOverrideTag("RenderType", "Transparent");
      materialReference.SetFloat("_Surface", (float)SurfaceType.Transparent);
      materialReference.SetFloat("_Blend", (float)BlendMode.SrcAlpha);
      materialReference.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
      materialReference.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
      materialReference.SetInt("_ZWrite", 0);
      materialReference.EnableKeyword("_ALPHATEST_ON");
      materialReference.DisableKeyword("_ALPHAPREMULTIPLY_ON");
      materialReference.renderQueue = (int)RenderQueue.Transparent;

      Color color = materialReference.color;
      color.a = alpha;
      materialReference.color = color;
    }
    else
    {
      stickTexture = materialReference.GetTexture("_Texture");
      materialReference.shader = useComplexShader ?  originalComplexShader : originalSimpleShader;
      materialReference.SetOverrideTag("RenderType", "");
      materialReference.SetFloat("_Surface", (float)SurfaceType.Opaque);
      materialReference.SetInt("_SrcBlend", (int)BlendMode.One);
      materialReference.SetInt("_DstBlend", (int)BlendMode.Zero);
      materialReference.SetInt("_ZWrite", 1);
      materialReference.DisableKeyword("_ALPHAPREMULTIPLY_ON");
      materialReference.renderQueue = -1;
    }
  }

  public static void ConfigureSettings()
  {
    stickOpacity = Plugin.modSettings.StickOpacity;
    stickGroundOffset = Plugin.modSettings.StickGroundOffset;
    
    Debug.Log($"Loading settings. Opacity: {stickOpacity}, Offset: {stickGroundOffset}");
  }
}