using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace GroundedIndicator;

[HarmonyPatch(typeof(StickPositioner))]
public class StickPositionerPatch
{
  private const string BuiltInStickShaderName = "Shader Graphs/Stick";

  private static readonly int TextureColorId = Shader.PropertyToID("_Texture_Color");
  private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
  private static readonly int ColorId = Shader.PropertyToID("_Color");
  private static readonly int TextureId = Shader.PropertyToID("_Texture");
  private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
  private static readonly int MainTexId = Shader.PropertyToID("_MainTex");

  private static readonly Dictionary<int, StickVisualState> VisualStates = new();
  private static readonly HashSet<string> WarnedShaders = new();

  private static float stickOpacity = 0.5f;
  private static float stickGroundOffset = 0.42f;

  [HarmonyPostfix]
  [HarmonyPatch("FixedUpdate")]
  public static void PostfixFixedUpdate(StickPositioner __instance)
  {
    if (!__instance.IsOwner || !__instance.Stick) return;

    LayerMask layerMask = LayerMask.GetMask("Ice");
    Vector3 start = __instance.Stick.BladeHandlePosition + __instance.transform.up * 0.25f;
    Vector3 direction = -__instance.Stick.transform.up;
    bool isGrounded = Physics.Raycast(start, direction, stickGroundOffset, layerMask);

    GetVisualState(__instance).SetOpacity(isGrounded ? 1f : stickOpacity);
  }

  [HarmonyPostfix]
  [HarmonyPatch("OnNetworkDespawn")]
  public static void PostfixOnNetworkDespawn(StickPositioner __instance)
  {
    int instanceId = __instance.GetInstanceID();
    if (!VisualStates.TryGetValue(instanceId, out StickVisualState state)) return;

    state.Dispose();
    VisualStates.Remove(instanceId);
  }

  private static StickVisualState GetVisualState(StickPositioner positioner)
  {
    int instanceId = positioner.GetInstanceID();
    MeshRenderer renderer = Traverse.Create(positioner.Stick.StickMesh)
      .Field("stickMeshRenderer")
      .GetValue<MeshRenderer>();

    if (VisualStates.TryGetValue(instanceId, out StickVisualState state) && state.Renderer == renderer)
    {
      return state;
    }

    state?.Dispose();
    state = new StickVisualState(renderer);
    VisualStates[instanceId] = state;
    return state;
  }

  public static void RestoreAll()
  {
    foreach (StickVisualState state in VisualStates.Values)
    {
      state.Dispose();
    }

    VisualStates.Clear();
  }

  public static void ConfigureSettings()
  {
    stickOpacity = Plugin.modSettings.StickOpacity;
    stickGroundOffset = Plugin.modSettings.StickGroundOffset;

    Debug.Log($"[{Plugin.MOD_NAME}] Settings loaded. Opacity: {stickOpacity}, Offset: {stickGroundOffset}");
  }

  private sealed class StickVisualState : IDisposable
  {
    public MeshRenderer Renderer { get; }

    private readonly MaterialPropertyBlock originalPropertyBlock = new();
    private readonly MaterialPropertyBlock workingPropertyBlock = new();

    private Material originalMaterial;
    private Material fallbackMaterial;
    private int colorPropertyId = -1;
    private Color originalColor = Color.white;
    private float appliedOpacity = -1f;
    private bool isFaded;
    private bool usingFallback;

    public StickVisualState(MeshRenderer renderer)
    {
      Renderer = renderer;
      CaptureMaterial(renderer ? renderer.sharedMaterial : null);
    }

    public void SetOpacity(float opacity)
    {
      if (!Renderer) return;

      opacity = Mathf.Clamp01(opacity);
      DetectMaterialChange();

      bool shouldFade = opacity < 0.999f;
      if (shouldFade == isFaded && Mathf.Approximately(opacity, appliedOpacity)) return;

      if (!shouldFade)
      {
        Restore();
        appliedOpacity = opacity;
        return;
      }

      if (!isFaded)
      {
        RefreshBaseline();
      }

      if (CanFadeWithPropertyBlock(originalMaterial))
      {
        ApplyPropertyBlockOpacity(opacity);
      }
      else
      {
        ApplyFallbackOpacity(opacity);
      }

      isFaded = true;
      appliedOpacity = opacity;
    }

    private void DetectMaterialChange()
    {
      Material currentMaterial = Renderer.sharedMaterial;
      if (usingFallback && currentMaterial == fallbackMaterial) return;
      if (!usingFallback && currentMaterial == originalMaterial) return;

      // StickMesh.SetSkinID replaces material slot zero. Remove our property
      // override before taking the newly selected skin as the new baseline.
      Renderer.SetPropertyBlock(originalPropertyBlock);
      DestroyFallback();
      CaptureMaterial(currentMaterial);
      isFaded = false;
      appliedOpacity = -1f;
    }

    private void CaptureMaterial(Material material)
    {
      originalMaterial = material;
      RefreshBaseline();
    }

