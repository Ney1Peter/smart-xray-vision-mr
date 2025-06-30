using UnityEngine;
using UnityEditor; // 必须在 UNITY_EDITOR 块内使用
using UnityEditor.SceneManagement; // 必须在 UNITY_EDITOR 块内使用
using System.Collections.Generic;
using System.Linq; // 用于 Linq 扩展方法
using System.IO; // 必须添加: 用于 Directory.Exists 和 Directory.CreateDirectory

// 需要引用 EffectMesh 所在的命名空间
using Meta.XR.MRUtilityKit;

public class EffectMeshManualSaver : MonoBehaviour
{
    [Tooltip("需要保存网格的 EffectMesh 组件引用。")]
    public EffectMesh targetEffectMesh;

    [Tooltip("保存 Mesh Asset 的目标文件夹路径 (相对于 Assets 文件夹)。例如: Assets/SavedMRUKMeshes/")]
    public string meshSavePath = "Assets/SavedMRUKMeshes/";

    [Tooltip("用于存储网格和变换数据的 ScriptableObject 资产。")]
    public EffectMeshSavedData savedDataAsset;

    // ⭐ 新增: 在重新创建网格时使用的默认材质
    [Tooltip("重新创建网格时应用的默认材质。如果为空，将使用 Unity Standard 材质。")]
    public Material defaultRecreatedMaterial;


    // 常量：用于在场景中识别我们重新创建的 Mesh GameObject
    private const string RECREATED_MESH_TAG = "MRUK_RecreatedMesh";
    private const string RECREATED_MESHES_ROOT_NAME = "SavedMRUKMeshes_Root";


    // ----------------------------------------------------
    // 手动触发保存所有已生成的 Effect Mesh
    // ----------------------------------------------------

