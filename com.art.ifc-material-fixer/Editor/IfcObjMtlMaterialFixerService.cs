using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Art.IfcMaterialFixer
{
    public sealed class IfcObjMtlMaterialFixOptions
    {
        public string MaterialFolder = IfcObjMtlMaterialFixerService.DefaultMaterialFolder;
        public string PreferredShaderName = "Universal Render Pipeline/Lit";
        public string FallbackShaderName = "Standard";
        public bool AssignAllMaterialSlots = true;
        public bool SaveAssets = true;
        public bool LogSummary = true;
    }

    public struct IfcObjMtlMaterialFixResult
    {
        public bool Success;
        public string Error;
        public int ObjMappings;
        public int MtlColors;
        public int Renderers;
        public int AssignedRenderers;
        public int SkippedNoMesh;
        public int MissingObjMapping;
        public int MissingMtlColor;
        public int CreatedMaterials;
        public int UpdatedMaterials;

        public override string ToString()
        {
            if (!Success)
            {
                return "IFC OBJ/MTL material fix failed: " + Error;
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                "IFC OBJ/MTL material fix completed. Renderers: {0}, assigned: {1}, OBJ mappings: {2}, MTL colors: {3}, missing OBJ/usemtl: {4}, missing MTL Kd: {5}, skipped no mesh: {6}, created materials: {7}, updated materials: {8}.",
                Renderers,
                AssignedRenderers,
                ObjMappings,
                MtlColors,
                MissingObjMapping,
                MissingMtlColor,
                SkippedNoMesh,
                CreatedMaterials,
                UpdatedMaterials
            );
        }
    }

    public static class IfcObjMtlMaterialFixerService
    {
        public const string DefaultMaterialFolder = "Assets/Materials/IfcMtlColors";

        public static IfcObjMtlMaterialFixResult Fix(
            GameObject root,
            string objPath,
            string mtlPath,
            IfcObjMtlMaterialFixOptions options = null
        )
        {
            options = options ?? new IfcObjMtlMaterialFixOptions();

            IfcObjMtlMaterialFixResult result = new IfcObjMtlMaterialFixResult();

            if (root == null)
            {
                return Fail(result, "No IFC root GameObject was provided.");
            }

            if (string.IsNullOrWhiteSpace(objPath) || !File.Exists(objPath))
            {
                return Fail(result, "OBJ file not found: " + objPath);
            }

            if (string.IsNullOrWhiteSpace(mtlPath) || !File.Exists(mtlPath))
            {
                return Fail(result, "MTL file not found: " + mtlPath);
            }

            string materialFolder = NormalizeAssetPath(options.MaterialFolder);
            if (!EnsureAssetFolder(materialFolder))
            {
                return Fail(result, "Material folder must be inside Assets: " + materialFolder);
            }

            Shader shader = FindMaterialShader(options);
            if (shader == null)
            {
                return Fail(
                    result,
                    "Could not find shader: "
                        + options.PreferredShaderName
                        + " or "
                        + options.FallbackShaderName
                );
            }

            Dictionary<string, string> objToMtl = ParseObjObjectToMaterial(objPath);
            Dictionary<string, Color> mtlColors = ParseMtlColors(mtlPath);
            Dictionary<string, Material> materialCache = new Dictionary<string, Material>(
                StringComparer.OrdinalIgnoreCase
            );

            result.ObjMappings = objToMtl.Count;
            result.MtlColors = mtlColors.Count;

            MeshRenderer[] renderers = root.GetComponentsInChildren<MeshRenderer>(true);
            result.Renderers = renderers.Length;

            foreach (MeshRenderer renderer in renderers)
            {
                MeshFilter meshFilter = renderer.GetComponent<MeshFilter>();
                if (meshFilter == null || meshFilter.sharedMesh == null)
                {
                    result.SkippedNoMesh++;
                    continue;
                }

                string mtlName;
                if (!TryGetMtlNameForRenderer(renderer, meshFilter, objToMtl, out mtlName))
                {
                    result.MissingObjMapping++;
                    continue;
                }

                Color color;
                if (!mtlColors.TryGetValue(mtlName, out color))
                {
                    result.MissingMtlColor++;
                    continue;
                }

                Material material = GetOrCreateMaterial(
                    materialFolder,
                    mtlName,
                    color,
                    shader,
                    materialCache,
                    ref result
                );

                AssignMaterial(renderer, material, options.AssignAllMaterialSlots);
                EditorUtility.SetDirty(renderer);
                result.AssignedRenderers++;
            }

            result.Success = true;

            if (options.SaveAssets)
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            if (root.scene.IsValid())
            {
                EditorSceneManager.MarkSceneDirty(root.scene);
            }

            if (options.LogSummary)
            {
                Debug.Log(result.ToString());
            }

            return result;
        }

        private static IfcObjMtlMaterialFixResult Fail(
            IfcObjMtlMaterialFixResult result,
            string message
        )
        {
            result.Success = false;
            result.Error = message;
            Debug.LogError(result.ToString());
            return result;
        }

        private static Shader FindMaterialShader(IfcObjMtlMaterialFixOptions options)
        {
            Shader shader = null;

            if (!string.IsNullOrWhiteSpace(options.PreferredShaderName))
            {
                shader = Shader.Find(options.PreferredShaderName);
            }

            if (shader == null && !string.IsNullOrWhiteSpace(options.FallbackShaderName))
            {
                shader = Shader.Find(options.FallbackShaderName);
            }

            return shader;
        }

        private static void AssignMaterial(
            MeshRenderer renderer,
            Material material,
            bool assignAllSlots
        )
        {
            if (!assignAllSlots)
            {
                renderer.sharedMaterial = material;
                return;
            }

            Material[] sharedMaterials = renderer.sharedMaterials;
            if (sharedMaterials == null || sharedMaterials.Length == 0)
            {
                renderer.sharedMaterial = material;
                return;
            }

            for (int i = 0; i < sharedMaterials.Length; i++)
            {
                sharedMaterials[i] = material;
            }

            renderer.sharedMaterials = sharedMaterials;
        }

        private static bool TryGetMtlNameForRenderer(
            MeshRenderer renderer,
            MeshFilter meshFilter,
            Dictionary<string, string> objToMtl,
            out string mtlName
        )
        {
            foreach (string candidate in GetRendererNameCandidates(renderer, meshFilter))
            {
                if (objToMtl.TryGetValue(candidate, out mtlName))
                {
                    return true;
                }
            }

            mtlName = null;
            return false;
        }

        private static IEnumerable<string> GetRendererNameCandidates(
            MeshRenderer renderer,
            MeshFilter meshFilter
        )
        {
            List<string> candidates = new List<string>();

            AddCandidate(candidates, renderer.gameObject.name);

            if (meshFilter != null && meshFilter.sharedMesh != null)
            {
                AddCandidate(candidates, meshFilter.sharedMesh.name);
            }

            IfcProductData productData = renderer.GetComponent<IfcProductData>();
            if (productData == null)
            {
                productData = renderer.GetComponentInParent<IfcProductData>();
            }

            if (productData != null)
            {
                AddCandidate(candidates, productData.Id);
                AddCandidate(candidates, productData.Label.ToString(CultureInfo.InvariantCulture));
            }

            return candidates;
        }

        private static void AddCandidate(List<string> candidates, string value)
        {
            string clean = CleanName(value);
            if (string.IsNullOrWhiteSpace(clean))
            {
                return;
            }

            if (!candidates.Contains(clean))
            {
                candidates.Add(clean);
            }
        }

        private static Dictionary<string, string> ParseObjObjectToMaterial(string objPath)
        {
            Dictionary<string, string> result = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase
            );

            string currentObject = null;
            string currentGroup = null;

            foreach (string rawLine in File.ReadLines(objPath))
            {
                string line = rawLine.Trim();

                if (line.StartsWith("o ", StringComparison.Ordinal))
                {
                    currentObject = line.Substring(2).Trim();
                    currentGroup = null;
                    continue;
                }

                if (line.StartsWith("g ", StringComparison.Ordinal))
                {
                    currentGroup = line.Substring(2).Trim();
                    continue;
                }

                if (line.StartsWith("usemtl ", StringComparison.Ordinal))
                {
                    string materialName = line.Substring("usemtl ".Length).Trim();
                    AddObjMaterialMapping(result, currentObject, materialName);
                    AddObjMaterialMapping(result, currentGroup, materialName);
                }
            }

            return result;
        }

        private static void AddObjMaterialMapping(
            Dictionary<string, string> result,
            string objectOrGroupName,
            string materialName
        )
        {
            string cleanName = CleanName(objectOrGroupName);
            if (string.IsNullOrWhiteSpace(cleanName) || string.IsNullOrWhiteSpace(materialName))
            {
                return;
            }

            result[cleanName] = materialName;
        }

        private static Dictionary<string, Color> ParseMtlColors(string mtlPath)
        {
            Dictionary<string, Color> result = new Dictionary<string, Color>(
                StringComparer.OrdinalIgnoreCase
            );
            Dictionary<string, float> alphaByMaterial = new Dictionary<string, float>(
                StringComparer.OrdinalIgnoreCase
            );

            string currentMaterial = null;

            foreach (string rawLine in File.ReadLines(mtlPath))
            {
                string line = rawLine.Trim();

                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                if (line.StartsWith("newmtl ", StringComparison.Ordinal))
                {
                    currentMaterial = line.Substring("newmtl ".Length).Trim();
                    continue;
                }

                if (string.IsNullOrWhiteSpace(currentMaterial))
                {
                    continue;
                }

                if (line.StartsWith("Kd ", StringComparison.Ordinal))
                {
                    string[] parts = SplitMtlLine(line);
                    float r;
                    float g;
                    float b;

                    if (
                        parts.Length >= 4
                        && TryParseInvariant(parts[1], out r)
                        && TryParseInvariant(parts[2], out g)
                        && TryParseInvariant(parts[3], out b)
                    )
                    {
                        float alpha;
                        if (!alphaByMaterial.TryGetValue(currentMaterial, out alpha))
                        {
                            alpha = 1f;
                        }

                        result[currentMaterial] = new Color(r, g, b, alpha);
                    }

                    continue;
                }

                if (line.StartsWith("d ", StringComparison.Ordinal))
                {
                    UpdateAlpha(result, alphaByMaterial, currentMaterial, line, false);
                    continue;
                }

                if (line.StartsWith("Tr ", StringComparison.Ordinal))
                {
                    UpdateAlpha(result, alphaByMaterial, currentMaterial, line, true);
                }
            }

            return result;
        }

        private static void UpdateAlpha(
            Dictionary<string, Color> colors,
            Dictionary<string, float> alphaByMaterial,
            string materialName,
            string line,
            bool inverted
        )
        {
            string[] parts = SplitMtlLine(line);
            float value;

            if (parts.Length < 2 || !TryParseInvariant(parts[1], out value))
            {
                return;
            }

            float alpha = inverted ? 1f - value : value;
            alpha = Mathf.Clamp01(alpha);
            alphaByMaterial[materialName] = alpha;

            Color color;
            if (colors.TryGetValue(materialName, out color))
            {
                color.a = alpha;
                colors[materialName] = color;
            }
        }

        private static string[] SplitMtlLine(string line)
        {
            return line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        }

        private static bool TryParseInvariant(string value, out float result)
        {
            return float.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out result
            );
        }

        private static Material GetOrCreateMaterial(
            string folder,
            string mtlName,
            Color color,
            Shader shader,
            Dictionary<string, Material> cache,
            ref IfcObjMtlMaterialFixResult result
        )
        {
            Material cached;
            if (cache.TryGetValue(mtlName, out cached))
            {
                return cached;
            }

            string safeName = MakeSafeAssetName(mtlName);
            string assetPath = folder + "/" + safeName + ".mat";

            Material material = AssetDatabase.LoadAssetAtPath<Material>(assetPath);

            if (material == null)
            {
                material = new Material(shader);
                material.name = mtlName;
                AssetDatabase.CreateAsset(material, assetPath);
                result.CreatedMaterials++;
            }
            else
            {
                material.shader = shader;
                result.UpdatedMaterials++;
            }

            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }

            EditorUtility.SetDirty(material);
            cache[mtlName] = material;

            return material;
        }

        private static bool EnsureAssetFolder(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder))
            {
                return false;
            }

            folder = NormalizeAssetPath(folder);

            if (folder == "Assets")
            {
                return true;
            }

            if (!folder.StartsWith("Assets/", StringComparison.Ordinal))
            {
                return false;
            }

            if (AssetDatabase.IsValidFolder(folder))
            {
                return true;
            }

            string[] parts = folder.Split('/');
            string current = "Assets";

            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }

                current = next;
            }

            return AssetDatabase.IsValidFolder(folder);
        }

        private static string NormalizeAssetPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return path;
            }

            return path.Replace('\\', '/').TrimEnd('/');
        }

        private static string CleanName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            return name.Replace("(Clone)", string.Empty)
                .Replace(" Instance", string.Empty)
                .Trim();
        }

        private static string MakeSafeAssetName(string name)
        {
            string safe = string.IsNullOrWhiteSpace(name) ? "IFC_Material" : name;

            foreach (char c in Path.GetInvalidFileNameChars())
            {
                safe = safe.Replace(c, '_');
            }

            safe = safe.Replace('/', '_')
                .Replace('\\', '_')
                .Replace(':', '_')
                .Replace('"', '_')
                .Replace('*', '_')
                .Replace('?', '_')
                .Replace('<', '_')
                .Replace('>', '_')
                .Replace('|', '_')
                .Trim();

            if (safe.Length > 120)
            {
                safe = safe.Substring(0, 120);
            }

            return string.IsNullOrWhiteSpace(safe) ? "IFC_Material" : safe;
        }
    }
}
