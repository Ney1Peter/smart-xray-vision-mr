using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class HoldOnObject : MonoBehaviour
{
    // Start is called before the first frame update
    private void Awake()
    {
        // 注册场景加载事件
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // 获取加载的场景索引
        int loadedSceneIndex = scene.buildIndex;

        // 检查加载的场景索引是否不是115
        if (loadedSceneIndex != 115)
        {
            // 如果不是115，使这个对象在加载新场景时不被销毁
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            // 如果是115，考虑从DontDestroyOnLoad状态移除
            // 注意：直接移除DontDestroyOnLoad状态的官方方法有限，通常需要将其移至可销毁的新场景
            // 这里仅为示例，实际使用时可能需要根据具体情况调整逻辑
        }
    }

    private void OnDestroy()
    {
        // 注销场景加载事件
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }


}
