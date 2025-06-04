/*  PointCloudVisualizer.cs
 *  -----------------------
 *  订阅 ROS2 /camera/points  (sensor_msgs/PointCloud2)  
 *  用对象池 + 采样方式把点云可视化为小球。
 *
 *  Tested with: Unity 2020.3 / ROS-TCP-Connector 0.9 / Jazzy PointCloud2
 */
using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Sensor;

public class PointCloudVisualizer : MonoBehaviour
{
    /* ---------- Inspector 可调参数 ---------- */
    [Header("ROS")]
    [Tooltip("桥接端发布的 PointCloud2 Topic")]
    public string topicName = "/camera/points";

    [Header("Visual")]
    public GameObject pointPrefab;          // 拖一个小 cube / sphere
    [Tooltip("单帧最多可显示的点数")]
    public int maxPoints = 8_000;
    [Tooltip("行采样步长 (>=1)")]
    public int rowSkip = 6;                 // 480/6 ≈ 80 行
    [Tooltip("列采样步长 (>=1)")]
    public int colSkip = 6;                 // 640/6 ≈ 107 列
    /* -------------------------------------- */

    ROSConnection ros;

    // 字段偏移缓存
    int offX = -1, offY = -1, offZ = -1, pointStep;

    /* ========= 对象池 ========= */
    readonly List<GameObject> pool = new List<GameObject>();
    int poolIndex = 0;                      // 本帧复用到的位置
    /* ========================= */

    void Start()
    {
        ros = ROSConnection.GetOrCreateInstance();
        ros.Subscribe<PointCloud2Msg>(topicName, OnPointCloud);
    }

    void OnPointCloud(PointCloud2Msg msg)
    {
        /* --1. 首帧：解析字段偏移 -- */
        if (offX < 0)
        {
            foreach (var f in msg.fields)
            {
                switch (f.name)
                {
                    case "x": offX = (int)f.offset; break;
                    case "y": offY = (int)f.offset; break;
                    case "z": offZ = (int)f.offset; break;
                }
            }
            pointStep = (int)msg.point_step;
            if (offX < 0 || offY < 0 || offZ < 0)
            {
                Debug.LogError("[PCD] x/y/z field not found!"); return;
            }
        }

        /* --2. 复位对象池索引 -- */
        poolIndex = 0;

        /* --3. 解析并可视化 -- */
        int rows = (int)msg.height;
        int cols = (int)msg.width;
        byte[] buf = msg.data;

        for (int r = 0; r < rows; r += Mathf.Max(1, rowSkip))
        {
            int rowBase = r * cols * pointStep;
            for (int c = 0; c < cols; c += Mathf.Max(1, colSkip))
            {
                if (poolIndex >= maxPoints) goto QUIT;   // 显示上限

                int ofs = rowBase + c * pointStep;
                if (ofs + offZ + 4 > buf.Length) break;  // 越界保险

                float x = BitConverter.ToSingle(buf, ofs + offX);
                float y = BitConverter.ToSingle(buf, ofs + offY);
                float z = BitConverter.ToSingle(buf, ofs + offZ);
                if (float.IsNaN(x) || float.IsInfinity(x) ||
                    float.IsNaN(y) || float.IsInfinity(y) ||
                    float.IsNaN(z) || float.IsInfinity(z))
                    continue;

                Vector3 pos = new Vector3(x, z, y);    // Gazebo→Unity
                SpawnOrReuse(pos);
            }
        }
    QUIT:
        HideUnused();
    }

    /* ================= 工具函数 ================= */

    void SpawnOrReuse(Vector3 pos)
    {
        GameObject go;
        if (poolIndex < pool.Count)         // 复用
        {
            go = pool[poolIndex];
            go.transform.localPosition = pos;
            go.SetActive(true);
        }
        else                                // 新建
        {
            go = Instantiate(pointPrefab, pos, Quaternion.identity, transform);
            pool.Add(go);
        }
        poolIndex++;
    }

    /// 隐藏本帧未用到的旧点
    void HideUnused()
    {
        for (int i = poolIndex; i < pool.Count; ++i)
            pool[i].SetActive(false);
    }
}
