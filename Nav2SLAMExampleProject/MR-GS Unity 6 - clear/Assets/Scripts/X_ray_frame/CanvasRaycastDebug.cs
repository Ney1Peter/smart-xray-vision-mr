using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[RequireComponent(typeof(GraphicRaycaster))]
public class CanvasRaycastDebug : MonoBehaviour
{
    public EventSystem eventSystem;      // 拖入场景中的 EventSystem
    GraphicRaycaster raycaster;

    void Awake()
    {
        raycaster = GetComponent<GraphicRaycaster>();
        if (eventSystem == null)
            eventSystem = FindObjectOfType<EventSystem>();
    }

    void Update()
    {
        // 每次鼠标左键（或手柄“Submit”如果映射到 Mouse0）按下时
        if (Input.GetMouseButtonDown(0))
        {
            // 准备 PointerEventData
            var data = new PointerEventData(eventSystem)
            {
                position = Input.mousePosition
            };
            // 发射射线
            var results = new List<RaycastResult>();
            raycaster.Raycast(data, results);

            if (results.Count == 0)
            {
                Debug.Log("[RaycastDebug] 没有命中任何 UI");
            }
            else
            {
                foreach (var r in results)
                    Debug.Log($"[RaycastDebug] 命中: {r.gameObject.name}");
            }
        }
    }
}
