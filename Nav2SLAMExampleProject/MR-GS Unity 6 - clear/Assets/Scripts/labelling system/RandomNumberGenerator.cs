using System.Collections.Generic;

using System.IO;
using Newtonsoft.Json;
using UnityEngine;


public class RandomNumberGenerator
{



    // 用于生成乱序数字的方法
    public List<int> GenerateRandomNumbers(int startNumber)
    {
        List<int> numbers = new List<int>();
        
        // 生成数字
        for (int i = 0; i < 18; i++)
        {
            numbers.Add(startNumber + i * 6);
        }
        List<List<int>> dividedList = DivideAndShuffleList(numbers, 3);
        List<int> selectedElements = SelectElements(dividedList, 3);
        return selectedElements;
    }

    // 打乱列表的方法
    private void Shuffle<T>(List<T> list)
    {
        System.Random random = new System.Random();
        int n = list.Count;
        while (n > 1)
        {
            n--;
            int k = random.Next(n + 1);
            T value = list[k];
            list[k] = list[n];
            list[n] = value;
        }
    }

    // 生成包含6个小列表的大列表
    public List<List<int>> GenerateMultipleRandomNumberLists()
    {
        List<List<int>> bigList = new List<List<int>>();
        for (int startNumber = 1; startNumber <= 6; startNumber++)
        {
            var smallList = GenerateRandomNumbers(startNumber);
            //Shuffle<int>(smallList);
            bigList.Add(smallList);
        }
        return bigList;
    }
    public List<List<int>> GenerateAllRandomLists()
    {
        List<List<int>> bigList = new List<List<int>>();
        List<int> Group1 = new List<int>();
        List<int> Group2 = new List<int>();
        List<int> Group3 = new List<int>();

        for (int startNumber = 1; startNumber <= 6; startNumber++)
        {
            var smallList = GenerateRandomNumbers(startNumber);

            Group1.AddRange(smallList.GetRange(0, 6));
            Group2.AddRange(smallList.GetRange(6, 6));
            Group3.AddRange(smallList.GetRange(12, 6));

        }

        bigList.Add(ShuffleAndMerge(Group1));
        bigList.Add(ShuffleAndMerge(Group2));
        bigList.Add(ShuffleAndMerge(Group3));

        return bigList;
    }
    public static List<T> ShuffleAndMerge<T>(List<T> originalList)
    {
        int partSize = originalList.Count / 3; // 确定每一份的大致大小

        // 分成三份
        List<T> part1 = new List<T>(originalList.GetRange(0, partSize));
        List<T> part2 = new List<T>(originalList.GetRange(partSize, partSize));
        List<T> part3 = new List<T>(originalList.GetRange(2 * partSize, originalList.Count - 2 * partSize));

        // 分别打乱每一份
        ShuffleList(part1);
        ShuffleList(part2);
        ShuffleList(part3);

        // 合并打乱后的列表
        List<T> mergedList = new List<T>();
        mergedList.AddRange(part1);
        mergedList.AddRange(part2);
        mergedList.AddRange(part3);

        return mergedList;
    }

    public static List<List<T>> DivideAndShuffleList<T>(List<T> list, int partSize)
    {
        List<List<T>> dividedList = new List<List<T>>();
        for (int i = 0; i < list.Count; i += partSize)
        {
            var part = new List<T>(list.GetRange(i, Mathf.Min(partSize, list.Count - i)));
            ShuffleList(part);
            dividedList.Add(part);
        }
        return dividedList;
    }

    private static void ShuffleList<T>(List<T> list)
    {
        for (int i = 0; i < list.Count; i++)
        {
            T temp = list[i];
            int randomIndex = Random.Range(i, list.Count);
            list[i] = list[randomIndex];
            list[randomIndex] = temp;
        }
    }

    private static List<int> GenerateShuffledIndexList(int size)
    {
        List<int> list = new List<int>();
        for (int i = 0; i < size; i++)
        {
            list.Add(i);
        }
        ShuffleList(list);
        return list;
    }

    // 选择元素
    public static List<T> SelectElements<T>(List<List<T>> dividedList, int rounds)
    {
        List<T> selectedElements = new List<T>();
        for (int round = 0; round < rounds; round++)
        {
            List<int> listA = GenerateShuffledIndexList(dividedList.Count);
            foreach (var index in listA)
            {
                if (round < dividedList[index].Count) // Ensure the element exists
                {
                    selectedElements.Add(dividedList[index][round]);
                }
            }
        }
        return selectedElements;
    }

    public void SaveListOfListsToJson<T>(List<List<T>> listOfLists, string userID)
    {
        // 使用Newtonsoft.Json序列化
        string json = JsonConvert.SerializeObject(listOfLists, Formatting.Indented);

        string filePath = GetFilePath(userID);
        File.WriteAllText(filePath, json);
    }

    string GetFilePath(string userID)
    {
        string directoryPath = Application.persistentDataPath + "/SavedIndexes";
        if (!Directory.Exists(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }
        return directoryPath + "/" + userID + "_index.json";
    }


    public List<List<T>> LoadListOfListsFromJson<T>(string userID)
    {
        string filePath = GetFilePath(userID);

        if (File.Exists(filePath))
        {
            // 读取文件内容
            string json = File.ReadAllText(filePath);
            // 使用Newtonsoft.Json反序列化
            List<List<T>> listOfLists = JsonConvert.DeserializeObject<List<List<T>>>(json);
            return listOfLists;
        }
        else
        {
            Debug.LogError($"File not found: {filePath}");
            return null;
        }
    }


}
