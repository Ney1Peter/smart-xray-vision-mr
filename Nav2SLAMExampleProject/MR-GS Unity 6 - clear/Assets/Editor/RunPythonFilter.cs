using UnityEngine;
using UnityEditor;
using System.Diagnostics;
using System.IO;

public class RunPythonFilter : EditorWindow
{
    private Object plyFile;
    private string outputFileName = "output_filtered.ply";
    private string pythonExePath = "python"; // 如果需要，也可以设置为全路径

    [MenuItem("Tools/3DGS Filter via Python")]
    public static void ShowWindow()
    {
        GetWindow<RunPythonFilter>("3DGS Python Filter");
    }

    void OnGUI()
    {
        GUILayout.Label("3DGS PLY Filtering (Python)", EditorStyles.boldLabel);
        plyFile = EditorGUILayout.ObjectField("Input Binary .PLY", plyFile, typeof(DefaultAsset), false);
        outputFileName = EditorGUILayout.TextField("Output File Name", outputFileName);
        pythonExePath = EditorGUILayout.TextField("Python Executable", pythonExePath);

        if (GUILayout.Button("Run Python Filter"))
        {
            if (plyFile == null)
            {
                EditorUtility.DisplayDialog("Error", "Please select a .ply file.", "OK");
                return;
            }

            string inputPath = AssetDatabase.GetAssetPath(plyFile);
            string fullInputPath = Path.GetFullPath(inputPath);
            string outputPath = Path.Combine(Path.GetDirectoryName(fullInputPath), outputFileName);

            string scriptPath = Path.GetFullPath("Assets/ExternalTools/filter_3dgs.py");

            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = pythonExePath,
                Arguments = $"\"{scriptPath}\" \"{fullInputPath}\" \"{outputPath}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            try
            {
                using (Process process = Process.Start(psi))
                {
                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();
                    process.WaitForExit();

                    UnityEngine.Debug.Log($"[Python Output] {output}");
                    if (!string.IsNullOrWhiteSpace(error))
                        UnityEngine.Debug.LogWarning($"[Python Error] {error}");

                    if (File.Exists(outputPath))
                    {
                        UnityEngine.Debug.Log($"✅ Filtered file saved at:\n{outputPath}");
                        AssetDatabase.Refresh();
                    }
                    else
                    {
                        UnityEngine.Debug.LogError("❌ Output file was not created.");
                    }
                }
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogError($"❌ Failed to run Python script: {ex.Message}");
            }
        }
    }
}
