using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEditor;

public class Filter3DGSPlyWindow : EditorWindow
{
    private TextAsset inputPly;
    private string outputFileName = "output_filter.ply";
    private float radius = 1.0f;

    [MenuItem("Tools/3DGS PLY Filter Window")]
    public static void ShowWindow()
    {
        GetWindow<Filter3DGSPlyWindow>("3DGS PLY Filter");
    }

    void OnGUI()
    {
        GUILayout.Label("3DGS PLY Filtering Tool", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        inputPly = (TextAsset)EditorGUILayout.ObjectField("Input .ply File", inputPly, typeof(TextAsset), false);
        radius = EditorGUILayout.FloatField("Radius (meters)", radius);
        outputFileName = EditorGUILayout.TextField("Output File Name", outputFileName);

        EditorGUILayout.Space();

        if (GUILayout.Button("Run Filter"))
        {
            if (inputPly == null)
            {
                EditorUtility.DisplayDialog("Error", "Please assign a .ply file.", "OK");
                return;
            }

            string inputPath = AssetDatabase.GetAssetPath(inputPly);
            string outputDir = "Assets/Filtered";
            string outputPath = Path.Combine(outputDir, outputFileName);

            RunFilter(inputPath, outputPath, radius);
        }
    }

    void RunFilter(string inputPath, string outputPath, float radius)
    {
        List<string> headerLines = new List<string>();
        List<string> dataLines = new List<string>();
        int vertexCount = 0;
        bool headerEnded = false;
        Vector3 sum = Vector3.zero;

        using (StreamReader reader = new StreamReader(inputPath))
        {
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                if (!headerEnded)
                {
                    headerLines.Add(line);
                    if (line.StartsWith("element vertex"))
                    {
                        var parts = line.Split(' ');
                        vertexCount = int.Parse(parts[2]);
                    }
                    if (line.Trim() == "end_header")
                    {
                        headerEnded = true;
                        continue;
                    }
                }
                else
                {
                    dataLines.Add(line);
                    string[] parts = line.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 3) continue;

                    float x = float.Parse(parts[0], CultureInfo.InvariantCulture);
                    float y = float.Parse(parts[1], CultureInfo.InvariantCulture);
                    float z = float.Parse(parts[2], CultureInfo.InvariantCulture);
                    sum += new Vector3(x, y, z);
                }
            }
        }

        Vector3 center = sum / vertexCount;
        Debug.Log($"PLY centroid: {center}");

        List<string> filteredLines = new List<string>();
        foreach (string line in dataLines)
        {
            string[] parts = line.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3) continue;

            float x = float.Parse(parts[0], CultureInfo.InvariantCulture);
            float y = float.Parse(parts[1], CultureInfo.InvariantCulture);
            float z = float.Parse(parts[2], CultureInfo.InvariantCulture);
            Vector3 pos = new Vector3(x, y, z);

            if (Vector3.Distance(pos, center) <= radius)
            {
                filteredLines.Add(line);
            }
        }

        for (int i = 0; i < headerLines.Count; i++)
        {
            if (headerLines[i].StartsWith("element vertex"))
            {
                headerLines[i] = $"element vertex {filteredLines.Count}";
                break;
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
        using (StreamWriter writer = new StreamWriter(outputPath))
        {
            foreach (var h in headerLines) writer.WriteLine(h);
            foreach (var l in filteredLines) writer.WriteLine(l);
        }

        Debug.Log($"Filtered PLY saved to: {outputPath}, count: {filteredLines.Count}");
        AssetDatabase.Refresh();
    }
}