    private void RefreshBaseline()
    {
      colorPropertyId = FindColorProperty(originalMaterial);
      originalColor = colorPropertyId >= 0 ? originalMaterial.GetColor(colorPropertyId) : Color.white;
      originalPropertyBlock.Clear();
      if (Renderer) Renderer.GetPropertyBlock(originalPropertyBlock);
    }

    private bool CanFadeWithPropertyBlock(Material material)
    {
      if (!material || colorPropertyId < 0) return false;

      // The built-in stick graph is tagged Transparent and exposes a color
      // property, but does not connect that property's alpha to its output
      if (material.shader && material.shader.name == BuiltInStickShaderName) return false;

      string renderType = material.GetTag("RenderType", false, string.Empty);
      return renderType.Equals("Transparent", StringComparison.OrdinalIgnoreCase) ||
             material.renderQueue >= (int)RenderQueue.Transparent;
    }

    private void ApplyPropertyBlockOpacity(float opacity)
    {
      DestroyFallback();
      Renderer.GetPropertyBlock(workingPropertyBlock);

      Color color = originalColor;
      color.a *= opacity;
      workingPropertyBlock.SetColor(colorPropertyId, color);
      Renderer.SetPropertyBlock(workingPropertyBlock);
    }

    private void ApplyFallbackOpacity(float opacity)
    {
      if (!fallbackMaterial)
      {
        fallbackMaterial = CreateFallbackMaterial(originalMaterial);
      }

      if (!fallbackMaterial)
      {
        WarnOnce(originalMaterial);
        return;
      }

      Color color = fallbackMaterial.GetColor(BaseColorId);
      color.a = originalColor.a * opacity;
      fallbackMaterial.SetColor(BaseColorId, color);

      Renderer.SetPropertyBlock(originalPropertyBlock);
      Renderer.sharedMaterial = fallbackMaterial;
      usingFallback = true;
    }

    private void Restore()
    {
      if (!Renderer) return;

      if (usingFallback && originalMaterial)
      {
        Renderer.sharedMaterial = originalMaterial;
      }

      Renderer.SetPropertyBlock(originalPropertyBlock);
      DestroyFallback();
      isFaded = false;
    }

    public void Dispose()
    {
      Restore();
      originalMaterial = null;
    }

    private void DestroyFallback()
    {
      usingFallback = false;
      if (!fallbackMaterial) return;

      UnityEngine.Object.Destroy(fallbackMaterial);
      fallbackMaterial = null;
    }

    private static Material CreateFallbackMaterial(Material source)
    {
      Shader shader = Shader.Find("Universal Render Pipeline/Lit");
      if (!shader) return null;

      Material material = source ? new Material(source) : new Material(shader);
      material.shader = shader;
      material.name = $"{source?.name ?? "Stick"} (GroundedIndicator Transparent)";
      material.renderQueue = (int)RenderQueue.Transparent;

      Texture texture = FindTexture(source);
      if (texture)
      {
        material.SetTexture(BaseMapId, texture);

        int sourceTextureId = FindTextureProperty(source);
        if (sourceTextureId >= 0)
        {
          material.SetTextureScale("_BaseMap", source.GetTextureScale(sourceTextureId));
          material.SetTextureOffset("_BaseMap", source.GetTextureOffset(sourceTextureId));
        }
      }

      int sourceColorId = FindColorProperty(source);
      Color color = sourceColorId >= 0 ? source.GetColor(sourceColorId) : Color.white;
      material.SetColor(BaseColorId, color);

      material.SetOverrideTag("RenderType", "Transparent");
      material.SetFloat("_Surface", 1f);
      material.SetFloat("_Blend", 0f);
      material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
      material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
      material.SetInt("_SrcBlendAlpha", (int)BlendMode.One);
      material.SetInt("_DstBlendAlpha", (int)BlendMode.OneMinusSrcAlpha);
      material.SetInt("_ZWrite", 0);
      material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
      material.DisableKeyword("_ALPHATEST_ON");
      material.DisableKeyword("_ALPHAPREMULTIPLY_ON");

      return material;
    }

    private static int FindColorProperty(Material material)
    {
      if (!material) return -1;
      if (material.HasProperty(TextureColorId)) return TextureColorId;
      if (material.HasProperty(BaseColorId)) return BaseColorId;
      if (material.HasProperty(ColorId)) return ColorId;
      return -1;
    }

    private static Texture FindTexture(Material material)
    {
      int texturePropertyId = FindTextureProperty(material);
      return texturePropertyId >= 0 ? material.GetTexture(texturePropertyId) : null;
    }

    private static int FindTextureProperty(Material material)
    {
      if (!material) return -1;
      if (material.HasProperty(TextureId)) return TextureId;
      if (material.HasProperty(BaseMapId)) return BaseMapId;
      if (material.HasProperty(MainTexId)) return MainTexId;
      return -1;
    }

    private static void WarnOnce(Material material)
    {
      string shaderName = material && material.shader ? material.shader.name : "<missing>";
      if (!WarnedShaders.Add(shaderName)) return;

      Debug.LogWarning($"[{Plugin.MOD_NAME}] Could not make stick shader '{shaderName}' transparent; " +
                       "the original material was left unchanged.");
    }
  }
}
