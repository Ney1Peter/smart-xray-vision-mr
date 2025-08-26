using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

public class CanvasDebug : MonoBehaviour
{
    void Start()
    {
        var brs = GetComponents<GraphicRaycaster>();
        var tdr = GetComponents<TrackedDeviceGraphicRaycaster>();
        var btns = GetComponentsInChildren<Button>();
        Debug.Log($"[CanvasDebug] GraphicRaycasters={brs.Length}, TrackedDR={tdr.Length}, Buttons={btns.Length}");
    }
}
