using UnityEngine;
using System.Collections.Generic;
using System.Collections; // For IEnumerator

#if UNITY_EDITOR // 确保这个脚本只在 Unity 编辑器中编译和运行
using UnityEditor; // 用于保存 Asset 和监听 Play Mode 状态
using UnityEditor.SceneManagement; // 用于保存场景
#endif

namespace Meta.XR.MRUtilityKit.Editor
{
    public class EffectMeshSaver : MonoBehaviour
    {
        [Tooltip("需要监听并保存网格的 EffectMesh 组件引用。")]
        public EffectMesh targetEffectMesh;

        [Tooltip("保存 Mesh Asset 的目标文件夹路径 (相对于 Assets 文件夹)。例如: Assets/SavedMRUKMeshes/")]
        public string savePath = "Assets/SavedMRUKMeshes/";

        [Tooltip("如果勾选，会在 Play Mode 结束后自动在场景中重新创建并放置这些网格。")]
        public bool autoRecreateMeshesAfterPlay = true;

        [Tooltip("如果勾选，每次重新创建网格时会先删除之前生成的旧网格对象。")]
        public bool clearExistingRecreatedMeshes = true;

        // 用于在场景中识别我们自动创建的 Mesh GameObject
        private const string RECREATED_MESH_TAG = "MRUK_RecreatedMesh";

        // ----------------------------------------------------
        // 生命周期与 Play Mode 状态监听
        // ----------------------------------------------------

        void Awake()
        {
#if !UNITY_EDITOR
            // 在构建的游戏中禁用
            enabled = false;
            return;
#endif
            // 订阅 Play Mode 状态变化事件
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            Debug.Log("EffectMeshSaver: 订阅 Play Mode 状态变化事件。", this);

            // 确保 targetEffectMesh 已设置
            if (targetEffectMesh == null)
            {
                Debug.LogError("EffectMeshSaver: 未指定目标 EffectMesh。请在 Inspector 中设置。", this);
                enabled = false;
                return;
            }
            // 自动保存逻辑（可选，如果你想同时自动保存和自动重新创建）
            // 如果你只想要重新创建，可以注释掉这里的MRUK回调注册
            // MRUK.Instance.RegisterSceneLoadedCallback(OnMRUKSceneLoaded);
        }

