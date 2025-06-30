using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class LabelFollower : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        this.transform.rotation = Quaternion.Lerp(this.transform.rotation, Quaternion.Euler(0, Camera.main.transform.rotation.eulerAngles.y, 0), Time.deltaTime);
    }
}
