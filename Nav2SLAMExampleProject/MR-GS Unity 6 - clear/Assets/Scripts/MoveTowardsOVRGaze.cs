using UnityEngine;

public class MoveTowardsOVRGaze : MonoBehaviour
{
    public Transform centerEyeAnchor;

    // 移动速度
    public float moveSpeed = 1.5f;

    void Start()
    {
        if (centerEyeAnchor == null)
        {
            GameObject anchorObj = GameObject.Find("CenterEyeAnchor");
            if (anchorObj != null)
            {
                centerEyeAnchor = anchorObj.transform;
            }
            else
            {
                Debug.LogError("未找到CenterEyeAnchor，请手动指定！");
            }
        }
    }

    void Update()
    {
        if (centerEyeAnchor == null) return;

        // 获取头盔朝向
        Vector3 forward = centerEyeAnchor.forward;
        Vector3 right = centerEyeAnchor.right;
        Vector3 up = centerEyeAnchor.up;

        // 根据手柄输入或按键输入移动（以Meta Quest的手柄为例）
        float horizontal = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick).x;
        float vertical = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick).y;

        // 垂直移动（上/下飘浮）例如使用右手柄的摇杆或按钮控制：
        float verticalMovement = OVRInput.Get(OVRInput.Axis2D.SecondaryThumbstick).y;

        // 计算移动方向（不考虑重力和碰撞）
        Vector3 moveDirection = (forward * vertical + right * horizontal + up * verticalMovement).normalized;

        // 移动物体（直接修改transform，无需Collider）
        transform.position += moveDirection * moveSpeed * Time.deltaTime;
    }
}
