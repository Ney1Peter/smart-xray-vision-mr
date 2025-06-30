using Oculus.Interaction;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using System.Threading.Tasks;
using System.IO;
using Oculus.Interaction.HandGrab;
using Meta.WitAi;
using UnityEngine.EventSystems;


public class LabellingManager : MonoBehaviour
{
    [SerializeField] private string userID;
    [SerializeField] private GameObject targetUI;
    [SerializeField] private DistanceGrabInteractor distanceGrabInteractor;
    [SerializeField] private RayInteractor rayInteractor;
    [SerializeField] private int groupNumber;
    private Coroutine checkCoroutine;


    private bool isTraining;
    private bool isSaved;

    private GameObject correctLabelInformation;
    private float OveralldDetectionTime;
    private float visibleDetectionTime;
    private Vector3 localisedPosition;
    private string detectedObjectName;
    private string detectedChangeType;
    private string confidenceScore;
    private string correctChangeType;
    private string correctObjectType;
    private string sceneCondition;
    private Vector3 correctPosition;
    private readonly object bufferLock = new object();
    private Dictionary<string, List<LabelFormat>> allLabelDic = new Dictionary<string, List<LabelFormat>>();
    private List<string> userInputDataBuffer = new List<string>();
    private string filePath;
    private bool isDetected;
    private Outline currentOutline;
    private bool indexIntroFlag = false;

    public bool IndexIntroFlag
    {
        get { return indexIntroFlag; }
        set { indexIntroFlag = value; }
    }
    private string uINumber;
    public string UINumber
    {
        get { return uINumber; }
        set { uINumber = value; }
    }

    // Start is called before the first frame update

    private void OnEnable()
    {
        checkCoroutine = StartCoroutine(CheckObjectCoroutine());
        
    }
    void OnDisable()
    {
        if (checkCoroutine != null)
            StopCoroutine(checkCoroutine);
        int currentSceneIndex = SceneManager.GetActiveScene().buildIndex;
        if (currentSceneIndex != 0 && !isTraining && !isSaved)
            FileHandler.SaveToJSON<LabelFormat>(allLabelDic, userID + "_UI" + UINumber + "_Group" + groupNumber + ".json");
        
    }
    void Start()
    {
        DataClear();
        
        
        targetUI.gameObject.SetActive(false);
        filePath = Application.persistentDataPath + "/" + userID + "_UI" + UINumber + "_Group" + groupNumber + "_inputdata.csv";
        if (userID.Length < 1)
        {
            userID = "User";
        }


        
        isDetected = false;
    }


    private void DataClear()
    {
        OveralldDetectionTime = 0f; visibleDetectionTime = 0f; localisedPosition = Vector3.zero;
        detectedObjectName = string.Empty;
        detectedChangeType = string.Empty;
        isDetected = false;
    }

    public void ToggleMessageDelivery(Toggle toggle)
    {
        if (toggle != null)
        {
            Debug.Log(toggle.name);

        }


    }

    public void ChangeTypeDelivery(Toggle toggle)
    {
        if (toggle != null)
        {
            detectedChangeType = toggle.name;
            Debug.Log(toggle.name);

        }
    }

    public void ConfidenceDelivery(Toggle toggle)
    {
        if (toggle != null)
        {
            confidenceScore = toggle.name;
            Debug.Log(toggle.name);


        }
    }



    public void BottonMessageDelivery(Button button)
    {
        if (button != null)
        {
            Debug.Log(button.name);

        }


    }

    public void ResetUI()
    {
        // 重置所有Toggle
        foreach (Toggle toggle in targetUI.GetComponentsInChildren<Toggle>())
        {
            toggle.isOn = false;
        }

        foreach (Button button in targetUI.GetComponentsInChildren<Button>())
        {
            var animator = button.GetComponent<Animator>();
            if (animator != null)
            {
                animator.Play("Normal", 0, 0);  
                animator.Update(0);
            }

            button.interactable = true; 
        }


    }

