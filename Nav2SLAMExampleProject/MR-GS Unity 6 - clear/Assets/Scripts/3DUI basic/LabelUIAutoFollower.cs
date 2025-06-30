using System.Collections;
using UnityEngine;

public class LabelUIAutoFollower : MonoBehaviour
{
    [Header("Smooth Follow Settings")]
    public float menuDeadZoneRotation = 15f;
    public float positionSmoothSpeed = 5f;
    public float rotationSmoothSpeed = 5f;

    private Coroutine followCoroutine;
    private float cibleAngle;

    void OnEnable()
    {
        followCoroutine = StartCoroutine(FollowCameraCoroutine());
    }

    void OnDisable()
    {
        if (followCoroutine != null)
            StopCoroutine(followCoroutine);
    }

    IEnumerator FollowCameraCoroutine()
    {
        cibleAngle = Camera.main.transform.rotation.eulerAngles.y;

        while (true)
        {
            Vector3 cible = Camera.main.transform.position;
            transform.position = Vector3.Lerp(transform.position, cible, Time.deltaTime * positionSmoothSpeed);

            Quaternion yangle = Quaternion.Euler(0, Camera.main.transform.rotation.eulerAngles.y, 0);
            Quaternion cangle = Quaternion.Euler(0, cibleAngle, 0);

            if (Quaternion.Angle(yangle, cangle) > menuDeadZoneRotation / 2)
            {
                cibleAngle = Camera.main.transform.rotation.eulerAngles.y;
            }

            Quaternion targetRotation = Quaternion.Euler(0, cibleAngle, 0);
            transform.rotation = Quaternion.Lerp(transform.rotation, targetRotation, Time.deltaTime * rotationSmoothSpeed);

            yield return null;
        }
    }
}
