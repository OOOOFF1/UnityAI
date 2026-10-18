using System;
using System.IO;
using UnityEngine;

public class ZhipuConfig : MonoBehaviour
{
    [Header("智谱配置")]
    [Tooltip("密钥从本地 JSON 文件读取，不在 Inspector 中填写。")]
    [SerializeField] private bool showConfigPathInLog = true;

    private string apiKey;
    private bool loaded;

    private string ConfigPath =>
        Path.Combine(Application.persistentDataPath,"zhipu_config.json");

    [Serializable]
    private class ZhipuConfigData
    {
        public string apiKey;
    }

    public string ApiKey
    {
        get
        {
            EnsureLoaded();
            return apiKey;
        }
    }

    private void Awake()
    {
        LoadConfig();
    }

    public void LoadConfig()
    {
        loaded = true;
        apiKey = string.Empty;

        if (showConfigPathInLog)
            Debug.Log("智谱本地配置路径：" + ConfigPath);

        if (!File.Exists(ConfigPath))
        {
            Debug.LogWarning("未找到智谱配置文件，请创建 zhipu_config.json。");
            return;
        }

        try
        {
            string json = File.ReadAllText(ConfigPath);
            ZhipuConfigData data =JsonUtility.FromJson<ZhipuConfigData>(json);

            if (data == null ||string.IsNullOrWhiteSpace(data.apiKey))
            {
                Debug.LogWarning("智谱配置文件中的 API Key 为空。");
                return;
            }

            apiKey = data.apiKey.Trim();
            Debug.Log("智谱本地配置加载成功。");
        }
        catch (Exception e)
        {
            Debug.LogError("读取智谱配置失败：" + e.Message);
        }
    }

    private void EnsureLoaded()
    {
        if (!loaded)
            LoadConfig();
    }

    public bool IsValid()
    {
        EnsureLoaded();
        return !string.IsNullOrWhiteSpace(apiKey);
    }
}
