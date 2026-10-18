using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;//字符串编码转换
using UnityEngine;
using UnityEngine.Networking;//UnityWebRequest，Unity 用来发 http 网络请求

//WebSocket（讯飞语音）
//1. 长连接：建立一次连接，通道一直保持打开，持续双向收发数据
//2. 流式实时：一边说话一边收识别结果；一边发文本一边收音频流，持续不断来回传数据
//3. 适合：语音实时听写、实时语音合成

//HTTP POST（Ollama、智谱大模型）
//1. 短连接：发一次请求 → 服务器一次性返回全部结果 → 连接直接关掉。一问一答，完事就断
//2. 不是双向持续通道，请求发出去，等一次性返回响应
//3. 适合：一次性问答、文本翻译（你这段代码就是用来翻译角色名字）
//POST：HTTP 请求方法。意思是把数据放在请求体里面发给服务器。 GET 是把参数拼在网址后面；POST 适合传大量数据，比如 JSON，调用大模型 API 几乎全用 POST
public class OllamaManager : MonoBehaviour
{
    public static OllamaManager instance;

    //AI平台
    public enum AIProvider
    {
        Auto,
        Ollama,
        Zhipu
    }

    [Header("AI Provider")]
    [SerializeField] private AIProvider provider = AIProvider.Auto;

    [Header("Ollama")]
    //ollamaUrl：Ollama 本地服务地址。[localhost](https://localhost)就是本机电脑，11434 是 ollama 默认端口，/api/chat是聊天接口地址
    [SerializeField] private string ollamaUrl ="http://localhost:11434/api/chat";
    [SerializeField] private string ollamaModel ="llama3.2:latest";//使用的模型名字

    [Header("Zhipu")]
    [SerializeField] private ZhipuConfig zhipuConfig;
    [SerializeField] private string zhipuUrl = "https://open.bigmodel.cn/api/paas/v4/chat/completions";
    [SerializeField] private string zhipuModel = "glm-4-flash";

    [Header("Request")]
    //请求超时时间：120 秒。超过这个时间没有收到 AI 返回，直接判定请求失败
    [SerializeField] private int requestTimeout = 120;

    //AI聊天历史，保存全部对话上下文
    private List<OllamaMessage> chatHistory = new List<OllamaMessage>();

