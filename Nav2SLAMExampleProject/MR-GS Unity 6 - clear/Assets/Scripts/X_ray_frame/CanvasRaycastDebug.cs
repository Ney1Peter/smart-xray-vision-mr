using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[RequireComponent(typeof(GraphicRaycaster))]
public class CanvasRaycastDebug : MonoBehaviour
{
    public EventSystem eventSystem;      // ���볡���е� EventSystem
    GraphicRaycaster raycaster;

    void Awake()
    {
        raycaster = GetComponent<GraphicRaycaster>();
        if (eventSystem == null)
            eventSystem = FindObjectOfType<EventSystem>();
    }

    void Update()
    {
        // ÿ�������������ֱ���Submit�����ӳ�䵽 Mouse0������ʱ
        if (Input.GetMouseButtonDown(0))
        {
            // ׼�� PointerEventData
            var data = new PointerEventData(eventSystem)
            {
                position = Input.mousePosition
            };
            // ��������
            var results = new List<RaycastResult>();
            raycaster.Raycast(data, results);

            if (results.Count == 0)
            {
                Debug.Log("[RaycastDebug] û�������κ� UI");
            }
            else
            {
                foreach (var r in results)
                    Debug.Log($"[RaycastDebug] ����: {r.gameObject.name}");
            }
        }
    }
}
