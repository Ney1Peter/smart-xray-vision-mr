using System;
using System.Collections.Generic;
[Serializable]
public class LabelDic
{
    public Dictionary<string, List<LabelFormat>> labelDic;
    public LabelDic(Dictionary<string, List<LabelFormat>> labelDic)
    {
        this.labelDic = labelDic;
    }


}