    //AI回复事件
    public static Action<string> OnResponseReceived;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
        //切换场景的时候，这个 GameObject 不会被销毁，AI 对话历史不会丢
        DontDestroyOnLoad(gameObject);
    }

    /// <summary>
    /// 从 ZhipuConfig 获取本地 API Key。
    /// 如果 Inspector 没有拖入组件，则自动从当前物体查找。
    /// </summary>
    private string GetZhipuApiKey()
    {
        if (zhipuConfig == null)
        {
            zhipuConfig = GetComponent<ZhipuConfig>();
        }

        if (zhipuConfig == null)
        {
            Debug.LogError("找不到 ZhipuConfig 组件，请将其添加到 OllamaManager 所在的 GameObject。");
            return "";
        }

        if (!zhipuConfig.IsValid())
        {
            Debug.LogError("智谱 API Key 无效，请检查本地 zhipu_config.json 文件。");
            return "";
        }

        return zhipuConfig.ApiKey;
    }

    private void Start()
    {
        Initialize();//调用初始化函数
    }

    //初始化AI
    public void Initialize()
    {
        chatHistory.Clear();//清空历史聊天记录
        //添加 system 角色提示词：system是系统提示
        chatHistory.Add(new OllamaMessage("system","You are a Unity expert."));
        Debug.Log("OllamaManager 初始化完成。当前AI平台：" +GetActualProvider());
    }

    //获取当前实际使用的平台
    //如果编辑器手动选了 Ollama 或者 Zhipu，直接返回选中的
    //如果是 Auto 自动模式：Windows/Mac/Linux 独立程序、Unity 编辑器 → 使用本地 Ollama。Android 安卓 /iOS 苹果手机 → 使用智谱云端 API（手机不能跑本地 Ollama）
    public AIProvider GetActualProvider()
    {
        if (provider != AIProvider.Auto)
        {
            return provider;
        }

        //#if是平台预编译指令，不是运行时判断，打包的时候就决定哪段代码保留
#if UNITY_EDITOR || UNITY_STANDALONE_WIN || UNITY_STANDALONE_OSX || UNITY_STANDALONE_LINUX

        return AIProvider.Ollama;

#elif UNITY_ANDROID || UNITY_IOS

        return AIProvider.Zhipu;

#else

        return AIProvider.Ollama;

#endif
    }

    //设置当前角色
    public void SetRole(string firstPrompt)
    {
        //重置 AI 角色提示词：清空全部聊天历史，把 system 提示词换成传入的 firstPrompt。如果传入为空，就默认用 Unity 专家
        chatHistory.Clear();

        if (!string.IsNullOrWhiteSpace(firstPrompt))
        {
            chatHistory.Add(new OllamaMessage("system",firstPrompt));
        }
        else
        {
            chatHistory.Add(new OllamaMessage("system","You are a Unity expert."));
        }
        Debug.Log("AI角色已经设置：" +firstPrompt);
    }

    //加载历史消息
    public void LoadHistory(ChatGPTxUnity.Message[] history)
    {
        chatHistory.Clear();

        //如果没有历史消息
        if (history == null ||history.Length == 0)
        {
            chatHistory.Add(new OllamaMessage("system","You are a Unity expert."));
            return;
        }

        //加入历史消息
        for (int i = 0;i < history.Length;i++)
        {
            ChatGPTxUnity.Message message =history[i];

            if (message == null)
            {
                continue;
            }

            string role;

            //History里面第一条消息是角色Prompt，所以第一条消息作为system
            if (i == 0)
            {
                role = "system";
            }
            else if (message.isUser)
            {
                role = "user";
            }
            else
            {
                role = "assistant";
            }

            chatHistory.Add(new OllamaMessage(role,message.content));
        }

        Debug.Log("AI聊天历史加载完成，消息数量：" +chatHistory.Count);
    }

    //发送消息
    //对外调用入口：别的脚本调用OllamaManager.instance.Chat("你好")就可以提问 AI
    //逻辑：1. 判断输入文本不能是空，空就警告直接返回2. 把用户这句话加入聊天历史（user 角色消息）3. 获取当前 AI 平台4. 启动协程，分别调用 Ollama 或者智谱的网络请求函数
    public void Chat(string userMessage)
    {
        if (string.IsNullOrWhiteSpace(userMessage))
        {
            Debug.LogWarning("发送给AI的消息不能为空。");
            return;
        }

        chatHistory.Add(new OllamaMessage("user",userMessage));

        AIProvider actualProvider =GetActualProvider();

        Debug.Log("准备发送AI消息，当前平台：" +actualProvider);

        if (actualProvider == AIProvider.Ollama)
        {
            StartCoroutine(ChatWithOllama());
        }
        else if (actualProvider == AIProvider.Zhipu)
        {
            StartCoroutine(ChatWithZhipu());
        }
    }

    private IEnumerator ChatWithOllama()
    {
        OllamaRequest requestData =new OllamaRequest();
        requestData.model =ollamaModel;
        requestData.messages = chatHistory;
        requestData.stream =false;//非流式，等 AI 全部生成完一次性返回全部文字（不是一点点流返回）
        //把 C# 类序列化成 JSON 字符串，用来 POST 发给 Ollama 接口
        string json =JsonUtility.ToJson(requestData);

        Debug.Log("发送到Ollama的请求：" +json);

        //UnityWebRequest 是Unity自带的网络请求工具，用来在 Unity 里发 HTTP 请求（GET / POST），替代 C# 原生 HttpClient，适配 Unity 各个平台（安卓、Windows 等），必须在协程 IEnumerator 里面使用
        //using()：自动释放网络资源，请求结束自动清理。创建一个 POST 请求，目标地址是 ollama 的接口地址ollamaUrl
        using (UnityWebRequest request =new UnityWebRequest(ollamaUrl,"POST"))
        {
            //Encoding.UTF8.GetBytes(json)：把 json 字符串转成字节数组，作为 POST 请求的 body
            //网络传输只能传字节，不能直接传字符串。把我们拼好的 JSON 文本，转成 UTF-8 编码的字节数组，这就是要发给 Ollama 服务器的数据
            byte[] bodyRaw =Encoding.UTF8.GetBytes(json);
            //UploadHandlerRaw：上传的数据。UploadHandlerRaw代表：把上面准备好的字节数组，作为本次 POST 要上传的内容
            request.uploadHandler =new UploadHandlerRaw(bodyRaw);
            //DownloadHandlerBuffer：接收服务器返回的文本，存到内存缓冲区。DownloadHandlerBuffer：服务器返回的所有文本，全部放到内存缓冲区里保存，后面我们读取.text就能拿到返回字符串
            request.downloadHandler =new DownloadHandlerBuffer();
            //请求头 Content-Type:application/json：告诉服务器，我发给你的是 JSON
            request.SetRequestHeader("Content-Type","application/json");
            request.timeout =requestTimeout;//设置超时时间
            //协程等待网络请求完成，这一行会暂停函数，等网络回来再继续往下跑
            yield return request.SendWebRequest();

            //网络请求成功
            if (request.result ==UnityWebRequest.Result.Success)
            {
                string responseText =request.downloadHandler.text;

                Debug.Log("Ollama返回：" +responseText);

                //拿到返回的 json 文本，JsonUtility.FromJson 把 json 转成 C# OllamaResponse 对象
                OllamaResponse response =JsonUtility.FromJson<OllamaResponse>(responseText);

                if (response != null &&response.message != null)
                {
                    //取出 AI 回答内容，添加到聊天历史，角色是 assistant
                    //OnResponseReceived?.Invoke(assistantMessage); 触发事件，通知外部脚本 AI 回答来了
                    string assistantMessage =response.message.content;
                    chatHistory.Add(new OllamaMessage("assistant",assistantMessage));
                    OnResponseReceived?.Invoke(assistantMessage);
                }
                else
                {
                    Debug.LogError("Ollama返回的数据无法解析。");
                }
            }
            else
            {
                Debug.LogError("Ollama请求失败：" +request.error);
                Debug.LogError("Ollama返回内容：" +(request.downloadHandler != null ? request.downloadHandler.text : "无返回数据"));
            }
        }
    }


    private IEnumerator ChatWithZhipu()
    {
        string apiKey = GetZhipuApiKey();

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            yield break;
        }

        ZhipuRequest requestData =new ZhipuRequest();
        requestData.model =zhipuModel;
        requestData.messages =chatHistory;
        requestData.stream =false;
        string json =JsonUtility.ToJson(requestData);

        Debug.Log("准备发送智谱AI请求。");

        using (UnityWebRequest request =new UnityWebRequest(zhipuUrl,"POST"))
        {
            byte[] bodyRaw =Encoding.UTF8.GetBytes(json);
            request.uploadHandler =new UploadHandlerRaw(bodyRaw);
            request.downloadHandler =new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type","application/json");
            //请求头必须带上 Authorization Bearer + apiKey 做身份认证
            request.SetRequestHeader("Authorization", "Bearer " + apiKey);
            request.timeout =requestTimeout;
            yield return request.SendWebRequest();

            if (request.result ==UnityWebRequest.Result.Success)
            {
                string responseText =request.downloadHandler.text;

                Debug.Log("智谱AI请求成功。");

                ZhipuResponse response =JsonUtility.FromJson<ZhipuResponse>(responseText);
                if (response != null &&response.choices != null &&response.choices.Length > 0 &&response.choices[0].message != null)
                {
                    string assistantMessage =response.choices[0].message.content;
                    chatHistory.Add(new OllamaMessage("assistant",assistantMessage));
                    OnResponseReceived?.Invoke(assistantMessage);
                }
                else
                {
                    Debug.LogError("智谱AI返回的数据无法解析。");
                    Debug.LogError("返回内容：" +responseText);
                }
            }
            else
            {
                Debug.LogError("智谱AI请求失败：" +request.error);
                Debug.LogError("HTTP状态码：" +request.responseCode);
                Debug.LogError("返回内容：" +(request.downloadHandler != null ? request.downloadHandler.text : "无返回数据"));
            }
        }
    }

    public IEnumerator TranslateRoleName(string originalName,Action<string, string> callback)
    {
        yield return StartCoroutine(TranslateName(originalName,callback));
    }

    public IEnumerator TranslateName(string originalName,Action<string, string> callback)
    {
        if (string.IsNullOrWhiteSpace(originalName))
        {
            callback?.Invoke("", "");
            yield break;//空文本直接返回空
        }

        originalName =originalName.Trim();
        Debug.Log("开始翻译 Role / Category 名字：" +originalName);
        AIProvider actualProvider =GetActualProvider();
        //ContainsChineseCharacters判断原文有没有中文，返回布尔值 true/false
        bool sourceIsChinese =ContainsChineseCharacters(originalName);

        string systemPrompt;
        if (sourceIsChinese)
        {
            //只输出固定格式 JSON {"english":"xxx","chinese":"xxx"}，不要多余文字
            //禁止输出拼音
            systemPrompt =
                "You are a UI localization translator.\n" +
                "The source text is Simplified Chinese.\n" +
                "Translate the source text into natural English by MEANING, not pronunciation.\n" +
                "The Chinese value MUST be exactly the original source text.\n" +
                "NEVER output Hanyu Pinyin.\n" +
                "NEVER output Chinese pronunciation or romanization.\n" +
                "NEVER treat a Chinese character as an English acronym.\n" +
                "A one-character Chinese name must be translated according to its meaning.\n" +
                "Examples:\n" +
                "爱 -> Love\n" +
                "钱 -> Money\n" +
                "心 -> Heart\n" +
                "梦 -> Dream\n" +
                "光 -> Light\n" +
                "雨 -> Rain\n" +
                "Return ONLY valid JSON in exactly this format:\n" +
                "{\"english\":\"...\",\"chinese\":\"...\"}\n" +
                "The chinese value MUST be exactly the original source text.\n" +
                "The english value MUST be a natural English translation, never pinyin.\n" +
                "Do not explain anything.\n" +
                "Do not add Markdown.";
        }
        else
        {
            systemPrompt =
                "You are a UI localization translator.\n" +
                "The source text is English or non-Chinese text.\n" +
                "The English value MUST be exactly the original source text.\n" +
                "Translate the source text into natural Simplified Chinese by MEANING.\n" +
                "Do NOT transliterate or romanize anything into pinyin.\n" +
                "Return ONLY valid JSON in exactly this format:\n" +
                "{\"english\":\"...\",\"chinese\":\"...\"}\n" +
                "The english value MUST be exactly the original source text.\n" +
                "The chinese value MUST be a natural Simplified Chinese translation.\n" +
                "Do not explain anything.\n" +
                "Do not add Markdown.";
        }

        //新建单独的消息列表（翻译不污染聊天对话历史，翻译是独立请求，不会加到上面 chatHistory 聊天记录）
        List<OllamaMessage> translationMessages =new List<OllamaMessage>();
        translationMessages.Add(new OllamaMessage("system",systemPrompt));
        //"Original name:\n" +originalName：发给 AI 的提示词，告诉 AI：原始名字是 xxx，请翻译
        //前面还有一条system消息，是系统提示词，规定 AI 翻译规则
        //translationMessages是 List，用来存放一整套对话上下文，一次性打包发给 Ollama 接口
        translationMessages.Add(new OllamaMessage("user","Original name:\n" +originalName));

        const int maxAttempts = 3;//最多重试 3 次

        for (int attempt = 1;attempt <= maxAttempts;attempt++)
        {
            string resultEnglish = "";
            string resultChinese = "";

            bool finished = false;//翻译失败

            if (actualProvider ==AIProvider.Ollama)
            {
                yield return StartCoroutine(TranslateNameWithOllama(translationMessages,originalName,sourceIsChinese,(english, chinese) =>
                        {
                            resultEnglish =english;
                            resultChinese =chinese;
                            finished =true;
                        }
                    )
                );
            }
            else if (actualProvider ==AIProvider.Zhipu)
            {
                yield return StartCoroutine(TranslateNameWithZhipu(translationMessages,originalName,sourceIsChinese,(english, chinese) =>
                        {
                            resultEnglish =english;
                            resultChinese =chinese;
                            finished =true;//标记翻译请求完成
                        }
                    )
                );
            }
            else
            {
                callback?.Invoke("", "");
                yield break;
            }

            while (!finished)
            {
                yield return null;//等待回调完成
            }

            //中文原文
            if (sourceIsChinese)
            {
                //中文永远使用用户原文
                resultChinese =originalName;
                //IsValidEnglishTranslation：校验英文是否合法，不能包含中文、不能是拼音
                if (IsValidEnglishTranslation(resultEnglish))
                {
                    Debug.Log("名字翻译成功：" +originalName +" → " +resultEnglish);
                    callback?.Invoke(resultEnglish.Trim(),resultChinese);
                    yield break;
                }
            }
            else
            {
                //英文原文永远使用用户原文
                resultEnglish =originalName;
                //中文必须真的包含中文
                if (IsValidChineseTranslation(resultChinese))
                {
                    Debug.Log("名字翻译成功：" +originalName +" → " +resultChinese);
                    callback?.Invoke(resultEnglish,resultChinese.Trim());
                    yield break;
                }
            }
            Debug.LogWarning("名字翻译结果无效：" +originalName +"，第 " +attempt +"/" +maxAttempts +" 次重试。");
            //循环里面防止瞬间疯狂调用 API，限流；简单延时等待，等上一步变量赋值完成
            yield return new WaitForSeconds(0.1f);
        }

        Debug.LogWarning("名字翻译最终失败，不保存错误翻译：" +originalName);
        callback?.Invoke("","");
    }

    private IEnumerator TranslateNameWithOllama(List<OllamaMessage> messages,string originalName,bool sourceIsChinese,Action<string, string> callback)
    {
        OllamaRequest requestData =new OllamaRequest();
        requestData.model =ollamaModel;
        requestData.messages =messages;
        requestData.stream =false;
        string json =JsonUtility.ToJson(requestData);

        using (UnityWebRequest request =new UnityWebRequest(ollamaUrl,"POST"))
        {
            request.uploadHandler =new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            request.downloadHandler =new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type","application/json");
            request.timeout =requestTimeout;
            yield return request.SendWebRequest();

            if (request.result ==UnityWebRequest.Result.Success)
            {
                string responseText =request.downloadHandler.text;
                OllamaResponse response =JsonUtility.FromJson<OllamaResponse>(responseText);

                if (response != null &&response.message != null)
                {
                    //调用ParseTranslationResult，把 AI 回答文本丢进去解析翻译结果，并且把回调传进去
                    ParseTranslationResult(response.message.content,originalName,sourceIsChinese,callback);
                }
                else
                {
                    callback?.Invoke("","");
                }
            }
            else
            {
                Debug.LogWarning("Ollama名字翻译失败：" +originalName +"，错误：" +request.error);
                callback?.Invoke("","");
            }
        }
    }

    private IEnumerator TranslateNameWithZhipu(List<OllamaMessage> messages,string originalName,bool sourceIsChinese,Action<string, string> callback)
    {
        string apiKey = GetZhipuApiKey();

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            callback?.Invoke("", "");
            yield break;
        }

        ZhipuRequest requestData =new ZhipuRequest();
        requestData.model =zhipuModel;
        requestData.messages =messages;
        requestData.stream =false;
        string json =JsonUtility.ToJson(requestData);

        using (UnityWebRequest request =new UnityWebRequest(zhipuUrl,"POST"))
        {
            request.uploadHandler =new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            request.downloadHandler =new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type","application/json");
            request.SetRequestHeader("Authorization", "Bearer " + apiKey);
            request.timeout =requestTimeout;
            yield return request.SendWebRequest();

            if (request.result ==UnityWebRequest.Result.Success)
            {
                string responseText =request.downloadHandler.text;
                ZhipuResponse response =JsonUtility.FromJson<ZhipuResponse>(responseText);

                if (response != null &&response.choices != null &&response.choices.Length > 0 &&response.choices[0].message != null)
                {
                    ParseTranslationResult(response.choices[0].message.content,originalName,sourceIsChinese,callback);
                }
                else
                {
                    callback?.Invoke("","");
                }
            }
            else
            {
                Debug.LogWarning("智谱名字翻译失败：" +originalName +"，错误：" +request.error);
                Debug.LogWarning("HTTP状态码：" +request.responseCode);
                callback?.Invoke("","");
            }
        }
    }

    //解析翻译结果
    private void ParseTranslationResult(string content,string originalName,bool sourceIsChinese,Action<string, string> callback)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            callback?.Invoke("","");
            return;
        }

        //调用ExtractJsonObject，从 AI 一大段文字里面，抠出{ ... }JSON 字符串。抠不出来就返回空
        string json =ExtractJsonObject(content);
        if (string.IsNullOrWhiteSpace(json))
        {
            callback?.Invoke("","");
            return;
        }

        try
        {
            //把抠出来的 JSON，转成RoleTranslationResponse对象，这个类只有两个字段english和chinese，对应 AI 输出的翻译结果
            RoleTranslationResponse result =JsonUtility.FromJson<RoleTranslationResponse>(json);
            if (result == null)
            {
                callback?.Invoke("","");
                return;
            }

            string english =result.english == null? "": result.english.Trim();
            string chinese =result.chinese == null? "": result.chinese.Trim();

            //中文原文强制保留
            if (sourceIsChinese)
            {
                chinese =originalName;
            }
            else
            {
                //英文原文强制保留
                english =originalName;
            }
            callback?.Invoke(english,chinese);
        }
        catch (Exception e)
        {
            Debug.LogWarning("翻译JSON解析失败：" +originalName +"\n" +e.Message +"\nAI返回：" +content);

            callback?.Invoke("","");
        }
    }

    //从AI返回内容里面提取JSON
    private static string ExtractJsonObject(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "";
        }

        int start =text.IndexOf('{');//找到第一个左大括号{的下标（JSON 开始位置）
        int end =text.LastIndexOf('}');//找到最后一个右大括号}的下标（JSON 结束位置）

        if (start < 0 ||end <= start)
        {
            return "";
        }
        //Substring：截取字符串，从 start 位置开始，截取长度end-start+1，拿到完整{...}JSON 文本
        return text.Substring(start,end - start + 1);
    }

    //判断字符串里面有没有中文
    private static bool ContainsChineseCharacters(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        for (int i = 0;i < text.Length;i++)
        {
            char c =text[i];
            if ((c >= '\u4E00' &&c <= '\u9FFF') ||(c >= '\u3400' &&c <= '\u4DBF'))
            {
                return true;
            }
        }
        return false;
    }

    //判断AI返回的英文是否有效
    private static bool IsValidEnglishTranslation(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }
        if (ContainsChineseCharacters(text))
        {
            return false;
        }

        string normalized =text.Trim().ToLowerInvariant();//全部转小写

        //不能是拼音
        if (LooksLikePinyin(normalized))
        {
            return false;
        }

        //允许小写英文字母、空格、- ' . , ! ? &这些名字常用符号
        //如果出现其他字符（数字、日文、中文）直接返回 false
        //最后判断：必须至少包含一个英文字母才算合法英文
        bool hasLetter =false;
        for (int i = 0;i < normalized.Length;i++)
        {
            char c =normalized[i];
            if (c >= 'a' &&c <= 'z')
            {
                hasLetter =true;
                continue;
            }
            if (c == ' ' ||c == '-' ||c == '\'' ||c == '.' ||c == ',' ||c == '!' ||c == '?' ||c == '&')
            {
                continue;
            }
            return false;
        }
        return hasLetter;
    }

    //判断AI返回的中文是否有效
    private static bool IsValidChineseTranslation(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return ContainsChineseCharacters(text);
    }

    //判断文本是不是汉语拼音
    public static bool LooksLikePinyin(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }
        string normalized =text.Trim().ToLowerInvariant();
        for (int i = 0;i < normalized.Length;i++)
        {
            char c =normalized[i];
            if (!(c >= 'a' &&c <= 'z') &&c != ' ' &&c != '-' &&c != '\'')
            {
                return false;
            }
        }

        //按 空格、-、' 把字符串切成单词数组，并且删掉分割出来的空字符串
        string[] words =normalized.Split(new char[]{ ' ','-','\''},StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return false;
        }

        //常见英文单词，这些即使碰巧属于拼音音节，也不能直接判定为拼音
        //哈希集合，查找速度极快，用来判断某个单词在不在列表里
        HashSet<string> commonEnglish =new HashSet<string> {"a","an","and","are","be","can","he","her","him","i","in","is","it","me","my","no","of","on","or","she","so","the","to","we","you","your","love","money","heart","dream","light","rain","like","lover","friend","person","girl","boy","family","home","star","moon","sun"};
        HashSet<string> pinyin =GetPinyinSyllables();

        bool allPinyin =true;//假设所有单词都是拼音，一旦发现不是，就改成 false
        bool hasLongPinyinWord =false;//标记是否存在长度≥2 的拼音片段

        for (int i = 0;i < words.Length;i++)
        {
            string word =words[i];
            if (commonEnglish.Contains(word))
            {
                allPinyin =false;
                break;
            }

            if (!pinyin.Contains(word))
            {
                allPinyin =false;
                break;
            }

            if (word.Length >= 2)
            {
                hasLongPinyinWord =true;
            }
        }
        //所有单词全部都是拼音音节，里面至少有一个单词长度≥2
        return allPinyin &&hasLongPinyinWord;
    }

    //标准普通话无声调拼音音节
    private static HashSet<string> GetPinyinSyllables()
    {
        string pinyinText =
            "a ai an ang ao " +
            "ba bai ban bang bao bei ben beng bi bian biao bie bin bing bo bu " +
            "ca cai can cang cao ce cen ceng cha chai chan chang chao che chen cheng chi chong chou chu chua chuai chuan chuang chui chun chuo ci cong cou cu cuan cui cun cuo " +
            "da dai dan dang dao de dei den deng di dian diao die ding diu dong dou du duan dui dun duo " +
            "e ei en eng er " +
            "fa fan fang fei fen feng fo fou fu " +
            "ga gai gan gang gao ge gei gen geng gong gou gu gua guai guan guang gui gun guo " +
            "ha hai han hang hao he hei hen heng hong hou hu hua huai huan huang hui hun huo " +
            "ji jia jian jiang jiao jie jin jing jiong jiu ju juan jue jun " +
            "ka kai kan kang kao ke kei ken keng kong kou ku kua kuai kuan kuang kui kun kuo " +
            "la lai lan lang lao le lei leng li lia lian liang liao lie lin ling liu long lou lu luan lue lun luo " +
            "ma mai man mang mao me mei men meng mi mian miao mie min ming miu mo mou mu " +
            "na nai nan nang nao ne nei nen neng ni nian niang niao nie nin ning niu nong nou nu nuan nuo " +
            "pa pai pan pang pao pei pen peng pi pian piao pie pin ping po pou pu " +
            "qi qia qian qiang qiao qie qin qing qiong qiu qu quan que qun " +
            "ran rang rao re ren reng ri rong rou ru rua ruan rui run ruo " +
            "sa sai san sang sao se sei sen seng sha shai shan shang shao she shei shen sheng shi shou shu shua shuai shuan shuang shui shun shuo si song sou su sua suan sui sun suo " +
            "ta tai tan tang tao te teng ti tian tiao tie ting tong tou tu tuan tui tun tuo " +
            "wa wai wan wang wei wen weng wo wu " +
            "xi xia xian xiang xiao xie xin xing xiong xiu xu xuan xue xun " +
            "ya yan yang yao ye yi yin ying yo yong you yu yuan yue yun " +
            "za zai zan zang zao ze zei zen zeng zha zhai zhan zhang zhao zhe zhei zhen zheng zhi zhong zhou zhu zhua zhuai zhuan zhuang zhui zhun zhuo zi zong zou zu zuan zui zun zuo";
        //以空格分割字符串，变成拼音数组，删掉分割产生的空字符串
        string[] syllables =pinyinText.Split(new char[]{' '},StringSplitOptions.RemoveEmptyEntries);
        //把拼音数组装进 HashSet，返回
        return new HashSet<string>(syllables);
    }

    //清空聊天历史
    public void ClearHistory()
    {
        chatHistory.Clear();
        chatHistory.Add(new OllamaMessage("system","You are a Unity expert."));
        Debug.Log("AI聊天历史已经清空。");
    }

    //获取当前聊天历史数量
    public int GetHistoryCount()
    {
        return chatHistory.Count;
    }

    //获取聊天历史
    public List<OllamaMessage> GetHistory()
    {
        return chatHistory;
    }
}

//Ollama数据结构
[Serializable]
public class OllamaRequest
{
    public string model;
    public List<OllamaMessage> messages;
    public bool stream;
}

[Serializable]
public class OllamaMessage
{
    public string role;
    public string content;

    public OllamaMessage(string role,string content)
    {
        this.role =role;
        this.content =content;
    }
}


[Serializable]
public class OllamaResponse
{
    public string model;
    public string created_at;//这条回答生成的时间戳
    public OllamaMessage message;
    //是否完成生成。stream=false 一次性返回的时候，done 一定是 true，代表 AI 已经把全部内容输出完毕
    public bool done;
}

//智谱数据结构
[Serializable]
public class ZhipuRequest
{
    public string model;
    public List<OllamaMessage> messages;
    public bool stream;
}

[Serializable]
public class ZhipuResponse
{
    public ZhipuChoice[] choices;//智谱返回结构是数组choices
}

[Serializable]
public class ZhipuChoice
{
    public OllamaMessage message;
}

//Role名字翻译返回数据
[Serializable]
public class RoleTranslationResponse
{
    public string english;
    public string chinese;
}