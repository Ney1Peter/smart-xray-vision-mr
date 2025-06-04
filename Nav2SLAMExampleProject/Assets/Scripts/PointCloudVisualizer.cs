/*  PointCloudVisualizer.cs
 *  -----------------------
 *  ���� ROS2 /camera/points  (sensor_msgs/PointCloud2)  
 *  �ö���� + ������ʽ�ѵ��ƿ��ӻ�ΪС��
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
    /* ---------- Inspector �ɵ����� ---------- */
    [Header("ROS")]
    [Tooltip("�ŽӶ˷����� PointCloud2 Topic")]
    public string topicName = "/camera/points";

    [Header("Visual")]
    public GameObject pointPrefab;          // ��һ��С cube / sphere
    [Tooltip("��֡������ʾ�ĵ���")]
    public int maxPoints = 8_000;
    [Tooltip("�в������� (>=1)")]
    public int rowSkip = 6;                 // 480/6 �� 80 ��
    [Tooltip("�в������� (>=1)")]
    public int colSkip = 6;                 // 640/6 �� 107 ��
    /* -------------------------------------- */

    ROSConnection ros;

    // �ֶ�ƫ�ƻ���
    int offX = -1, offY = -1, offZ = -1, pointStep;

    /* ========= ����� ========= */
    readonly List<GameObject> pool = new List<GameObject>();
    int poolIndex = 0;                      // ��֡���õ���λ��
    /* ========================= */

    void Start()
    {
        ros = ROSConnection.GetOrCreateInstance();
        ros.Subscribe<PointCloud2Msg>(topicName, OnPointCloud);
    }

    void OnPointCloud(PointCloud2Msg msg)
    {
        /* --1. ��֡�������ֶ�ƫ�� -- */
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

        /* --2. ��λ��������� -- */
        poolIndex = 0;

        /* --3. ���������ӻ� -- */
        int rows = (int)msg.height;
        int cols = (int)msg.width;
        byte[] buf = msg.data;

        for (int r = 0; r < rows; r += Mathf.Max(1, rowSkip))
        {
            int rowBase = r * cols * pointStep;
            for (int c = 0; c < cols; c += Mathf.Max(1, colSkip))
            {
                if (poolIndex >= maxPoints) goto QUIT;   // ��ʾ����

                int ofs = rowBase + c * pointStep;
                if (ofs + offZ + 4 > buf.Length) break;  // Խ�籣��

                float x = BitConverter.ToSingle(buf, ofs + offX);
                float y = BitConverter.ToSingle(buf, ofs + offY);
                float z = BitConverter.ToSingle(buf, ofs + offZ);
                if (float.IsNaN(x) || float.IsInfinity(x) ||
                    float.IsNaN(y) || float.IsInfinity(y) ||
                    float.IsNaN(z) || float.IsInfinity(z))
                    continue;

                Vector3 pos = new Vector3(x, z, y);    // Gazebo��Unity
                SpawnOrReuse(pos);
            }
        }
    QUIT:
        HideUnused();
    }

    /* ================= ���ߺ��� ================= */

    void SpawnOrReuse(Vector3 pos)
    {
        GameObject go;
        if (poolIndex < pool.Count)         // ����
        {
            go = pool[poolIndex];
            go.transform.localPosition = pos;
            go.SetActive(true);
        }
        else                                // �½�
        {
            go = Instantiate(pointPrefab, pos, Quaternion.identity, transform);
            pool.Add(go);
        }
        poolIndex++;
    }

    /// ���ر�֡δ�õ��ľɵ�
    void HideUnused()
    {
        for (int i = poolIndex; i < pool.Count; ++i)
            pool[i].SetActive(false);
    }
}
