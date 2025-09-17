// using UnityEngine;
// using UnityEditor;
// using System.Collections.Generic;
// using System.IO;
//
// public static class GreasePencilImporter {
//     [System.Serializable]
//     private class StrokePointJson {
//         public List<float> position; // [x,y,z]
//         public float radius;
//         public float opacity;
//     }
//
//     [System.Serializable]
//     private class StrokeJson {
//         public List<StrokePointJson> points;
//         public int material;
//     }
//
//     public static void ImportFromJson(string jsonPath) {
//         if (!File.Exists(jsonPath)) {
//             Debug.LogError("JSON file not found: " + jsonPath);
//             return;
//         }
//
//         string json = File.ReadAllText(jsonPath);
//
//         // Wrap in object since JsonUtility can’t parse bare arrays
//         string wrapped = "{ \"items\": " + json + "}";
//
//         Wrapper wrapper = JsonUtility.FromJson<Wrapper>(wrapped);
//
//         GreasePencil asset = ScriptableObject.CreateInstance<GreasePencil>();
//         asset.strokes = new List<GreasePencilStroke>();
//
//         foreach (var s in wrapper.items) {
//             GreasePencilStroke stroke = new GreasePencilStroke {
//                 points = new List<GreasePencilPoint>(),
//                 material = s.material
//             };
//
//             foreach (var p in s.points) {
//                 stroke.points.Add(new GreasePencilPoint {
//                     position = new Vector3(p.position[0], p.position[1], p.position[2]),
//                     radius = p.radius,
//                     opacity = p.opacity
//                 });
//             }
//
//             asset.strokes.Add(stroke);
//         }
//
//         string assetPath = "Assets/GreasePencilData.asset";
//         AssetDatabase.CreateAsset(asset, assetPath);
//         AssetDatabase.SaveAssets();
//         AssetDatabase.Refresh();
//
//         Debug.Log("Grease Pencil data imported to " + assetPath);
//     }
//
//     [System.Serializable]
//     private class Wrapper {
//         public List<StrokeJson> items;
//     }
//
//     [MenuItem("Assets/Import Grease Pencil JSON")]
//     public static void ImportDialog() {
//         string path = EditorUtility.OpenFilePanel("Select Grease Pencil JSON", Application.dataPath, "json");
//         if (!string.IsNullOrEmpty(path)) {
//             ImportFromJson(path);
//         }
//     }
// }
