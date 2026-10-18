using System;
using System.IO;
using UnityEngine;

public class XunfeiConfig : MonoBehaviour
{
    [Header("讯飞配置")]
    [Tooltip("密钥从本地 JSON 文件读取，不在 Inspector 中填写。")]

    [SerializeField]
    private bool showConfigPathInLog = true;

    private string appId;
    private string apiKey;
    private string apiSecret;

    private bool loaded;
    private bool loadSucceeded;

    public string AppId
    {
        get
        {
            EnsureLoaded();
            return appId;
        }
    }

    public string ApiKey
    {
        get
        {
            EnsureLoaded();
            return apiKey;
        }
    }

    public string ApiSecret
    {
        get
        {
            EnsureLoaded();
            return apiSecret;
        }
    }

    private string ConfigPath
    {
        get
        {
            return Path.Combine(Application.persistentDataPath,"xunfei_config.json");
        }
    }

    [Serializable]
    private class XunfeiConfigData
    {
        public string appId;
        public string apiKey;
        public string apiSecret;
    }

    private void Awake()
    {
        Debug.Log("【配置诊断A】XunfeiConfig.Awake 已执行", this);

        LoadConfig();

        Debug.Log("【配置诊断B】LoadConfig 调用已返回", this);
    }

    /// <summary>
    /// 从本地 JSON 读取讯飞配置。
    /// </summary>
    public void LoadConfig()
    {
        loaded = true;
        loadSucceeded = false;

        appId = string.Empty;
        apiKey = string.Empty;
        apiSecret = string.Empty;

        Debug.Log("【测试1】已经执行到配置路径输出");

        if (showConfigPathInLog)
            Debug.Log("讯飞本地配置文件路径：" + ConfigPath);

        Debug.Log("【测试2】即将检查配置文件是否存在");

        Debug.Log("【测试3】File.Exists = " + File.Exists(ConfigPath));

        if (!File.Exists(ConfigPath))
        {
            Debug.LogWarning("没有找到讯飞本地配置文件，请按说明创建：" + ConfigPath);
            return;
        }

        try
        {
            string json = File.ReadAllText(ConfigPath);

            XunfeiConfigData data =JsonUtility.FromJson<XunfeiConfigData>(json);

            if (data == null)
            {
                Debug.LogError("讯飞配置文件解析失败。");
                return;
            }

            appId = data.appId ?? string.Empty;
            apiKey = data.apiKey ?? string.Empty;
            apiSecret = data.apiSecret ?? string.Empty;
            Debug.Log("【字段检查】appId 为空：" +string.IsNullOrWhiteSpace(appId));

            Debug.Log("【字段检查】apiKey 为空：" +string.IsNullOrWhiteSpace(apiKey));

            Debug.Log("【字段检查】apiSecret 为空：" +string.IsNullOrWhiteSpace(apiSecret));
            
            if (!IsValid())
            {
                appId = string.Empty;
                apiKey = string.Empty;
                apiSecret = string.Empty;

                Debug.LogWarning("讯飞配置不完整，请检查本地 JSON 文件。");
                return;
            }

            loadSucceeded = true;
            Debug.Log("讯飞本地配置加载成功。");
        }
        catch (Exception e)
        {
            appId = string.Empty;
            apiKey = string.Empty;
            apiSecret = string.Empty;

            Debug.LogError("读取讯飞配置失败：" + e.Message);
        }
    }

    private void EnsureLoaded()
    {
        if (!loaded)
        {
            LoadConfig();
        }
    }

    public bool IsValid()
    {
        EnsureLoaded();

        return !string.IsNullOrWhiteSpace(appId)&& !string.IsNullOrWhiteSpace(apiKey)&& !string.IsNullOrWhiteSpace(apiSecret);
    }
}