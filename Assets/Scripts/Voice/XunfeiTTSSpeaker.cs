using System;
using System.Collections;
using System.Text;
using UnityEngine;
using NativeWebSocket;

//wss://tts-api.xfyun.cn/v2/tts
public class XunfeiTTSSpeaker : MonoBehaviour
{
    [Header("讯飞 TTS 配置")]
    [SerializeField] private XunfeiConfig config;//讯飞配置对象

    [Header("发音人")]
    [SerializeField] private string voiceName = "xiaoyan";//讯飞发音人

    //传给讯飞业务参数，范围 0~100，50是默认正常速度/音调/音量
    [Header("语速")]
    [Range(0, 100)]
    [SerializeField] private int speed = 50;
    [Header("音高")]
    [Range(0, 100)]
    [SerializeField] private int pitch = 50;
    [Header("音量")]
    [Range(0, 100)]
    [SerializeField] private int volume = 50;

    [Header("AudioSource")]
    [SerializeField] private AudioSource audioSource;//用来播放生成好的语音 Clip

    //只读对外属性，给上层 Bridge 读取：speaker.IsSpeaking判断是否正在朗读。只能在这个脚本内部赋值
    public bool IsSpeaking { get; private set; }

    private WebSocket webSocket;//NativeWebSocket 的 WebSocket 实例，用来和讯飞服务器通信
    private Coroutine speakCoroutine;//保存 SpeakAsync 这个主协程引用
    private Coroutine playbackCoroutine;//音频播放等待协程：等 AudioClip 播放完
    //拼接音频 base64 字符串。讯飞服务器会分多段返回 audio，每一段都是 base64 字符串，不断 Append 拼接在一起，全部接收完一次性转成 PCM 字节数组
    private StringBuilder audioBase64Builder;

    //receivedEnd 只代表本次请求流程结束，不管成功还是失败
    private bool receivedEnd;//标记：是否收到讯飞最后一包（status=2），代表服务器音频全部发完
    private bool connectFinished;//WebSocket 连接流程是否走完（不管成功失败）
    private bool connectSuccess;//WebSocket 连接是否成功

    private string lastError;//保存错误信息，供上层查看
    //每次调用 SpeakAsync，currentRequestId++,作用：防止旧 WebSocket 回调干扰新请求
    private int currentRequestId;//请求版本号