    IEnumerator CheckObjectCoroutine()
    {
        while (true)
        {
            // 检测右手扳机是否刚刚被按下
            if (OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.RTouch))
            {

                if (rayInteractor.HasCandidate)
                {
                    var tempInteractable = rayInteractor.Candidate;
                    if (tempInteractable.gameObject.CompareTag("UI")){
                        yield return null; // 结束当前帧的循环，等待下一帧
                        continue;
                    }

                }

                if (distanceGrabInteractor.HasCandidate)
                {
                    GameObject interactable = distanceGrabInteractor.Candidate.gameObject;
                    Debug.Log($"The current interacted object: {interactable.transform.name}");
                    GetGroundTruth(interactable);
                    detectedObjectName = interactable.transform.name;
                    localisedPosition = interactable.transform.position;
                    // Removed targetUI.SetActive(true) from here

                    // Start the coroutine and wait for it to complete
                    yield return StartCoroutine(EnableOutlineNextFrame(interactable.gameObject));

                    // AFTER EnableOutlineNextFrame has run, re-check the Outline component's state
                    if (interactable.gameObject.TryGetComponent<Outline>(out Outline currentOutline))
                    {
                        if (currentOutline.enabled)
                        {
                            // Outline is now active, so show the UI
                            targetUI.SetActive(true);
                            isDetected = true;
                        }
                        else
                        {
                            // Outline is now inactive, so hide the UI
                            targetUI.SetActive(false);
                            isDetected = false;
                        }
                    }
                    else
                    {
                        // No Outline component exists (shouldn't happen if it's added),
                        // so UI should probably be hidden.
                        targetUI.SetActive(false);
                        isDetected = false;
                    }
                }
            }
            yield return null;
        }
    }


    IEnumerator EnableOutlineNextFrame(GameObject obj)
    {
        yield return null; // 等待一帧，确保 Awake/Start 已执行

        if (obj.TryGetComponent<Outline>(out Outline outline))
        {
            // If Outline component already exists
            if (outline.enabled)
            {
                // If it's active, the user wants to deactivate it
                Debug.Log($"Outline for {obj.name} is active, deactivating.");
                outline.enabled = false;
                // No 'yield return false;' here
            }
            else
            {
                // If it exists but is inactive, activate it
                Debug.Log($"Outline for {obj.name} is inactive, activating.");
                outline.enabled = true;
                outline.OutlineColor = Color.blue; // Set color (if needed)
                outline.OutlineWidth = 4;           // Set width (if needed)
                // No 'yield return true;' here
            }
        }
        else
        {
            // If no Outline component, add one and activate
            Debug.Log($"No Outline component found on {obj.name}, adding and activating.");
            outline = obj.AddComponent<Outline>();
            outline.enabled = true; // Enabled by default after adding
            outline.OutlineColor = Color.blue; // Set highlight color
            outline.OutlineWidth = 4;           // Set highlight width
            // No 'yield return true;' here
        }

        // The coroutine simply finishes here.
    }

    private void GetGroundTruth(GameObject obj)
    {
        if (obj.TryGetComponent<ChangeProperties>(out var tempdata))
        {
            sceneCondition = tempdata.sceneCondition;
            correctObjectType = tempdata.changedObject;
            correctChangeType = tempdata.changedType; // 修改为正确字段
            correctPosition = tempdata.transform.position;
        }
        else
        {
            Debug.LogWarning($"GameObject '{obj.name}' does not have a ChangeProperties component.");
        }
    }


    public async Task WriteDataToFileAsync()
    {
        List<string> dataToWrite;
        lock (bufferLock)
        {
            dataToWrite = new List<string>(userInputDataBuffer);
            userInputDataBuffer.Clear();
        }

        using (StreamWriter writer = new StreamWriter(filePath, true))
        {
            foreach (string line in dataToWrite)
            {
                await writer.WriteLineAsync(line);
            }
        }
        Debug.Log("User Input Data write complete.");
    }

    IEnumerator CallWriteDataToFileAsync()
    {
        Task task = WriteDataToFileAsync();

        // 等待异步任务完成
        while (!task.IsCompleted)
        {
            yield return null;  // 暂停协程，等待下一帧继续
        }

        if (task.IsFaulted)
        {
            // 处理异常
            Debug.LogError("Task failed: " + task.Exception.ToString());
        }
        else
        {
            // 任务成功完成后的操作
            Debug.Log("Task completed successfully");
        }
    }

    public void SavingLabel()
    {
        if (true)
        {
            /*            stringList[12] = string.Join(",", pressRateList);
                        stringList[13] = CalculateAverageEulerAngularVelocity(controllerAngularVelocitiesList).ToString();
                        stringList[14] = CalculateAverageEulerAngularVelocity(headAngularVelocitiesList).ToString();
                        stringList[16] = visiableNumber.ToString();
                        if (uINumber == "1")
                        {
                            stringList[17] = isStandardMode ? "Stancard" : "Zoom in";
                        }
                        else
                            stringList[17] = "None";*/

            int currentSceneIndex = SceneManager.GetActiveScene().buildIndex;
            if (true)
            {
                // 创建一个新的LabelFormat对象


                var objectFlag = detectedObjectName == correctObjectType ? "Correct" : "Wrong";
                var changeFlag = detectedChangeType == correctChangeType ? "Corret" : "Wrong";


                var localisedError = (currentSceneIndex >= 19 && currentSceneIndex <= 36) ? "None"
                                 : Vector3.Distance(localisedPosition, correctPosition).ToString();
                var newLabel = new LabelFormat(userID, uINumber, currentSceneIndex.ToString(),sceneCondition, 
                    OveralldDetectionTime.ToString(), visibleDetectionTime.ToString(), detectedObjectName, detectedChangeType,localisedPosition.ToString(),confidenceScore,correctObjectType,correctChangeType,correctPosition.ToString(),objectFlag,changeFlag,localisedError
                    );

                // 获取当前场景的名称作为字典的键
                string currentSceneName = currentSceneIndex.ToString();

                // 检查字典中是否已经有这个键
                if (allLabelDic.ContainsKey(currentSceneName))
                {
                    // 如果存在，向对应的列表中添加新的LabelFormat对象
                    allLabelDic[currentSceneName].Add(newLabel);
                }
                else
                {
                    // 如果不存在，创建一个新的列表并添加到字典中
                    List<LabelFormat> newList = new List<LabelFormat> { newLabel };
                    allLabelDic.Add(currentSceneName, newList);
                }
                //WriteDataToFile();
                  FileHandler.SaveToJSON<LabelFormat>(allLabelDic, userID + "_UI" + UINumber + "_Group" + groupNumber + ".json");
/*                StartCoroutine(CallWriteDataToFileAsync());
*/                Debug.Log("Data has been saved.");
                //FileHandler.SaveToJSON<LabelFormat>(myLabelDic, userID + "UI number_"+ UINumber + "Group number_"+groupNumber + ".json");
            }
        }


    }
}