        void OnDestroy()
        {
#if UNITY_EDITOR
            // 取消订阅，避免内存泄漏
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            Debug.Log("EffectMeshSaver: 取消订阅 Play Mode 状态变化事件。", this);
            // 如果有订阅 MRUK.Instance.RegisterSceneLoadedCallback，也应在此处取消订阅，
            // 但 MRUK 当前没有提供公共的 UnregisterSceneLoadedCallback 方法，
            // 所以我们只能依赖 EditorApplication 的生命周期和单次触发逻辑。
#endif
        }

        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode)
            {
                // 在退出 Play Mode 之前，确保网格已经被保存
                Debug.Log("EffectMeshSaver: 检测到退出 Play Mode，准备保存运行时网格。", this);
                // 这里调用之前的保存逻辑，确保网格文件已经存在
                SaveAllEffectMeshesInternal();
            }
            else if (state == PlayModeStateChange.EnteredEditMode)
            {
                // 在进入 Edit Mode 后，重新创建网格 GameObject
                if (autoRecreateMeshesAfterPlay)
                {
                    Debug.Log("EffectMeshSaver: 检测到进入 Edit Mode，准备重新创建网格对象。", this);
                    RecreateMeshesInScene();
                }
            }
        }

        // ----------------------------------------------------
        // 保存网格到 Asset 文件 (与之前手动保存方法类似)
        // ----------------------------------------------------

        /// <summary>
        /// 内部方法：保存 EffectMesh 生成的所有网格到项目文件夹中。
        /// </summary>
        private void SaveAllEffectMeshesInternal()
        {
#if UNITY_EDITOR
            if (targetEffectMesh == null || targetEffectMesh.EffectMeshObjects.Count == 0)
            {
                Debug.LogWarning("EffectMeshSaver: 没有要保存的网格，或者 EffectMesh 未指定/未生成网格。", this);
                return;
            }

            int savedCount = 0;
            string fullFolderPath = savePath.TrimEnd('/') + "/";
            string systemPath = Application.dataPath + "/" + savePath.Replace("Assets/", "").TrimEnd('/') + "/";

            if (!System.IO.Directory.Exists(systemPath))
            {
                System.IO.Directory.CreateDirectory(systemPath);
                AssetDatabase.Refresh();
            }

            foreach (var pair in targetEffectMesh.EffectMeshObjects)
            {
                MRUKAnchor anchor = pair.Key;
                EffectMesh.EffectMeshObject effectMeshObj = pair.Value;

                if (effectMeshObj.mesh != null)
                {
                    string meshName = anchor.name + "_GeneratedMesh";
                    string fullAssetPath = fullFolderPath + meshName + ".asset";

                    Mesh meshToSave = Instantiate(effectMeshObj.mesh);
                    meshToSave.name = meshName;

                    if (AssetDatabase.LoadAssetAtPath<Mesh>(fullAssetPath) != null)
                    {
                        AssetDatabase.DeleteAsset(fullAssetPath);
                    }

                    try
                    {
                        AssetDatabase.CreateAsset(meshToSave, fullAssetPath);
                        savedCount++;
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogError($"EffectMeshSaver: 保存网格 {meshName} 失败。错误: {e.Message}", this);
                    }
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"EffectMeshSaver: 成功保存 {savedCount} 个网格 Asset 文件。", this);
#endif
        }

        // ----------------------------------------------------
        // 在 Play Mode 结束后重新创建 GameObject 并分配 Mesh
        // ----------------------------------------------------

        /// <summary>
        /// 在编辑模式下，根据已保存的 Mesh Asset 重新创建 GameObject 并放置在场景中。
        /// </summary>
        [ContextMenu("Recreate Meshes Manually")] // 也提供手动重新创建的选项
        public void RecreateMeshesInScene()
        {
#if UNITY_EDITOR
            if (Application.isPlaying)
            {
                Debug.LogWarning("EffectMeshSaver: 重新创建网格功能只能在 Edit Mode (编辑模式) 下使用。");
                return;
            }

            Debug.Log("EffectMeshSaver: 开始在场景中重新创建网格对象。", this);

            // 清理旧的重新创建的网格对象（可选）
            if (clearExistingRecreatedMeshes)
            {
                ClearRecreatedMeshesInScene();
            }

            string fullFolderPath = savePath.TrimEnd('/') + "/";
            // 获取所有已保存的网格 Asset
            string[] meshGuids = AssetDatabase.FindAssets("t:Mesh", new[] { fullFolderPath.Replace("Assets/", "").TrimEnd('/') });

            if (meshGuids.Length == 0)
            {
                Debug.LogWarning($"EffectMeshSaver: 在 '{fullFolderPath}' 路径下没有找到任何已保存的网格 Asset。", this);
                return;
            }

            GameObject parentObject = null;
            if (targetEffectMesh != null)
            {
                // 尝试在 EffectMesh 的父级下创建所有重新生成的网格
                parentObject = new GameObject("RecreatedMRUKMeshes");
                parentObject.transform.SetParent(targetEffectMesh.transform.parent, false);
            }
            else
            {
                // 如果没有 targetEffectMesh，就在场景根目录创建
                parentObject = new GameObject("RecreatedMRUKMeshes");
            }

            int recreatedCount = 0;
            foreach (string guid in meshGuids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                Mesh loadedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);

                if (loadedMesh != null)
                {
                    // 从网格名称中提取原始锚点名称，以找到其原始变换信息
                    string originalAnchorName = loadedMesh.name.Replace("_GeneratedMesh", "");
                    MRUKAnchor originalAnchor = FindObjectOfType<MRUKRoom>()?.Anchors.Find(a => a.name == originalAnchorName);

                    GameObject newMeshGO = new GameObject(loadedMesh.name);
                    newMeshGO.transform.SetParent(parentObject.transform); // 设置父级

                    // 如果能找到原始锚点，则使用其变换信息，确保位置正确
                    if (originalAnchor != null)
                    {
                        newMeshGO.transform.position = originalAnchor.transform.position;
                        newMeshGO.transform.rotation = originalAnchor.transform.rotation;
                        newMeshGO.transform.localScale = originalAnchor.transform.localScale;
                        newMeshGO.transform.SetParent(originalAnchor.transform); // 保持与原始锚点的父子关系
                    }
                    else
                    {
                        // 如果找不到原始锚点，可能网格是全局网格或其他特殊情况
                        // 此时，需要手动调整其位置，或者默认为零点
                        Debug.LogWarning($"EffectMeshSaver: 未能找到与网格 '{loadedMesh.name}' 对应的原始 MRUKAnchor。网格将放置在场景零点或其父级下。", this);
                    }


                    MeshFilter meshFilter = newMeshGO.AddComponent<MeshFilter>();
                    meshFilter.sharedMesh = loadedMesh; // 将保存的 Mesh Asset 分配给 MeshFilter

                    MeshRenderer meshRenderer = newMeshGO.AddComponent<MeshRenderer>();
                    if (targetEffectMesh != null && targetEffectMesh.MeshMaterial != null)
                    {
                        meshRenderer.sharedMaterial = targetEffectMesh.MeshMaterial; // 使用 EffectMesh 的材质
                    }
                    else
                    {
                        // 如果没有指定材质，使用默认材质或警告
                        Debug.LogWarning($"EffectMeshSaver: 没有为 {newMeshGO.name} 指定材质。使用默认材质。", this);
                    }

                    // 添加一个标记组件或标签，以便以后可以识别和清理
                    newMeshGO.tag = RECREATED_MESH_TAG; // 需要在 Unity 中定义这个 Tag

                    recreatedCount++;
                }
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene()); // 标记场景已修改，提示保存
            Debug.Log($"EffectMeshSaver: 已在场景中重新创建了 {recreatedCount} 个网格对象。", this);
#endif
        }

        /// <summary>
        /// 清理之前由 EffectMeshSaver 重新创建的所有网格对象。
        /// </summary>
        [ContextMenu("Clear Recreated Meshes In Scene")]
        public void ClearRecreatedMeshesInScene()
        {
#if UNITY_EDITOR
            if (Application.isPlaying)
            {
                Debug.LogWarning("EffectMeshSaver: 清理重新创建的网格功能只能在 Edit Mode (编辑模式) 下使用。");
                return;
            }

            Debug.Log("EffectMeshSaver: 开始清理场景中之前重新创建的网格对象。", this);
            GameObject[] oldMeshes = GameObject.FindGameObjectsWithTag(RECREATED_MESH_TAG);
            int clearedCount = 0;
            foreach (GameObject go in oldMeshes)
            {
                // 确保我们删除的是由我们创建的，并且其父级也是我们创建的
                if (go.transform.parent != null && go.transform.parent.name == "RecreatedMRUKMeshes")
                {
                    DestroyImmediate(go);
                    clearedCount++;
                }
            }
            // 清理根父级 GameObject
            GameObject parent = GameObject.Find("RecreatedMRUKMeshes");
            if (parent != null)
            {
                DestroyImmediate(parent);
                clearedCount++;
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log($"EffectMeshSaver: 已清理了 {clearedCount} 个旧的重新创建网格对象。", this);
#endif
        }
    }
}