    private void Awake()
    {
        //尝试拿物体身上 AudioSource；没有就自动添加 AudioSource 组件
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
        }
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }
        audioSource.playOnAwake = false;//游戏启动不自动播放声音
        IsSpeaking = false;//不在朗读
    }

    private void Update()
    {
        //NativeWebSocket 要求：每帧调用 DispatchMessageQueue
        //WebSocket收到的消息、事件会暂存消息队列，在主线程 Update 里面执行 OnMessage、OnOpen、OnClose 回调
        if (webSocket != null)
        {
            //WebSocket 收到服务器消息（讯飞返回的 json、音频 base64）是在子线程
            //Unity 禁止子线程直接操作 AudioClip、Debug.Log、修改组件变量，会直接崩溃。
            //NativeWebSocket 的做法：子线程收到消息，先放进消息队列，不直接执行回调。
            //DispatchMessageQueue()：在主线程 Update 里，把队列里存的消息拿出来，执行 OnMessage/ OnClose 这些回调
            webSocket.DispatchMessageQueue();
        }
    }

    // 外部调用
    public IEnumerator SpeakAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            yield break;
        }
        StopSpeak();//先调用 StopSpeak，终止上一轮还没跑完的 TTS

        //校验讯飞密钥配置是否填写完整，不满足直接退出
        if (config == null)
        {           
            yield break;
        }
        if (!config.IsValid())
        {           
            yield break;
        }

        //开启新一轮请求，保存本次的 requestId，重置全部状态标记
        IsSpeaking = true;
        currentRequestId++;
        int requestId = currentRequestId;
        audioBase64Builder = new StringBuilder();
        receivedEnd = false;
        connectFinished = false;
        connectSuccess = false;
        lastError = "";

        //调用之前写的XunfeiAuth，按讯飞算法生成带 HMAC-SHA256 签名的 wss 鉴权地址。生成失败直接退出
        string authUrl;
        try
        {
            authUrl = XunfeiAuth.CreateTTSAuthUrl("tts-api.xfyun.cn", "/v2/tts", config.ApiKey, config.ApiSecret);

        }
        catch (Exception e)
        {            
            IsSpeaking = false;
            yield break;
        }

        webSocket = new WebSocket(authUrl);
        webSocket.OnOpen += () =>
        {
            if (requestId != currentRequestId)
            {
                return;
            }
            connectSuccess = true;
            connectFinished = true;
        };

        webSocket.OnError += (errorMsg) =>
        {
            if (requestId != currentRequestId)
            {
                return;
            }
            lastError = errorMsg;           
            connectFinished = true;
            connectSuccess = false;
        };

        webSocket.OnClose += (closeCode) =>
        {
            if (requestId != currentRequestId)
            {
                return;
            }
            connectFinished = true;
        };

        webSocket.OnMessage += (bytes) =>
        {
            if (requestId != currentRequestId)
            {
                return;
            }

            ProcessTTSResponse(bytes, requestId);
        };

        //启动连接协程，等待 WebSocket 连接完成（最多 12 秒超时）。阻塞当前 SpeakAsync 协程，直到连接流程结束
        yield return StartCoroutine(ConnectCoroutine(requestId));

        if (!connectSuccess)//连接失败，清理websocket，退出
        {         
            IsSpeaking = false;
            CleanupWebSocket();
            yield break;
        }

        // WebSocket 已经成功
        //构造请求 JSON，通过 WebSocket 发送给讯飞服务器
        string requestJson = BuildRequestJson(text);
        try
        {
            awaitSendText(requestJson);
        }
        catch (Exception e)
        {
            Debug.LogError("讯飞 TTS：发送请求失败\n" +e);
            IsSpeaking = false;
            CleanupWebSocket();
            yield break;
        }

        // 等待讯飞返回 status=2,循环等待，直到 receivedEnd=true（服务器全部音频发完）或者 20 秒超时
        float timeout = 20f;
        float timer = 0f;
        while (!receivedEnd &&IsSpeaking &&requestId == currentRequestId)
        {
            timer += Time.deltaTime;
            if (timer >= timeout)
            {
                break;
            }
            yield return null;//暂停，等到下一帧继续循环
        }

        if (requestId != currentRequestId)
        {
            yield break;
        }

        // 转换音频
        //收到完整 base64 字符串，转成 PCM 原始二进制。调用 PlayPcmAudio 生成 AudioClip 并播放
        if (audioBase64Builder.Length > 0)
        {
            byte[] pcmData;
            try
            {
                //Convert.FromBase64String：把 base64 字符串还原成原始二进制字节数组，也就是 PCM 音频原始数据
                pcmData = Convert.FromBase64String(audioBase64Builder.ToString());
            }
            catch (Exception e)
            {
                Debug.LogError("讯飞 TTS：Base64 音频解析失败\n" +e);
                IsSpeaking = false;
                CleanupWebSocket();
                yield break;
            }
            PlayPcmAudio(pcmData);

            //等待音频播放全部结束，才继续往下执行
            if (playbackCoroutine != null)
            {
                yield return playbackCoroutine;
            }
        }
        else
        {
            Debug.LogWarning("讯飞 TTS：没有收到音频数据。");
        }

        //服务器的音频数据全部接收完毕，修改状态，关闭 WebSocket 清理资源
        if (requestId == currentRequestId)
        {
            IsSpeaking = false;
        }
        CleanupWebSocket();
        Debug.Log("========== 讯飞 TTS 本轮完成 ==========");
    }

    //连接协程
    //调用webSocket.Connect()发起网络连接,循环等待 connectFinished 标记，最多等待 12 秒，超时判定连接失败
    private IEnumerator ConnectCoroutine(int requestId)
    {
        if (webSocket == null)
        {
            yield break;
        }

        bool connectStarted = false;

        try
        {
            connectStarted = true;
            webSocket.Connect();
        }
        catch (Exception e)
        {
            lastError = e.Message;
            Debug.LogError("讯飞 TTS：调用 WebSocket.Connect() 失败\n" +e);
            connectFinished = true;
            connectSuccess = false;
        }

        if (!connectStarted)
        {
            yield break;
        }

        float timer = 0f;
        // 给连接最多 12 秒
        while (!connectFinished &&requestId == currentRequestId)
        {
            timer += Time.deltaTime;
            if (timer >= 12f)
            {
                lastError = "WebSocket 连接超过 12 秒没有完成。";
                Debug.LogError("讯飞 TTS：WebSocket 连接超时。");
                connectFinished = true;
                connectSuccess = false;
                break;
            }
            yield return null;
        }
    }

    /// 发送 JSON,发送文本消息到 WebSocket
    private void awaitSendText(string json)
    {
        if (webSocket == null)
        {
            throw new Exception("WebSocket 对象为空。");//throw = 抛出异常
        }
        webSocket.SendText(json);
    }

    //构造讯飞 TTS 请求.使用 JsonUtility 序列化，替代手动拼接字符串，避免转义 Bug
    private string BuildRequestJson(string text)
    {
        //要朗读的文本必须 base64 编码放到 text 字段
        //Encoding.UTF8.GetBytes(text)：把文本转成 UTF8 字节数组
        //Convert.ToBase64String：字节数组转为 base64 字符串，传给讯飞接口
        string base64Text = Convert.ToBase64String(Encoding.UTF8.GetBytes(text));

        // 这里使用最基础、兼容性最高的参数。aue = raw,vcn = xiaoyan,tte = utf8
        // 讯飞官方在线 TTS 文档要求 data.status = 2，表示一次性发送完整文本。
        XunfeiTTSRequest request = new XunfeiTTSRequest();
        request.common = new XunfeiTTSCommon
        {
            app_id = config.AppId
        };
        request.business = new XunfeiTTSBusiness
        {
            //raw = 不压缩的 PCM 原始音频
            aue = "raw",//返回音频是 PCM 原始 16bit，每个声音采样，用 16 位（2 字节）存储声音大小
            vcn = voiceName,
            speed = speed,
            pitch = pitch,//音调
            volume = volume,
            tte = "utf8"//输入文本编码 utf8
        };
        request.data = new XunfeiTTSData
        {
            status = 2,//一次性发送全部文本（不是流式分段发文字）
            text = base64Text
        };
        return JsonUtility.ToJson(request);//把 C# 对象序列化成 JSON 字符串
    }

    /// 处理讯飞返回的 JSON,WebSocket 收到消息进入这个函数
    private void ProcessTTSResponse(byte[] bytes,int requestId)
    {
        if (bytes == null || bytes.Length == 0)
        {
            return;
        }

        string json;

        try
        {
            json = Encoding.UTF8.GetString(bytes);//bytes 转 UTF8 json 字符串
        }
        catch (Exception e)
        {
            Debug.LogError("讯飞 TTS：解析 WebSocket 文本失败\n" +e);
            return;
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        Debug.Log("讯飞 TTS：收到服务器响应");

        TTSResponse response;

        try
        {
            //JsonUtility.FromJson：JSON 字符串转回 C# 对象（反序列化）
            response = JsonUtility.FromJson<TTSResponse>(json);
        }
        catch (Exception e)
        {
            Debug.LogError("讯飞 TTS：JSON 解析失败\n" +e +"\n服务器数据：" +json);
            return;
        }

        if (response == null)
        {
            Debug.LogWarning("讯飞 TTS：服务器返回数据为空。");
            return;
        }

        // code != 0 表示讯飞业务层错误（密钥错误、余额不足等）
        if (response.code != 0)
        {
            string error ="讯飞 TTS 业务错误\n" +"code = " + response.code +"\nmessage = " +response.message + "\nsid = " + response.sid;
            lastError = error;
            Debug.LogError(error);
            receivedEnd = true;//此时网络连接是通的，但是任务失败。含义：本次 TTS 任务结束（失败结束）
            return;
        }

        if (response.data != null)
        {
            if (!string.IsNullOrEmpty(response.data.audio))
            {
                audioBase64Builder.Append(response.data.audio);//把 base64 音频片段追加到 StringBuilder
            }

            Debug.Log("讯飞 TTS：收到音频片段，" +"Base64 长度 = " +(response.data.audio == null? 0: response.data.audio.Length) +"，status = " +response.data.status);
            // status=2： 所有音频已经发送完成
            if (response.data.status == 2)
            {
                receivedEnd = true;
                Debug.Log("========== 讯飞 TTS 音频全部接收完成 ==========");
            }
        }
    }

    //讯飞返回的是 16bit PCM，16000Hz，单声道 的二进制数据，Unity 不能直接播放 PCM，必须转成 AudioClip
    private void PlayPcmAudio(byte[] pcmData)
    {
        if (pcmData == null || pcmData.Length < 2)
        {
            Debug.LogWarning("讯飞 TTS：PCM 数据为空。");
            IsSpeaking = false;
            return;
        }

        //PCM16：每 2 个字节 = 1 个采样点（short，-32768 ~ +32767）
        // 确保 PCM 是偶数字节
        int usableLength =pcmData.Length -(pcmData.Length % 2);
        int sampleCount = usableLength / 2;
        float[] samples =new float[sampleCount];

        for (int i = 0; i < sampleCount; i++)
        {
            int index = i * 2;//1 个音频采样 = 2 个连续 byte，i 是采样序号，index 是字节数组下标
            //pcmData[index] 低 8 位；pcmData[index + 1] <<8 高 8 位，合并成 short
            short sample =(short)(pcmData[index] |(pcmData[index + 1] << 8));
            //把 short (-32768~32767) 映射成 Unity AudioClip 需要的浮点数[-1 ~ +1]
            samples[i] =sample / 32768f;
        }

        //"Xunfei_TTS_Audio"：名字，随便写，编辑器看的，不影响声音。sampleCount：总采样点数量（一共有多少个声音样本）
        //1：声道，1 = 单声道（讯飞 TTS 返回就是单声道）。16000：采样率 16000Hz，和讯飞输出一致。false：是否支持 3D 空间音频。false = 2D 全局声音
        AudioClip clip =AudioClip.Create("Xunfei_TTS_Audio",sampleCount,1,16000,false);

        //SetData(浮点采样数组, 起始采样位置)，从 clip 的第 0 号采样点开始写入音频数据，也就是从头填充这个 AudioClip
        clip.SetData(samples, 0);//把浮点采样写入 clip

        if (audioSource == null)
        {
            Debug.LogError("讯飞 TTS：AudioSource 为空。");
            IsSpeaking = false;
            return;
        }

        //如果上一句话还在朗读，你直接替换 clip，会出现声音错乱、残留旧音频继续播放
        audioSource.Stop();//立刻停止正在播放的旧语音

        //释放旧的AudioClip，防止内存泄漏
        if (audioSource.clip != null)
        {
            Destroy(audioSource.clip);
        }

        audioSource.clip = clip;
        audioSource.Play();
        float duration =clip.length;//音频时长
        playbackCoroutine =StartCoroutine(WaitAudioEnd(duration));
    }

    //等待音频播放时长，等多久 = 音频长度。主协程yield return playbackCoroutine就会在这里卡住，等播放结束
    private IEnumerator WaitAudioEnd(float duration)
    {
        yield return new WaitForSeconds(duration);
    }

    /// 停止当前 TTS
    public void StopSpeak()
    {
        currentRequestId++;//新版本号，让旧请求的回调全部失效
        receivedEnd = true;//终止等待音频的循环

        if (speakCoroutine != null)
        {
            StopCoroutine(speakCoroutine);
            speakCoroutine = null;
        }

        if (playbackCoroutine != null)
        {
            StopCoroutine(playbackCoroutine);
            playbackCoroutine = null;
        }

        if (audioSource != null)
        {
            audioSource.Stop();
            //停止时释放Clip，防止内存泄漏
            if (audioSource.clip != null)
            {
                Destroy(audioSource.clip);
                audioSource.clip = null;
            }
        }

        CleanupWebSocket();
        audioBase64Builder = null;
        IsSpeaking = false;
    }

    //清理 WebSocket
    private void CleanupWebSocket()
    {
        if (webSocket == null)
        {
            return;
        }

        try
        {
            webSocket.Close();
        }
        catch
        {
            // 忽略关闭过程中的异常
        }

        webSocket = null;
    }

    private void OnDestroy()
    {
        StopSpeak();
    }

    //返回的 TTSResponse（服务器返回的 json）
    [Serializable]
    private class TTSResponse
    {
        public int code;//返回码，0 = 成功，非 0 = 报错
        public string message;//错误文字描述
        public string sid;//本次请求唯一会话 id，讯飞后台查日志用
        public TTSData data;
    }

    [Serializable]
    private class TTSData
    {
        public string audio;//base64 音频片段
        public string ced;//结束标记字段
        public int status;
    }
}

//讯飞 TTS 请求数据结构
[Serializable]
public class XunfeiTTSRequest
{
    public XunfeiTTSCommon common;
    public XunfeiTTSBusiness business;
    public XunfeiTTSData data;
}

[Serializable]
public class XunfeiTTSCommon
{
    public string app_id;
}

[Serializable]
public class XunfeiTTSBusiness
{
    public string aue;
    public string vcn;
    public int speed;
    public int pitch;
    public int volume;
    public string tte;
}

[Serializable]
public class XunfeiTTSData
{
    public int status;
    public string text;
}