using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace PuzzleFactory.Editor
{
    internal static class WorkshopReview
    {
        private const string Root = "Assets/Factory";
        private const string ScenePath = Root + "/Workshop.unity";

        [Serializable]
        private sealed class Palette
        {
            public Finish[] materials;
        }

        [Serializable]
        private sealed class Finish
        {
            public string name;
            public string hexColor;
            public float metallic;
            public float roughness;
            public float emission;
        }

        [MenuItem("Puzzle Factory/Build Workshop Review")]
        public static void Build()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.productName = "Puzzle Factory 3D";

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Models/FactoryWorkshop.fbx");
            if (!model)
                throw new InvalidOperationException("Import FactoryWorkshop.fbx before building the scene.");

            var factory = (GameObject)PrefabUtility.InstantiatePrefab(model);
            factory.name = "Factory workshop — Blender import";
            var materials = ImportPalette();
            var renderers = factory.GetComponentsInChildren<MeshRenderer>();
            var assemblies = new HashSet<string>();
            int triangles = 0;
            foreach (var renderer in renderers)
            {
                string[] designation = renderer.name.Split(new[] { "__" }, StringSplitOptions.None);
                if (designation.Length != 2 || !materials.TryGetValue(designation[1], out Material finish))
                    throw new InvalidOperationException("Missing factory finish for " + renderer.name);

                renderer.sharedMaterial = finish;
                assemblies.Add(designation[0]);
                var mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                for (int i = 0; i < mesh.subMeshCount; i++)
                    triangles += (int)mesh.GetIndexCount(i) / 3;
            }

            foreach (string name in new[] { "Red sorter", "Blue sorter", "Yellow sorter", "Helper robot",
                         "Infeed conveyor", "Left branch conveyor", "Right branch conveyor",
                         "Three-way diverter", "Upgrade arm display" })
            {
                if (!assemblies.Contains(name))
                    throw new InvalidOperationException("Missing assembly " + name);
            }

            Vector3 robot = CenterOf(renderers, "Helper robot");
            Vector3 blue = CenterOf(renderers, "Blue sorter");
            Vector3 yellow = CenterOf(renderers, "Yellow sorter");
            float right = Mathf.Sign(yellow.x - blue.x);
            float rear = Mathf.Sign(blue.z - robot.z);
            Vector3 Map(float x, float y, float z) => new Vector3(right * x, z, rear * y);

            var ambient = ColorFromHex("#B7C8E1");
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = ambient.linear * .48f;
            RenderSettings.reflectionIntensity = .55f;
            RenderSettings.fog = false;
            QualitySettings.antiAliasing = 4;
            QualitySettings.shadows = ShadowQuality.All;
            QualitySettings.shadowResolution = ShadowResolution.High;
            QualitySettings.shadowDistance = 55f;

            Directional("Warm key", Map(-5, -6, 10), ColorFromHex("#FFE1C2"), 1.05f, true);
            Directional("Cool fill", Map(5, -1, 6), ColorFromHex("#9BC7FF"), .78f, false);
            Directional("Warm rim", Map(2, 6, 6), ColorFromHex("#FFB377"), .63f, false);

            var camera = new GameObject("Portrait review camera").AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = ColorFromHex("#222F48");
            camera.orthographic = true;
            camera.nearClipPlane = .1f;
            camera.farClipPlane = 60f;
            camera.allowHDR = false;
            string review = Path.GetFullPath(Path.Combine(Application.dataPath, "../../docs/review"));
            Directory.CreateDirectory(review);
            Frame(camera, Map(6.5f, -16f, 15f), Map(0, .45f, .85f), 7.65f);
            Capture(camera, review, "unity-workshop", 860, 1080);
            Frame(camera, Map(1.3f, -9.5f, 5.1f), Map(-.73f, -2.46f, .82f), 2.43f);
            Capture(camera, review, "unity-details", 950, 720);
            Frame(camera, Map(4.6f, -16f, 15f), Map(.12f, .45f, .85f), 9.35f);
            Capture(camera, review, "unity-portrait", 720, 1280);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new IOException("Unable to save the workshop scene.");

            AssetDatabase.SaveAssets();

            Debug.Log($"Workshop verified: {renderers.Length} mesh renderers, {assemblies.Count} assemblies, "
                      + $"{triangles:N0} triangles. Robot={robot}, blue={blue}, yellow={yellow}. "
                      + $"Graphics device: {SystemInfo.graphicsDeviceName}.");
        }

        private static Dictionary<string, Material> ImportPalette()
        {
            string folder = Root + "/Materials";
            if (!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder(Root, "Materials");

            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(Root + "/Models/FactoryPalette.json");
            if (!asset)
                throw new InvalidOperationException("Missing FactoryPalette.json.");
            var shader = Shader.Find("Standard");
            if (!shader)
                throw new InvalidOperationException("The Built-in Standard shader is unavailable.");

            var result = new Dictionary<string, Material>();
            foreach (var finish in JsonUtility.FromJson<Palette>(asset.text).materials)
            {
                string path = $"{folder}/{finish.name}.mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (!material)
                {
                    material = new Material(shader);
                    AssetDatabase.CreateAsset(material, path);
                }

                Color color = ColorFromHex(finish.hexColor);
                material.color = color;
                material.SetFloat("_Metallic", finish.metallic);
                material.SetFloat("_Glossiness", 1 - finish.roughness);
                if (finish.emission > 0)
                {
                    material.EnableKeyword("_EMISSION");
                    material.SetColor("_EmissionColor", color * finish.emission);
                }
                else
                {
                    material.DisableKeyword("_EMISSION");
                }

                EditorUtility.SetDirty(material);
                result.Add(finish.name, material);
            }
            return result;
        }

        private static Vector3 CenterOf(MeshRenderer[] renderers, string assembly)
        {
            Bounds? bounds = null;
            foreach (var renderer in renderers)
            {
                if (!renderer.name.StartsWith(assembly + "__", StringComparison.Ordinal))
                    continue;
                Bounds combined = bounds ?? renderer.bounds;
                combined.Encapsulate(renderer.bounds);
                bounds = combined;
            }

            return bounds?.center ?? throw new InvalidOperationException("Missing " + assembly);
        }

        private static Color ColorFromHex(string hex)
        {
            if (ColorUtility.TryParseHtmlString(hex, out Color color))
                return color;
            throw new ArgumentException("Invalid palette color " + hex);
        }

        private static void Directional(string name, Vector3 from, Color color, float power, bool shadows)
        {
            var light = new GameObject(name).AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.LookRotation(-from);
            light.color = color;
            light.intensity = power;
            light.shadows = shadows ? LightShadows.Soft : LightShadows.None;
            light.shadowStrength = .66f;
        }

        private static void Frame(Camera camera, Vector3 position, Vector3 target, float halfHeight)
        {
            camera.transform.SetPositionAndRotation(position,
                Quaternion.LookRotation(target - position, Vector3.up));
            camera.orthographicSize = halfHeight;
        }

        private static void Capture(Camera camera, string folder, string name, int width, int height)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new InvalidOperationException("Use a graphics device to render the review.");

            var target = RenderTexture.GetTemporary(width * 2, height * 2, 24, RenderTextureFormat.ARGB32);
            var resolved = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            var pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target;
                camera.Render();
                target.filterMode = FilterMode.Bilinear;
                Graphics.Blit(target, resolved);
                RenderTexture.active = resolved;
                pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                pixels.Apply();
                File.WriteAllBytes(Path.Combine(folder, name + ".png"), pixels.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(resolved);
                RenderTexture.ReleaseTemporary(target);
                UnityEngine.Object.DestroyImmediate(pixels);
            }
        }
    }
}
