using System;

[Serializable]
public class LabelFormat
{
    public string userID;
    public string UIClassID;
    public string SceneIndex;
    public string SceneCondition;

    public string overallDetectionTime;
    public string VisiableDetectionTime;
    public string ChangeObj;
    public string ChangeEvent;
    public string Position;
    public string ConfidenceScore;

    public string CorrectObject;
    public string CorrectChange;
    public string CorrectPosition;

    public string ObjectFlag;
    public string ChangeFlag;
    public string PositionError;
    
    




    public LabelFormat(string ID, string uiNumber, string index, string scondition,

        string time1, string visiableTime, string obj, string change1, string pos, string confidenceScore,
        
        string cObject, string cChange, string cPosition, string objectFlag, string changeFlag,
        string positionError)
    {
        userID = ID;
        UIClassID = uiNumber;
        overallDetectionTime = time1;
        ChangeObj = obj;
        ChangeEvent = change1;
        Position = pos;
        
        
        SceneCondition = scondition;
        CorrectObject = cObject;
        CorrectChange = cChange; 
        CorrectPosition = cPosition;
        ObjectFlag= objectFlag;
        ChangeFlag = changeFlag;
        PositionError = positionError;
        SceneIndex = index;
        VisiableDetectionTime = visiableTime;
        ConfidenceScore = confidenceScore;

    }
}
