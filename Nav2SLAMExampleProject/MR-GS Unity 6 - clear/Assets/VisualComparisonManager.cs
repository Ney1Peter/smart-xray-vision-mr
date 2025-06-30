using GaussianSplatting.Runtime;
using Oculus.Interaction.Samples;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class VisualComparisonManager : MonoBehaviour
{

    [SerializeField] private GameObject VCUI;
    [SerializeField] private GameObject DS3;
    [SerializeField] private GameObject MW;
    [SerializeField] private GameObject SB;
    [SerializeField] private GaussianSplatRenderer GaussianRendererObject;
    [SerializeField] private bool MenuToggle = false;
    [SerializeField] private bool toggle = false;
    [SerializeField] private OVRPassthroughLayer OVRpassThrough;
    [SerializeField] private float passthroughOpacity = 1.0f;
    [SerializeField] private LayerMask maskAllLayers;    // “模式A”所用的Culling Mask（比如包含所有层）
    [SerializeField] private LayerMask maskOnlyGS;
    private float value = 1;
    private bool transparencyFlag;
    private bool flagSB = false;

    public bool TransparencyFlag
    {
        get { return transparencyFlag; } set { transparencyFlag = value; }
    }

    // Start is called before the first frame update
    void Start()
    {
        VCUI.SetActive(false);
        GaussianRendererObject.m_SplatScale = 0;
        StartCoroutine(TrackPrimaryButton());
        StartCoroutine(WatchMenuButton());
    }

    // Update is called once per frame

    public void ResetState()
    {
        OVRpassThrough.gameObject.SetActive(false);
        flagSB = false;
        DS3.SetActive(false);
        MW.SetActive(false);
        SB.SetActive(false);
        OVRpassThrough.overlayType = OVROverlay.OverlayType.Underlay;

        OVRpassThrough.gameObject.SetActive(true);

    }

    public void ActivateSB() 
    {

        flagSB = true;
        GaussianRendererObject.m_SplatScale = 1;
        SB.SetActive(true);
        OVRpassThrough.overlayType = OVROverlay.OverlayType.Overlay;
        VCUI.SetActive(false);

    }

    public void SBFlag() 
    { 
        flagSB = !flagSB;
    }


    IEnumerator WatchMenuButton()
    {
        while (true)
        {

            if (OVRInput.GetDown(OVRInput.Button.Start, OVRInput.Controller.LTouch))
            {
                MenuToggle = !MenuToggle;

                VCUI.SetActive(MenuToggle);
                
                if (!VCUI.activeSelf)
                {
                    GaussianRendererObject.m_SplatScale = 0;
                    OVRpassThrough.textureOpacity = 1;
                    ResetState();
                }
                Debug.Log("WatchMenuButton");

            }

            yield return null;
        }
    }

    IEnumerator TrackPrimaryButton()
    {

        while (true)
        {
            if (flagSB)
            {
                if (OVRInput.GetDown(OVRInput.RawButton.B, OVRInput.Controller.RTouch))
                {
                    toggle = !toggle;
                    OVRpassThrough.textureOpacity = toggle ? 0 : 1;

                   
                }
            }





            yield return null; // 每帧检查一次
        }
    }


    public void ActivateDS3()
    {
        GaussianRendererObject.m_SplatScale = 1;
        DS3.SetActive(true);
        VCUI.SetActive(false);
    }

    public void ActivateMW() {
        GaussianRendererObject.m_SplatScale = 1;
        MW.SetActive(true);
        VCUI.SetActive(false);
    }

    


    IEnumerator TrackThumbstickOnChange()
    {
        while (true)
        {
                
             
            float axisX = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick).x;

            // 加死区避免微小抖动误判



            Debug.Log($"摇杆发生变化: {axisX}");

            // 做出响应（比如增减 value）
            float sensitivity = 0.5f;
            float valueChange = axisX * sensitivity * Time.deltaTime;

            value += valueChange;
            value = Mathf.Clamp01(value);

            // 应用
            GaussianRendererObject.m_OpacityScale = value;
/*             
*/            

            

            yield return null;
        }
    }


}
