using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using UnityEngine.EventSystems;

[RequireComponent(typeof(Toggle))]
public class ToggleClickEvent : MonoBehaviour, IPointerClickHandler
{
    [System.Serializable]
    public class ToggleClickedEvent : UnityEvent<Toggle> { }

    public ToggleClickedEvent onClick;

    private Toggle toggle;

    void Awake()
    {
        toggle = GetComponent<Toggle>();
    }

    // 实现IPointerClickHandler接口，当用户点击时触发
    public void OnPointerClick(PointerEventData eventData)
    {
        // 触发Inspector中绑定的onClick事件
        onClick.Invoke(toggle);
    }
}
