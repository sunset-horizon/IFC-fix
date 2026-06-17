using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Art.IfcMaterialFixer
{
    public static class IfcObjMtlMaterialFixerMenu
    {
        [MenuItem("IFC Tools/Load IFC file with OBJ MTL materials")]
        public static void LoadIfcFileWithObjMtlMaterialsFromIfcToolsMenu()
        {
            LoadIfcFileWithObjMtlMaterials();
        }

        [MenuItem("Tools/IFC/Load IFC File With OBJ MTL Materials")]
        public static void LoadIfcFileWithObjMtlMaterials()
        {
            string ifcFilePath = EditorUtility.OpenFilePanel("Select IFC file", string.Empty, "ifc");
            if (string.IsNullOrEmpty(ifcFilePath))
            {
                return;
            }

            GameObject importedRoot;
            using (IfcFileLoader fileLoader = new IfcFileLoader())
            {
                importedRoot = fileLoader.LoadIfcFile(ifcFilePath);
            }

            if (importedRoot == null)
            {
                return;
            }

            string objPath;
            string mtlPath;
            if (!TryGetExpectedObjMtlPaths(ifcFilePath, out objPath, out mtlPath))
            {
                Debug.LogError("Could not resolve OBJ/MTL output paths for IFC: " + ifcFilePath);
                Selection.activeGameObject = importedRoot;
                return;
            }

            IfcObjMtlMaterialFixerService.Fix(importedRoot, objPath, mtlPath);
            Selection.activeGameObject = importedRoot;
        }

        [MenuItem("Tools/IFC/Fix Selected IFC Materials Auto Find OBJ MTL")]
        public static void FixSelectedIfcMaterialsAutoFind()
        {
            GameObject root = Selection.activeGameObject;
            if (root == null)
            {
                Debug.LogError("Select the imported IFC root GameObject in Hierarchy first.");
                return;
            }

            string objPath;
            string mtlPath;
            if (!TryFindObjMtlForRoot(root, out objPath, out mtlPath))
            {
                Debug.LogError(
                    "Could not find matching OBJ/MTL files for selected IFC root: " + root.name
                );
                return;
            }

            IfcObjMtlMaterialFixerService.Fix(root, objPath, mtlPath);
        }

        [MenuItem("Tools/IFC/Fix Selected IFC Materials From OBJ MTL")]
        public static void FixSelectedIfcMaterialsFromObjMtl()
        {
            GameObject root = Selection.activeGameObject;
            if (root == null)
            {
                Debug.LogError("Select the imported IFC root GameObject in Hierarchy first.");
                return;
            }

            string objPath = EditorUtility.OpenFilePanel(
                "Select OBJ file",
                Application.dataPath,
                "obj"
            );

            if (string.IsNullOrEmpty(objPath))
            {
                return;
            }

            string mtlPath = EditorUtility.OpenFilePanel(
                "Select MTL file",
                Path.GetDirectoryName(objPath),
                "mtl"
            );

            if (string.IsNullOrEmpty(mtlPath))
            {
                return;
            }

            IfcObjMtlMaterialFixerService.Fix(root, objPath, mtlPath);
        }

        [MenuItem("Tools/IFC/Fix Selected IFC Materials Auto Find OBJ MTL", true)]
        private static bool ValidateFixSelectedAutoFind()
        {
            return Selection.activeGameObject != null;
        }

        [MenuItem("Tools/IFC/Fix Selected IFC Materials From OBJ MTL", true)]
        private static bool ValidateFixSelectedFromObjMtl()
        {
            return Selection.activeGameObject != null;
        }

        private static bool TryGetExpectedObjMtlPaths(
            string ifcFilePath,
            out string objPath,
            out string mtlPath
        )
        {
            objPath = null;
            mtlPath = null;

            string outputPath = Config.CurrentConfig.OutputPath;
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                return false;
            }

            foreach (string baseName in GetIfcOutputBaseNameCandidates(ifcFilePath))
            {
                objPath = NormalizeFilePath(Path.Combine(outputPath, baseName + ".obj"));
                mtlPath = NormalizeFilePath(Path.Combine(outputPath, baseName + ".mtl"));

                if (File.Exists(objPath) && File.Exists(mtlPath))
                {
                    return true;
                }
            }

            return false;
        }

        private static IEnumerable<string> GetIfcOutputBaseNameCandidates(string ifcFilePath)
        {
            string fileName = Path.GetFileName(ifcFilePath);
            string baseName = Path.GetFileNameWithoutExtension(fileName);

            if (!string.IsNullOrWhiteSpace(baseName))
            {
                yield return baseName;
            }

            // Keep compatibility with the official plugin's string Replace naming.
            string pluginObjFileName = fileName.Replace("ifc", "obj");
            string pluginBaseName = Path.GetFileNameWithoutExtension(pluginObjFileName);
            if (!string.IsNullOrWhiteSpace(pluginBaseName) && pluginBaseName != baseName)
            {
                yield return pluginBaseName;
            }
        }

        private static bool TryFindObjMtlForRoot(
            GameObject root,
            out string objPath,
            out string mtlPath
        )
        {
            objPath = null;
            mtlPath = null;

            List<string> baseNames = GetRootBaseNameCandidates(root);
            List<string> searchFolders = GetSearchFolders();

            foreach (string folder in searchFolders)
            {
                if (!Directory.Exists(folder))
                {
                    continue;
                }

                foreach (string baseName in baseNames)
                {
                    string exactObj = NormalizeFilePath(Path.Combine(folder, baseName + ".obj"));
                    string exactMtl = NormalizeFilePath(Path.Combine(folder, baseName + ".mtl"));

                    if (File.Exists(exactObj) && File.Exists(exactMtl))
                    {
                        objPath = exactObj;
                        mtlPath = exactMtl;
                        return true;
                    }
                }
            }

            foreach (string folder in searchFolders)
            {
                if (!Directory.Exists(folder))
                {
                    continue;
                }

                string[] objFiles = Directory.GetFiles(folder, "*.obj", SearchOption.TopDirectoryOnly);
                foreach (string candidateObj in objFiles)
                {
                    string stem = Path.GetFileNameWithoutExtension(candidateObj);
                    if (!ContainsBaseName(baseNames, stem))
                    {
                        continue;
                    }

                    string candidateMtl = Path.ChangeExtension(candidateObj, ".mtl");
                    if (File.Exists(candidateMtl))
                    {
                        objPath = NormalizeFilePath(candidateObj);
                        mtlPath = NormalizeFilePath(candidateMtl);
                        return true;
                    }
                }
            }

            return false;
        }

        private static List<string> GetRootBaseNameCandidates(GameObject root)
        {
            List<string> result = new List<string>();
            AddUnique(result, Path.GetFileNameWithoutExtension(root.name));

            IfcFileAssociation association = root.GetComponent<IfcFileAssociation>();
            if (association != null && !string.IsNullOrWhiteSpace(association.IfcFile))
            {
                AddUnique(result, Path.GetFileNameWithoutExtension(association.IfcFile));
            }

            return result;
        }

        private static List<string> GetSearchFolders()
        {
            List<string> folders = new List<string>();
            AddUnique(folders, NormalizeFilePath(Config.CurrentConfig.OutputPath));
            AddUnique(folders, NormalizeFilePath(Path.Combine(Application.dataPath, "Meshes")));
            return folders;
        }

        private static bool ContainsBaseName(List<string> baseNames, string value)
        {
            foreach (string baseName in baseNames)
            {
                if (string.Equals(baseName, value, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static void AddUnique(List<string> values, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            foreach (string existing in values)
            {
                if (string.Equals(existing, value, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            values.Add(value);
        }

        private static string NormalizeFilePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return path;
            }

            return path.Replace('\\', '/');
        }
    }
}
