using UnityEngine;
using Oculus.Interaction;
using Oculus.Interaction.Grab;
using Oculus.Interaction.GrabAPI;
using Oculus.Interaction.Input;
using System;
using Oculus.Interaction.HandGrab;

public class RedirectedTransform : MonoBehaviour
{
    // 主动控制的物体 (A)
    public HandGrabInteractable sourceObject;

    public Transform initialTransformation;

    // 跟随物体 (B)
    public Transform targetObject;

    [Header("Gain设置（0~1之间，表示比例）")]
    [Range(0f, 1f)]
    public float positionGain = 0.5f;

    [Range(0f, 1f)]
    public float rotationGain = 0.5f;

    private Vector3 lastPosition;
    private Quaternion lastRotation;
    private Vector3 initialPosition;
    private Quaternion initialRotation;

    void Start()
    {
        if (sourceObject == null || targetObject == null)
        {
            Debug.LogError("请设置sourceObject和targetObject！");
            enabled = false;
            return;
        }

        initialPosition = sourceObject.transform.position;
        initialRotation = sourceObject.transform.rotation;
        lastPosition = sourceObject.transform.position;
        lastRotation = sourceObject.transform.rotation;
    }

    void Update()
    {
/*        if (sourceObject.State != InteractableState.Select)
            return;*/
        if (sourceObject.State == InteractableState.Select)
        {
            Vector3 positionDelta = sourceObject.transform.position - lastPosition;
            Quaternion rotationDelta = sourceObject.transform.rotation * Quaternion.Inverse(lastRotation);

            Vector3 redirectedPositionDelta = positionDelta * positionGain;
            Quaternion redirectedRotationDelta = Quaternion.Slerp(Quaternion.identity, rotationDelta, rotationGain);

            targetObject.position += redirectedPositionDelta;
            targetObject.rotation = redirectedRotationDelta * targetObject.rotation;

            lastPosition = sourceObject.transform.position;
            lastRotation = sourceObject.transform.rotation;
        }
        else
        {
            if (initialPosition != lastPosition || initialRotation != lastRotation)
            {
                lastPosition = initialPosition;
                lastRotation = initialRotation;
            }
            else
                return;
        }

    }
}