    /// <summary>
    /// 手动触发保存 EffectMesh 生成的所有网格到项目文件夹中，
    /// 并记录其世界变换信息到 ScriptableObject 资产。
    /// 只能在 Unity Play Mode (运行模式) 下调用。
    /// </summary>
    [ContextMenu("Save All Effect Meshes & Transforms")]
    public void SaveAllEffectMeshesAndTransforms()
    {
#if UNITY_EDITOR // 确保这段代码只在编辑器中运行
        if (!Application.isPlaying) // 必须在 Play Mode 中，因为网格是在运行时生成的
        {
            Debug.LogWarning("EffectMeshManualSaver: 此功能只能在 Play Mode (运行模式) 下使用，因为网格是在运行时生成的。请先进入 Play Mode。", this);
            return;
        }

        if (targetEffectMesh == null) // 检查 EffectMesh 引用是否已设置
        {
            Debug.LogError("EffectMeshManualSaver: 未指定目标 EffectMesh。请在 Inspector 中设置。", this);
            return;
        }
        if (savedDataAsset == null)
        {
            Debug.LogError("EffectMeshManualSaver: 未指定 'Saved Data Asset' ScriptableObject。请创建并设置。", this);
            return;
        }

        int meshCount = targetEffectMesh.EffectMeshObjects.Count;
        Debug.Log($"EffectMeshManualSaver: 触发保存。当前 EffectMesh 生成的网格数量: {meshCount}", this);

        if (meshCount == 0) // 如果没有网格，就没必要继续了
        {
            Debug.LogWarning("EffectMeshManualSaver: target EffectMesh 中没有找到任何网格。请确保 EffectMesh 已经成功生成了它们。", this);
            return;
        }

        // 清空 ScriptableObject 中的旧数据，准备存储新数据
        savedDataAsset.Clear();
        EditorUtility.SetDirty(savedDataAsset); // 标记为脏，以便保存

        int savedMeshesCount = 0;
        string fullMeshFolderPath = meshSavePath.TrimEnd('/') + "/";
        string systemMeshPath = Application.dataPath + "/" + fullMeshFolderPath.Replace("Assets/", "");


        if (!Directory.Exists(systemMeshPath))
        {
            Debug.Log($"EffectMeshManualSaver: 正在创建 Mesh 保存目录: {systemMeshPath}", this);
            Directory.CreateDirectory(systemMeshPath); // ⭐ 在这里使用 systemMeshPath
            AssetDatabase.Refresh();
        }

        // ⭐ 新增：用于生成唯一后缀的计数器
        int uniqueFileSuffix = 0;

        // 遍历 EffectMesh 中所有已生成的网格对象并保存
        foreach (var pair in targetEffectMesh.EffectMeshObjects)
        {
            MRUKAnchor anchor = pair.Key;
            Meta.XR.MRUtilityKit.EffectMesh.EffectMeshObject effectMeshObj = pair.Value;

            if (effectMeshObj.mesh != null && effectMeshObj.effectMeshGO != null)
            {
                // ⭐ 核心修正：生成绝对唯一的 Mesh Asset 文件名和内部名称
                // 使用 anchor.Label 或 anchor.name，并加上一个递增的唯一后缀和 InstanceID 片段
                string baseName = anchor.Label.ToString().Replace(" | ", "_").Replace(" ", "_").Replace("-", "_"); // 基于 Label
                // 使用 GetInstanceID() 来确保唯一性，因为它总是存在的
                string uniqueAnchorIDPart = anchor.GetInstanceID().ToString();

                // 组合：Label + _ + InstanceID + _ + 递增计数器
                string uniqueMeshIdentifier = $"{baseName}_{uniqueAnchorIDPart}_{uniqueFileSuffix++}";
                string meshFileName = $"{uniqueMeshIdentifier}_GeneratedMesh"; // 完整的 Mesh 文件名部分 (不含.asset)
                string fullAssetPath = $"{fullMeshFolderPath}{meshFileName}.asset";


                Mesh meshToSaveMemoryInstance = Instantiate(effectMeshObj.mesh); // 关键：每次都 Instatiate 一份独立的 Mesh 副本
                meshToSaveMemoryInstance.name = meshFileName; // 给内存实例命名，和文件名一致

                // 删除旧的 Asset (如果存在) - 现在由于名字绝对唯一，这行可能不常触发，但保留
                if (AssetDatabase.LoadAssetAtPath<Mesh>(fullAssetPath) != null)
                {
                    AssetDatabase.DeleteAsset(fullAssetPath);
                    Debug.LogWarning($"EffectMeshManualSaver: 正在覆盖（清理旧的）同名网格 Asset: {fullAssetPath}", this);
                }

                Mesh finalMeshAssetReference = null; // 用于存储最终的 Asset 引用

                try
                {
                    // 将内存实例保存为 Asset
                    AssetDatabase.CreateAsset(meshToSaveMemoryInstance, fullAssetPath);

                    // ⭐ 关键修正：每次 AssetDatabase.CreateAsset 后立即强制刷新 AssetDatabase
                    // 确保 Unity 及时发现新创建的 .asset 文件并开始索引
                    AssetDatabase.SaveAssets(); // 先确保写入磁盘
                    AssetDatabase.Refresh();    // 再刷新 AssetDatabase 发现新文件

                    // 现在，我们可以更可靠地通过路径加载到这个刚刚创建的 Asset 的引用
                    finalMeshAssetReference = AssetDatabase.LoadAssetAtPath<Mesh>(fullAssetPath);

                    if (finalMeshAssetReference != null)
                    {
                        savedMeshesCount++;

                        // 记录变换数据到 ScriptableObject
                        EffectMeshSavedData.SavedMeshEntry entry = new EffectMeshSavedData.SavedMeshEntry
                        {
                            // ⭐ 修正：使用 originalAnchorName，将唯一的标识符赋值给它
                            originalAnchorName = uniqueMeshIdentifier, // ⭐ 记录唯一的标识符
                            meshAsset = finalMeshAssetReference, // <-- 使用重新加载后的 Asset 引用
                            worldPosition = effectMeshObj.effectMeshGO.transform.position,
                            worldRotation = effectMeshObj.effectMeshGO.transform.rotation,
                            worldScale = effectMeshObj.effectMeshGO.transform.lossyScale
                        };
                        savedDataAsset.savedMeshes.Add(entry);

                        Debug.Log($"EffectMeshManualSaver: 成功保存网格 '{uniqueMeshIdentifier}' 及数据，并绑定到 ScriptableObject。", this);
                    }
                    else
                    {
                        Debug.LogError($"EffectMeshManualSaver: 无法重新加载保存的 Mesh Asset: {fullAssetPath}。ScriptableObject 中将缺少 Mesh 引用！这可能是一个 Unity 内部时序问题。", this);
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"EffectMeshManualSaver: 保存网格 '{uniqueMeshIdentifier}' 失败。错误: {e.Message}", this);
                }
            }
        }

        // 循环结束后，再次全局刷新和保存，确保所有更改都持久化
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // 保存 ScriptableObject 资产的最终确认
        EditorUtility.SetDirty(savedDataAsset);
        AssetDatabase.SaveAssets(); // 强制保存 AssetDatabase
        AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(savedDataAsset), ImportAssetOptions.ForceUpdate); // 强制重新导入 Asset，确保其状态在编辑器中得到刷新
        Debug.Log($"EffectMeshManualSaver: 完成保存。共保存了 {savedMeshesCount} 个网格 Asset，并更新了 {savedDataAsset.savedMeshes.Count} 条数据到 ScriptableObject。所有操作已刷新到磁盘。", this);

#else // 如果不是 UNITY_EDITOR，禁用此功能
        Debug.LogWarning("EffectMeshManualSaver: 此功能仅在 Unity 编辑器中可用。", this);
#endif
    }

    // ----------------------------------------------------
    // 工具菜单项：用于在 Editor Mode 下重新创建 GameObject
    // ----------------------------------------------------

    /// <summary>
    /// 在编辑模式下，根据 ScriptableObject 中保存的数据重新创建 GameObject。
    /// </summary>
    [ContextMenu("Recreate Saved Meshes In Scene")]
    public void RecreateSavedMeshesInScene()
    {
#if UNITY_EDITOR
        if (Application.isPlaying)
        {
            Debug.LogWarning("EffectMeshManualSaver: 重新创建网格功能只能在 Edit Mode (编辑模式) 下使用。");
            return;
        }
        if (savedDataAsset == null)
        {
            Debug.LogError("EffectMeshManualSaver: 未指定 'Saved Data Asset' ScriptableObject。无法重新创建。", this);
            return;
        }

        Debug.Log("EffectMeshManualSaver: 开始在场景中重新创建已保存的网格对象。", this);

        // 清理旧的重新创建的网格对象（可选）
        ClearRecreatedMeshesInScene();


        if (savedDataAsset.savedMeshes.Count == 0)
        {
            Debug.LogWarning("EffectMeshManualSaver: 'Saved Data Asset' 中没有找到任何网格数据。请先在 Play Mode 中保存网格。", this);
            return;
        }

        GameObject recreatedMeshesRoot = new GameObject(RECREATED_MESHES_ROOT_NAME);
        recreatedMeshesRoot.tag = RECREATED_MESH_TAG; // 标记根对象，方便清理

        int recreatedCount = 0;
        foreach (var entry in savedDataAsset.savedMeshes)
        {
            if (entry.meshAsset != null) // ⭐ 再次检查 meshAsset 是否非空
            {
                // 使用 Mesh Asset 的名称来命名新创建的 GameObject
                // Mesh Asset 的名称现在应该是 "Label_InstanceID_计数器_GeneratedMesh"
                GameObject newMeshGO = new GameObject(entry.meshAsset.name);
                newMeshGO.transform.SetParent(recreatedMeshesRoot.transform);

                // 应用保存的世界变换信息
                newMeshGO.transform.position = entry.worldPosition;
                newMeshGO.transform.rotation = entry.worldRotation;
                newMeshGO.transform.localScale = entry.worldScale;

                MeshFilter meshFilter = newMeshGO.AddComponent<MeshFilter>();
                meshFilter.sharedMesh = entry.meshAsset;

                MeshRenderer meshRenderer = newMeshGO.AddComponent<MeshRenderer>();
                // ⭐ 使用用户指定的默认材质，如果未指定则使用 Standard 材质
                meshRenderer.sharedMaterial = defaultRecreatedMaterial != null ? defaultRecreatedMaterial : new Material(Shader.Find("Standard"));

                newMeshGO.tag = RECREATED_MESH_TAG; // 标记子级
                recreatedCount++;
            }
            else
            {
                Debug.LogWarning($"EffectMeshManualSaver: ScriptableObject 中有一条数据缺少 Mesh Asset 引用，跳过创建。原始锚点名: {entry.originalAnchorName}", this);
            }
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene()); // 标记场景已修改
        Debug.Log($"EffectMeshManualSaver: 已在场景中重新创建了 {recreatedCount} 个网格对象。", this);
#else // 如果不是 UNITY_EDITOR
        Debug.LogWarning("EffectMeshManualSaver: 此功能仅在 Unity 编辑器中可用。", this);
#endif
    }

    // ----------------------------------------------------
    // 清理已重新创建的网格 GameObject
    // ----------------------------------------------------

    [ContextMenu("Clear Recreated Meshes In Scene")]
    public void ClearRecreatedMeshesInScene()
    {
#if UNITY_EDITOR
        if (Application.isPlaying)
        {
            Debug.LogWarning("EffectMeshManualSaver: 清理重新创建的网格功能只能在 Edit Mode (编辑模式) 下使用。");
            return;
        }

        Debug.Log("EffectMeshManualSaver: 开始清理场景中之前重新创建的网格对象。", this);

        GameObject rootParent = GameObject.Find(RECREATED_MESHES_ROOT_NAME);
        int clearedCount = 0;
        if (rootParent != null)
        {
            clearedCount = rootParent.transform.childCount + 1; // 加上根对象本身
            DestroyImmediate(rootParent);
        }
        else
        {
            GameObject[] oldMeshes = GameObject.FindGameObjectsWithTag(RECREATED_MESH_TAG);
            foreach (GameObject go in oldMeshes)
            {
                DestroyImmediate(go);
                clearedCount++;
            }
        }


        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log($"EffectMeshManualSaver: 已清理了 {clearedCount} 个旧的重新创建网格对象。", this);
#else // 如果不是 UNITY_EDITOR
        Debug.LogWarning("EffectMeshManualSaver: 此功能仅在 Unity 编辑器中可用。", this);
#endif
    }

    // ----------------------------------------------------
    // 可选：重置 Saved Data Asset
    // ----------------------------------------------------
    [ContextMenu("Clear Saved Data Asset")]
    public void ClearSavedDataAsset()
    {
#if UNITY_EDITOR
        if (savedDataAsset != null)
        {
            savedDataAsset.Clear();
            EditorUtility.SetDirty(savedDataAsset);
            AssetDatabase.SaveAssets();
            Debug.Log("EffectMeshManualSaver: 已清空 Saved Data Asset 中的所有数据。", this);
        }
        else
        {
            Debug.LogWarning("EffectMeshManualSaver: 未指定 'Saved Data Asset' ScriptableObject，无法清空。", this);
        }
#endif
    }
}