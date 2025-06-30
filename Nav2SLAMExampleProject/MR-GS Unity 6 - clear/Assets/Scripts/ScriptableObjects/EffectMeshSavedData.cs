using UnityEngine;
using System.Collections.Generic;

// 可以在 Unity 菜单中创建这种资产：Assets -> Create -> MRUK -> Saved Effect Mesh Data
[CreateAssetMenu(fileName = "EffectMeshSavedData", menuName = "MRUK/Saved Effect Mesh Data", order = 1)]
public class EffectMeshSavedData : ScriptableObject
{
    // 每个保存的网格条目
    [System.Serializable]
    public class SavedMeshEntry
    {
        public string originalAnchorName; // 原始 MRUKAnchor 的名称或 ID
        public Mesh meshAsset;            // 保存的 Mesh Asset 引用
        public Vector3 worldPosition;     // 世界坐标
        public Quaternion worldRotation;  // 世界旋转
        public Vector3 worldScale;        // 世界缩放

        // 可选：如果希望每个 Mesh GO 使用不同的材质，也可以在这里存储材质引用
        // public Material materialAsset;
    }

    public List<SavedMeshEntry> savedMeshes = new List<SavedMeshEntry>();

    public void Clear()
    {
        savedMeshes.Clear();
    }
}