using System;
using System.IO;
using UnityEngine;
using UnityEditor;

// NOTE: This script must be placed in a folder named 'Editor' in your Unity project.
// This is required for it to function correctly as an editor tool.

public class GreasePencilJsonImporter
{
    /// <summary>
    /// Creates a ScriptableObject asset from a selected Grease Pencil JSON file.
    /// This method is accessible from the Unity editor's Asset menu.
    /// </summary>
    [MenuItem("Assets/Create/Grease Pencil Data from JSON", true)]
    private static bool CreateFromJSONValidation()
    {
        // Check if a single object is selected in the project window
        if (Selection.objects.Length != 1) return false;

        // Check if the selected object is a TextAsset
        var selectedAsset = Selection.activeObject as TextAsset;
        if (selectedAsset == null) return false;

        // Check if the file has a .json extension
        string path = AssetDatabase.GetAssetPath(selectedAsset);
        return Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase);
    }

    [MenuItem("Assets/Create/Grease Pencil Data from JSON")]
    private static void CreateFromJSON()
    {
        // Get the selected TextAsset
        TextAsset jsonText = Selection.activeObject as TextAsset;
        if (jsonText == null) return;

        try
        {
            // Deserialize the JSON into our C# object
            GreasePencilData data = JsonUtility.FromJson<GreasePencilData>(jsonText.text);

            if (data == null)
            {
                Debug.LogError("Failed to deserialize JSON data. Check the file format.");
                return;
            }

            // Create a new ScriptableObject asset
            GreasePencilSO newAsset = ScriptableObject.CreateInstance<GreasePencilSO>();
            newAsset.data = data;

            // Save the new asset to the project
            string jsonPath = AssetDatabase.GetAssetPath(jsonText);
            string newAssetPath = jsonPath.Replace(".json", ".asset");
            AssetDatabase.CreateAsset(newAsset, newAssetPath);
            AssetDatabase.SaveAssets();
            
            Debug.Log($"Successfully created Grease Pencil ScriptableObject at: {newAssetPath}");
            Selection.activeObject = newAsset; // Select the new asset for the user
        }
        catch (Exception e)
        {
            Debug.LogError($"Error importing Grease Pencil JSON: {e.Message}");
        }
    }
}

