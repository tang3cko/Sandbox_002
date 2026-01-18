using UnityEngine;
using UnityEditor;
using System.IO;
using System.Linq;
using System.Collections.Generic;

namespace Sandbox_002.VAT.Editor
{
    /// <summary>
    /// Editor window for VAT baking.
    /// </summary>
    public class VATBakerWindow : EditorWindow
    {
        private const string ShaderName = "VAT/Playback";

        private GameObject _sourcePrefab;
        private AnimationClip _animationClip;
        private AnimationClip[] _availableClips;
        private int _selectedClipIndex;
        private string[] _availableMeshNames;
        private int _frameRate = 30;
        private string _outputPath = "Assets/_Project/01_VAT/Generated";

        // Texture format options
        private enum VATTextureFormat
        {
            RGBAHalf,
            RGBAFloat
        }
        private VATTextureFormat _textureFormat = VATTextureFormat.RGBAHalf;
        private bool _enableGPUInstancing = true;

        [MenuItem("Window/VAT/Baker")]
        public static void ShowWindow()
        {
            GetWindow<VATBakerWindow>("VAT Baker");
        }

        private void OnGUI()
        {
            GUILayout.Label("VAT Baker", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            EditorGUI.BeginChangeCheck();
            _sourcePrefab = (GameObject)EditorGUILayout.ObjectField(
                "Source FBX / Prefab",
                _sourcePrefab,
                typeof(GameObject),
                false);

            if (EditorGUI.EndChangeCheck() && _sourcePrefab != null)
            {
                LoadAnimationClips();
                LoadSkinnedMeshRenderers();
            }

            // Show all meshes that will be baked
            if (_availableMeshNames != null && _availableMeshNames.Length > 0)
            {
                EditorGUILayout.LabelField("Meshes to bake:", EditorStyles.boldLabel);
                EditorGUI.indentLevel++;
                foreach (var meshName in _availableMeshNames)
                {
                    EditorGUILayout.LabelField($"• {meshName}");
                }
                EditorGUI.indentLevel--;
                EditorGUILayout.Space();
            }

            // Animation clip dropdown
            if (_availableClips != null && _availableClips.Length > 0)
            {
                string[] clipNames = _availableClips.Select(c => c.name).ToArray();
                _selectedClipIndex = EditorGUILayout.Popup("Animation Clip", _selectedClipIndex, clipNames);
                _animationClip = _availableClips[_selectedClipIndex];
            }
            else
            {
                _animationClip = (AnimationClip)EditorGUILayout.ObjectField(
                    "Animation Clip",
                    _animationClip,
                    typeof(AnimationClip),
                    false);
            }

            _frameRate = EditorGUILayout.IntSlider("Frame Rate", _frameRate, 15, 60);

            EditorGUILayout.Space();

            EditorGUILayout.LabelField("Texture Settings", EditorStyles.boldLabel);
            _textureFormat = (VATTextureFormat)EditorGUILayout.EnumPopup("Texture Format", _textureFormat);
            EditorGUILayout.HelpBox(
                _textureFormat == VATTextureFormat.RGBAHalf
                    ? "RGBAHalf: 16-bit per channel. Good balance of precision and memory."
                    : "RGBAFloat: 32-bit per channel. Maximum precision, higher memory usage.",
                MessageType.None);

            EditorGUILayout.Space();

            EditorGUILayout.LabelField("Material Settings", EditorStyles.boldLabel);
            _enableGPUInstancing = EditorGUILayout.Toggle("Enable GPU Instancing", _enableGPUInstancing);

            EditorGUILayout.Space();

            EditorGUILayout.BeginHorizontal();
            _outputPath = EditorGUILayout.TextField("Output Path", _outputPath);
            if (GUILayout.Button("...", GUILayout.Width(30)))
            {
                string path = EditorUtility.OpenFolderPanel("Select Output Folder", _outputPath, "");
                if (!string.IsNullOrEmpty(path))
                {
                    if (path.StartsWith(Application.dataPath))
                    {
                        _outputPath = "Assets" + path.Substring(Application.dataPath.Length);
                    }
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();

            bool canBake = _sourcePrefab != null && _animationClip != null && HasSkinnedMeshRenderer(_sourcePrefab);

            EditorGUI.BeginDisabledGroup(!canBake);
            if (GUILayout.Button("Bake All Meshes", GUILayout.Height(40)))
            {
                BakeAllMeshes();
            }
            EditorGUI.EndDisabledGroup();

            if (_sourcePrefab != null && !HasSkinnedMeshRenderer(_sourcePrefab))
            {
                EditorGUILayout.HelpBox(
                    "The selected prefab does not contain a SkinnedMeshRenderer.",
                    MessageType.Error);
            }
            else if (!canBake)
            {
                EditorGUILayout.HelpBox(
                    "Please assign a Source FBX/Prefab and an Animation Clip.",
                    MessageType.Info);
            }

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "Generated assets per mesh:\n" +
                "- {Anim}_{Mesh}_VAT_Position.asset\n" +
                "- {Anim}_{Mesh}_VAT_Normal.asset\n" +
                "- {Anim}_{Mesh}_VAT_Mesh.asset\n" +
                "- {Anim}_{Mesh}_VAT.mat",
                MessageType.None);
        }

        private void LoadAnimationClips()
        {
            string assetPath = AssetDatabase.GetAssetPath(_sourcePrefab);
            var allAssets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            _availableClips = allAssets.OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__"))
                .ToArray();
            _selectedClipIndex = 0;

            if (_availableClips.Length > 0)
            {
                _animationClip = _availableClips[0];
            }
        }

        private void LoadSkinnedMeshRenderers()
        {
            var renderers = _sourcePrefab.GetComponentsInChildren<SkinnedMeshRenderer>();
            _availableMeshNames = renderers
                .Select(r => r.sharedMesh != null ? r.sharedMesh.name : r.gameObject.name)
                .ToArray();
        }

        private bool HasSkinnedMeshRenderer(GameObject prefab)
        {
            return prefab.GetComponentInChildren<SkinnedMeshRenderer>() != null;
        }

        private void BakeAllMeshes()
        {
            // Validate shader exists
            var shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                EditorUtility.DisplayDialog("Error", $"Shader '{ShaderName}' not found!", "OK");
                return;
            }

            GameObject instance = null;
            try
            {
                EditorUtility.DisplayProgressBar("VAT Baker", "Instantiating model...", 0.1f);

                // Temporarily instantiate the prefab
                instance = (GameObject)PrefabUtility.InstantiatePrefab(_sourcePrefab);
                instance.hideFlags = HideFlags.HideAndDontSave;

                // Get all SkinnedMeshRenderers
                var renderers = instance.GetComponentsInChildren<SkinnedMeshRenderer>();
                if (renderers.Length == 0)
                {
                    EditorUtility.DisplayDialog("Error", "No SkinnedMeshRenderer found.", "OK");
                    return;
                }

                // Ensure output directory exists
                if (!AssetDatabase.IsValidFolder(_outputPath))
                {
                    CreateFolderRecursive(_outputPath);
                }

                string animBaseName = SanitizeFileName(_animationClip.name);
                var generatedAssets = new List<string>();

                // Bake each mesh
                for (int i = 0; i < renderers.Length; i++)
                {
                    var smr = renderers[i];
                    string meshName = SanitizeFileName(smr.sharedMesh != null ? smr.sharedMesh.name : smr.gameObject.name);

                    float progress = 0.1f + (0.8f * i / renderers.Length);
                    EditorUtility.DisplayProgressBar("VAT Baker", $"Baking {meshName}... ({i + 1}/{renderers.Length})", progress);

                    // Get original material's texture
                    Texture originalBaseMap = null;
                    Color originalBaseColor = Color.white;
                    if (smr.sharedMaterial != null)
                    {
                        var originalMat = smr.sharedMaterial;
                        if (originalMat.HasProperty("_BaseMap"))
                            originalBaseMap = originalMat.GetTexture("_BaseMap");
                        else if (originalMat.HasProperty("_MainTex"))
                            originalBaseMap = originalMat.GetTexture("_MainTex");

                        if (originalMat.HasProperty("_BaseColor"))
                            originalBaseColor = originalMat.GetColor("_BaseColor");
                        else if (originalMat.HasProperty("_Color"))
                            originalBaseColor = originalMat.GetColor("_Color");
                    }

                    // Bake animation
                    var unityTextureFormat = _textureFormat == VATTextureFormat.RGBAHalf
                        ? TextureFormat.RGBAHalf
                        : TextureFormat.RGBAFloat;
                    var result = VATBaker.BakeAnimation(smr, _animationClip, _frameRate, unityTextureFormat);
                    result.originalBaseMap = originalBaseMap;
                    result.originalBaseColor = originalBaseColor;

                    // Define asset paths
                    string baseName = $"{animBaseName}_{meshName}";
                    string positionTexPath = $"{_outputPath}/{baseName}_VAT_Position.asset";
                    string normalTexPath = $"{_outputPath}/{baseName}_VAT_Normal.asset";
                    string meshPath = $"{_outputPath}/{baseName}_VAT_Mesh.asset";
                    string materialPath = $"{_outputPath}/{baseName}_VAT.mat";

                    // Delete existing assets if any
                    DeleteAssetIfExists(positionTexPath);
                    DeleteAssetIfExists(normalTexPath);
                    DeleteAssetIfExists(meshPath);
                    DeleteAssetIfExists(materialPath);

                    // Save assets
                    AssetDatabase.CreateAsset(result.positionTexture, positionTexPath);
                    AssetDatabase.CreateAsset(result.normalTexture, normalTexPath);
                    AssetDatabase.CreateAsset(result.staticMesh, meshPath);

                    var material = CreateVATMaterial(shader, result, _enableGPUInstancing);
                    AssetDatabase.CreateAsset(material, materialPath);

                    generatedAssets.Add($"  {meshName}:\n    - {positionTexPath}\n    - {normalTexPath}\n    - {meshPath}\n    - {materialPath}");
                }

                EditorUtility.DisplayProgressBar("VAT Baker", "Finalizing...", 0.95f);

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                Debug.Log($"VAT Baked successfully! ({renderers.Length} meshes)\n" +
                          $"---\n" +
                          string.Join("\n", generatedAssets));

            }
            finally
            {
                if (instance != null)
                {
                    DestroyImmediate(instance);
                }
                EditorUtility.ClearProgressBar();
            }
        }

        private Material CreateVATMaterial(Shader shader, VATBakeResult result, bool enableGPUInstancing)
        {
            var material = new Material(shader);

            // VAT parameters
            material.SetTexture("_PositionTex", result.positionTexture);
            material.SetTexture("_NormalTex", result.normalTexture);
            material.SetFloat("_FrameCount", result.data.frameCount);
            material.SetFloat("_Duration", result.data.duration);
            material.SetFloat("_VertexCount", result.data.vertexCount);
            material.SetVector("_PosMin", new Vector4(
                result.data.positionMin.x,
                result.data.positionMin.y,
                result.data.positionMin.z,
                0));
            material.SetVector("_PosMax", new Vector4(
                result.data.positionMax.x,
                result.data.positionMax.y,
                result.data.positionMax.z,
                0));

            // Original material's texture and color
            if (result.originalBaseMap != null)
            {
                material.SetTexture("_BaseMap", result.originalBaseMap);
            }
            material.SetColor("_BaseColor", result.originalBaseColor);

            // GPU Instancing
            material.enableInstancing = enableGPUInstancing;

            return material;
        }

        private void DeleteAssetIfExists(string path)
        {
            if (AssetDatabase.LoadAssetAtPath<Object>(path) != null)
            {
                AssetDatabase.DeleteAsset(path);
            }
        }

        private void CreateFolderRecursive(string path)
        {
            string[] folders = path.Split('/');
            string currentPath = folders[0]; // "Assets"

            for (int i = 1; i < folders.Length; i++)
            {
                string newPath = currentPath + "/" + folders[i];
                if (!AssetDatabase.IsValidFolder(newPath))
                {
                    AssetDatabase.CreateFolder(currentPath, folders[i]);
                }
                currentPath = newPath;
            }
        }

        private string SanitizeFileName(string name)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            foreach (char c in invalid)
            {
                name = name.Replace(c, '_');
            }
            return name;
        }
    }
}
