using UnityEngine;

public class ObjectRotationSyncWithYOffset : MonoBehaviour
{
    public Transform target; // 目标对象
    public float yOffset = 90f; // y轴的偏移量，默认为90度

    void Update()
    {
        if (target != null)
        {
            // 将位置与目标对象一致
            transform.position = target.position;

            // 获取目标对象的旋转
            Vector3 targetRotation = target.eulerAngles;

            // 创建一个新的旋转，在 y 轴添加偏移量
            transform.rotation = Quaternion.Euler(transform.rotation.eulerAngles.x, targetRotation.y + yOffset, transform.rotation.eulerAngles.z);
        }
    }
}
