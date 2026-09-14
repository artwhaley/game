using System;
using System.IO;
using System.Linq;
using Daz3D;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace TruthCardGame.EditorTools
{
    /// <summary>
    /// Repeatable Phase 00 render setup. It deliberately delegates DTU parsing,
    /// texture import and material construction to the bundled Daz bridge.
    /// </summary>
    public static class Phase00HdrpMaterialSetup
    {
        private const string HdrpAssetPath = "Assets/Settings/TruthCardGameHDRP.asset";
        private const string DtuPath = "Assets/Daz3D/lara/lara.dtu";
        private const string FbxPath = "Assets/Daz3D/lara/lara.fbx";
        private static double startedAt;
        private static bool waiting;

        [MenuItem("TruthCardGame/Phase 00/Configure HDRP and Import Lara Materials")]
        public static void Run()
        {
            try
            {
                ConfigureHdrp();
                Phase00RigSetup.ConfigureImports();

                Daz3DDTUImporter.ResetOptions();
                Daz3DDTUImporter.AutoImportDTUChanges = false;
                Daz3DDTUImporter.GenerateUnityPrefab = true;
                Daz3DDTUImporter.ReplaceSceneInstances = false;
                Daz3DDTUImporter.AutomateMecanimAvatarMappings = false;
                Daz3DDTUImporter.ReplaceMaterials = true;
                Daz3DDTUImporter.EnableDForceSupport = false;
                Daz3DDTUImporter.UseLegacyShaders = false;
                Daz3DBridge.ReadyToImport = false;
                Daz3DBridge.BatchConversionMode = 1;

                startedAt = EditorApplication.timeSinceStartup;
                waiting = true;
                EditorApplication.update -= Poll;
                EditorApplication.update += Poll;
                Daz3DDTUImporter.Import(DtuPath, FbxPath);
                Debug.Log("[PHASE00-HDRP] Waiting for Daz texture/material conversion.");
            }
            catch (Exception exception)
            {
                Fail(exception);
            }
        }

        [MenuItem("TruthCardGame/Phase 00/Repair HDRP Materials and Rebuild Showcase")]
        public static void RepairUnity6HdrpMaterials()
        {
            try
            {
                ConfigureHdrp();
                ApplyUnity6HdrpFallbackMaterials();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                VerifyMaterialPrefab();
                VerifyFacialMaterialInvariants();
                Phase00RigSetup.PrepareCharacterFoundation();
                Phase00RigSetup.BuildShowcase();
                Debug.Log("[PHASE00-HDRP] COMPLETE: replaced legacy Daz Shader Graph materials with HDRP/Lit texture-preserving fallbacks.");
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Fail(exception);
            }
        }

        private static void ConfigureHdrp()
        {
            Directory.CreateDirectory("Assets/Settings");
            var asset = AssetDatabase.LoadAssetAtPath<HDRenderPipelineAsset>(HdrpAssetPath);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<HDRenderPipelineAsset>();
                asset.name = "TruthCardGameHDRP";
                AssetDatabase.CreateAsset(asset, HdrpAssetPath);
            }

            GraphicsSettings.defaultRenderPipeline = asset;
            // Every quality tier inherits the one project-level HDRP asset.
            QualitySettings.renderPipeline = null;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            QualitySettings.shadows = ShadowQuality.All;
            QualitySettings.shadowmaskMode = ShadowmaskMode.DistanceShadowmask;
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log("[PHASE00-HDRP] Active pipeline asset: " + HdrpAssetPath);
        }

        private static void Poll()
        {
            if (!waiting) return;
            if (EditorApplication.timeSinceStartup - startedAt > 600d)
            {
                Fail(new TimeoutException("Daz material conversion did not complete within ten minutes."));
                return;
            }
            if (!Daz3DBridge.ReadyToImport) return;

            waiting = false;
            EditorApplication.update -= Poll;
            Daz3DBridge.BatchConversionMode = -1;
            try
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                ApplyUnity6HdrpFallbackMaterials();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                VerifyMaterialPrefab();
                Phase00RigSetup.ReportInputs();
                Phase00RigSetup.PrepareCharacterFoundation();
                Phase00RigSetup.BuildShowcase();
                VerifyMaterialPrefab();
                Debug.Log("[PHASE00-HDRP] COMPLETE: HDRP active, Lara materials/textures bound, showcase rebuilt.");
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Fail(exception);
            }
        }

        private static void VerifyMaterialPrefab()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Phase00RigSetup.LaraMaterialPrefabPath);
            if (prefab == null)
                throw new InvalidOperationException("Daz importer did not create " + Phase00RigSetup.LaraMaterialPrefabPath);

            var renderers = prefab.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) throw new InvalidOperationException("Lara material prefab has no renderers.");
            var materials = renderers.SelectMany(renderer => renderer.sharedMaterials).ToArray();
            if (materials.Any(material => material == null))
                throw new InvalidOperationException("Lara material prefab contains an unassigned material slot.");
            if (materials.Any(material => material.shader == null || material.shader.name == "Hidden/InternalErrorShader"))
                throw new InvalidOperationException("Lara material prefab contains a missing/error shader.");

            var textured = materials.Count(material =>
                Enumerable.Range(0, material.shader.GetPropertyCount())
                    .Where(index => material.shader.GetPropertyType(index) == UnityEngine.Rendering.ShaderPropertyType.Texture)
                    .Select(index => material.shader.GetPropertyName(index))
                    .Any(property => material.GetTexture(property) != null));
            if (textured == 0) throw new InvalidOperationException("Lara materials were generated without any texture bindings.");

            var hdrp = materials.Count(material => material.shader.name.IndexOf("HDRP", StringComparison.OrdinalIgnoreCase) >= 0);
            Debug.Log($"[PHASE00-HDRP] Verified {renderers.Length} renderers, {materials.Length} slots, " +
                      $"{materials.Distinct().Count()} materials, {textured} textured slots, {hdrp} HDRP shader slots.");
        }

        private static void ApplyUnity6HdrpFallbackMaterials()
        {
            var fallback = Shader.Find("HDRP/Lit");
            if (fallback == null || !fallback.isSupported)
                throw new InvalidOperationException("Unity 6 HDRP/Lit is unavailable for Lara material fallback.");

            var materialDirectories = new[]
            {
                "Assets/Daz3D/lara/Materials",
                "Assets/Daz3D/202102genesis8hair/2021-02Hair_189597"
            };
            var guids = materialDirectories.SelectMany(directory => AssetDatabase.FindAssets("t:Material", new[] { directory })).Distinct().ToArray();
            var repaired = 0;
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null || material.shader == null ||
                    !material.shader.name.StartsWith("Daz3D/", StringComparison.Ordinal)) continue;

                // The bundled 2023 Daz Shader Graph assets render magenta under
                // Unity 6000/HDRP 17. Reuse the bridge-imported texture bindings
                // through the supported HDRP/Lit shader rather than losing the
                // generated Daz material assignments.
                var diffuse = GetTexture(material, "_DiffuseMap");
                var normal = GetTexture(material, "_NormalMap");
                var diffuseColor = GetColor(material, "_Diffuse", Color.white);
                var metallic = GetFloat(material, "_Metallic", 0f);
                var roughness = GetFloat(material, "_Roughness", 0.5f);
                var alphaCutout = GetFloat(material, "_AlphaCutoffEnable", 0f) > 0.5f;

                material.shader = fallback;
                material.shaderKeywords = Array.Empty<string>();
                material.SetColor("_BaseColor", diffuseColor);
                if (diffuse != null) material.SetTexture("_BaseColorMap", diffuse);
                if (normal != null)
                {
                    material.SetTexture("_NormalMap", normal);
                    material.SetFloat("_NormalScale", 1f);
                }
                material.SetFloat("_Metallic", metallic);
                material.SetFloat("_Smoothness", Mathf.Clamp01(1f - roughness));
                material.SetFloat("_SurfaceType", 0f);
                if (alphaCutout)
                {
                    material.SetFloat("_AlphaCutoffEnable", 1f);
                    material.SetFloat("_AlphaCutoff", 0.5f);
                }
                EditorUtility.SetDirty(material);
                repaired++;
            }
            Debug.Log($"[PHASE00-HDRP] Repaired {repaired} legacy Daz materials with HDRP/Lit fallbacks.");
            ConfigureLaraEyesAndLashes();
        }

        private static void ConfigureLaraEyesAndLashes()
        {
            var lashTexturePath = "Assets/Daz3D/lara/Textures/TWLaraLashes_T_1006.jpg";
            var lashTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(lashTexturePath);
            var lashImporter = AssetImporter.GetAtPath(lashTexturePath) as TextureImporter;
            if (lashTexture == null || lashImporter == null)
                throw new InvalidOperationException("Lara's eyelash opacity texture is missing: " + lashTexturePath);
            if (lashImporter.alphaSource != TextureImporterAlphaSource.FromGrayScale || !lashImporter.alphaIsTransparency)
            {
                lashImporter.alphaSource = TextureImporterAlphaSource.FromGrayScale;
                lashImporter.alphaIsTransparency = true;
                lashImporter.SaveAndReimport();
                lashTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(lashTexturePath);
            }

            var lash = LoadLaraMaterial("_Eyelashes.mat");
            lash.SetTexture("_BaseColorMap", lashTexture);
            lash.SetColor("_BaseColor", Color.black);
            lash.SetFloat("_AlphaCutoffEnable", 1f);
            lash.SetFloat("_AlphaCutoff", 0.18f);
            lash.renderQueue = (int)RenderQueue.AlphaTest;
            EditorUtility.SetDirty(lash);

            // Daz's cornea and tear geometry are refractive transparent shells.
            // The generic opaque HDRP fallback turned them into white surfaces
            // that occluded the iris and pupil underneath.
            ConfigureTransparentEyeShell("_Cornea.mat", 0.08f);
            ConfigureTransparentEyeShell("_EyeMoisture.mat", 0.06f);
            ConfigureTransparentEyeShell("_EyeMoisture_1.mat", 0.06f);
        }

        private static Material LoadLaraMaterial(string fileName)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Daz3D/lara/Materials/" + fileName);
            if (material == null) throw new InvalidOperationException("Missing Lara material: " + fileName);
            return material;
        }

        private static void ConfigureTransparentEyeShell(string fileName, float alpha)
        {
            var material = LoadLaraMaterial(fileName);
            material.SetFloat("_SurfaceType", 1f);
            material.SetFloat("_BlendMode", 0f);
            material.SetFloat("_AlphaCutoffEnable", 0f);
            material.SetFloat("_TransparentZWrite", 0f);
            material.SetColor("_BaseColor", new Color(1f, 1f, 1f, alpha));
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(material);
        }

        private static void VerifyFacialMaterialInvariants()
        {
            var lashes = LoadLaraMaterial("_Eyelashes.mat");
            if (lashes.GetTexture("_BaseColorMap") == null || lashes.GetFloat("_AlphaCutoffEnable") < 0.5f)
                throw new InvalidOperationException("Eyelash material must use its opacity texture and alpha clipping.");
            foreach (var shell in new[] { "_Cornea.mat", "_EyeMoisture.mat", "_EyeMoisture_1.mat" })
            {
                var material = LoadLaraMaterial(shell);
                if (material.GetFloat("_SurfaceType") < 0.5f)
                    throw new InvalidOperationException(shell + " must remain transparent so it does not hide Lara's iris and pupil.");
            }
        }

        private static Texture GetTexture(Material material, string property) =>
            material.HasProperty(property) ? material.GetTexture(property) : null;

        private static Color GetColor(Material material, string property, Color fallback) =>
            material.HasProperty(property) ? material.GetColor(property) : fallback;

        private static float GetFloat(Material material, string property, float fallback) =>
            material.HasProperty(property) ? material.GetFloat(property) : fallback;

        private static void Fail(Exception exception)
        {
            waiting = false;
            EditorApplication.update -= Poll;
            Daz3DBridge.BatchConversionMode = -1;
            Debug.LogException(exception);
            Debug.LogError("[PHASE00-HDRP] FAILED: " + exception.Message);
            if (Application.isBatchMode) EditorApplication.Exit(1);
        }
    }
}
