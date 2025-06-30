using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine.UI;
using System.Threading;
using Oculus.Interaction;
using MixedReality.Toolkit;
using UnityEngine.InputSystem;

public class OptimizedOculusRayDetector : MonoBehaviour
{
    [Header("Distance Grab Interactor")]
    public DistanceGrabInteractor distanceGrabInteractor;

    private Coroutine checkCoroutine;

    void OnEnable()
    {
        // 启动协程
        checkCoroutine = StartCoroutine(CheckObjectCoroutine());
    }

    void OnDisable()
    {
        if (checkCoroutine != null)
            StopCoroutine(checkCoroutine);
    }

    IEnumerator CheckObjectCoroutine()
    {
        while (true)
        {
            yield return null;  // 等待下一帧

            // 检测右手扳机是否刚刚被按下
            if (OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.RTouch))
            {
                if (distanceGrabInteractor.HasCandidate)
                {
                    var interactable = distanceGrabInteractor.Candidate;
                    Debug.Log($"当前指向物体: {interactable.transform.name}");
                }
                else
                {
                    Debug.Log("当前未指向任何可交互物体");
                }
            }
        }
    }
}
