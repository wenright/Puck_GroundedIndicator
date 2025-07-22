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
  public static Shader originalShader;
  public static Shader transparentShader;
  public static Texture stickTexture;
  public static bool localStickOnGround = false;

  public static LineRenderer lineRenderer;

  [HarmonyPostfix]
  [HarmonyPatch("Awake")]
  public static void PostfixAwake(StickPositioner __instance)
  {
    transparentShader = Shader.Find("Universal Render Pipeline/Lit");
    originalShader = Shader.Find("Shader Graphs/Stick Simple");

    GameObject gameObject = new GameObject("Line Renderer for GroundedDetector");
    lineRenderer = gameObject.AddComponent<LineRenderer>();
    lineRenderer.material = new Material(Shader.Find("Sprites/Default"));

    lineRenderer.startColor = Color.red;
    lineRenderer.endColor = Color.cyan;

    lineRenderer.startWidth = 0.025f;
    lineRenderer.endWidth = 0.025f;

    lineRenderer.positionCount = 2;
    lineRenderer.useWorldSpace = true;
  }

  [HarmonyPostfix]
  [HarmonyPatch("FixedUpdate")]
  public static void PostfixFixedUpdate(StickPositioner __instance)
  {
    if (!__instance.IsOwner) return;

    LayerMask layerMask = LayerMask.GetMask("Ice");
    Vector3 start = __instance.Stick.BladeHandlePosition + __instance.transform.up * 0.25f;
    Vector3 direction = -__instance.Stick.transform.up;
    float distance = 0.42f;
    lineRenderer.SetPosition(0, start);
    lineRenderer.SetPosition(1, start + direction * distance);
    if (Physics.Raycast(start, direction, distance, layerMask))
    {
      if (!localStickOnGround)
      {
        localStickOnGround = true;
        SetTransparency(__instance.Stick, 1f);
      }
    }
    else
    {
      if (localStickOnGround)
      {
        localStickOnGround = false;
        SetTransparency(__instance.Stick, 0.5f);
      }
    }
  }

  private static void SetTransparency(Stick stick, float alpha)
  {
    Debug.Log($"Setting transparency to {alpha}");
    MeshRenderer MeshRenderer = Traverse.Create(stick.StickMesh).Field("stickMeshRenderer").GetValue() as MeshRenderer;
    Material material = MeshRenderer.material;

    if (stickTexture == null) stickTexture = material.GetTexture("_Texture");

    if (alpha <= 0.95)
    {
      material.shader = transparentShader;
      material.SetTexture("_BaseMap", stickTexture);
      material.SetOverrideTag("RenderType", "Transparent");
      material.SetFloat("_Surface", (float)SurfaceType.Transparent);
      material.SetFloat("_Blend", (float)BlendMode.SrcAlpha);
      material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
      material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
      material.SetInt("_ZWrite", 0);
      material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
      material.renderQueue = (int)RenderQueue.Transparent;
    }
    else
    {
      material.shader = originalShader;
      material.SetOverrideTag("RenderType", "");
      material.SetFloat("_Surface", (float)SurfaceType.Opaque);
      material.SetInt("_SrcBlend", (int)BlendMode.One);
      material.SetInt("_DstBlend", (int)BlendMode.Zero);
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

    // if (material.HasColor("_Color"))
    // {
    Color color = material.color;
    color.a = alpha;
    material.color = color;
    // }
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