using System;
using System.Collections;
using System.Collections.Generic;//List 字典容器
using System.Text;//StringBuilder拼接字符串
using System.Threading.Tasks;//async / await异步，连接网络、等待延时
using UnityEngine;
using TMPro;
using NativeWebSocket;//第三方 WebSocket 库，用来建立 wss 长连接收发二进制/文本
using Meta.WitAi.Json;//json 解析，解析讯飞返回的 json 报文
using UnityEngine.Localization.Settings;//读取游戏当前语言，实现 Auto 自动切换识别中文/英文
using System.Net.Sockets;

//Unity 原生的异步，底层是每帧执行。老版本 Unity 协程语法不支持直接写 await，协程只能用yield return。所以协程里想等 Task，只能写 while 循环轮询IsCompleted
//等待一个 Task 执行完成。await 不会卡死 Unity 主线程，等待的时候游戏照常运行，Task 完成之后，代码从 await 的下一行继续跑
//status=0：第一帧音频,status = 1：中间音频帧,status = 2：结束帧（告诉服务器音频传完了）
//vad_eos：静音多少毫秒自动结束本次识别
public class XunfeiSTTBridge : MonoBehaviour
{
    [Header("Xunfei Config")]
    [SerializeField] private XunfeiConfig xunfeiConfig;//引用配置脚本，存 appId、apiKey、apiSecret 密钥

    [Header("UI")]
    [SerializeField] private TMP_InputField promptInputField;//语音识别出来的文字显示到这个输入框

    [Header("Microphone")]
    //讯飞 IAT 强制要求16000采样率
    [SerializeField] private int sampleRate = 16000;
    //AudioClip 环形录音缓冲区最大时长 60 秒，最多存 60 秒音频
    //录音不停的时候，存满 60 秒，缓冲区就绕回开头，覆盖最早的音频，不会内存爆炸
    //不是说最多只能录 60 秒语音，是内存缓冲区只保留最近 60 秒声音
    [SerializeField] private int microphoneLengthSeconds = 60;

    [Header("Recognition Language")]
    [SerializeField] private XunfeiLanguage language = XunfeiLanguage.Auto;

    [Header("VAD End Silence (ms)")]
    //VAD 静音检测，3000 毫秒（=3秒）静音，讯飞服务器自动结束这一轮识别
    [SerializeField] private int vadEos = 3000;

    [Header("Session Interval")]
    //一轮识别结束后，等待 0.1 秒再重连开启下一轮识别
    [SerializeField] private float restartDelay = 0.1f;

    [Header("WebSocket")]
    //websocket连接超时 8 秒，8 秒连不上直接判定失败
    [SerializeField] private float connectTimeout = 8f;

    //讯飞语音听写 STT 服务器地址常量
    private const string Host = "iat-api.xfyun.cn";
    private const string Path = "/v2/iat";

    //讯飞每一帧发送1280个采样，16000 个样本 = 1 秒；1280 ÷16000 = 0.08 秒 = 80毫秒
    //讯飞协议不允许一次性把一大段音频全部发过去；必须切成一小块一小块 80ms 数据包，一块一块上传给服务器。FrameSamples=1280就是每块取多少采样
    private const int FrameSamples = 1280;

    //Socket：网络套接字，可以理解为客户端和服务器之间的网络管道。WebSocket 就是基于普通 Socket，专门用于长连接的网络管道，一边持续发音频，一边持续收识别文字
    // WebSocket状态变量
    private WebSocket websocket;//websocket 实例对象
    private bool websocketOpened;//true = 已经成功连上服务器
    private bool connecting;//标记“正在连接中”，防止重复并发发起连接
    //版本号；旧的 websocket 事件回调到达时，如果版本号不匹配直接丢弃消息，解决旧 websocket 延迟回调干扰新会话cc
    private int websocketGeneration = 0;

    //麦克风相关
    private AudioClip microphoneClip;//Unity 麦克风录音环形 AudioClip 缓冲区
    private string microphoneDevice;//选中的麦克风设备名字
    private int lastSamplePosition; //上一次读取音频的采样下标；环形录音，从这个位置取下一块音频数据
    private bool micPersistentStarted;//麦克风是否已经持续启动

    //会话状态标记
    private bool shouldBeRecording;//用户希望录音开/关，只是一个愿望标记
    private bool isRecording;//实际当前硬件状态，代表麦克风真的正在采集音频
    //false = 还没发送音频包（下一包是第一帧 status=0）；true = 已经发过第一帧，后续全部是中间帧 status=1。业务逻辑标记
    private bool isSendingAudio; //控制 status=0 还是 status=1
    private bool sendingAudio;//防止上一块音频还没发送完成，又发起下一块发送，并发错乱
    private bool restartingRequest;//标记正在重连开启下一轮识别，防止协程重复执行

    //会话令牌，用于防止 async void 竞态条件
    //当你按下录音，又快速松开；旧的 await 异步任务还在后台跑，会篡改新会话数据
    //每次开启新会话_sessionToken++；await 醒来，对比 token，如果不等于当前 token，代表会话已经作废，直接 return 不再执行业务
    private int _sessionToken = 0;

    // 输出抑制标记
    // 用户在发送消息后，通知 STT 不要再把文字写回输入框,防止讯飞延迟到达的最后一帧响应覆盖已发送的消息
    private bool _suppressOutput = false;

    //识别文本缓存
    private string currentTranscript = "";//本轮临时识别结果（还没结束，会不断修改纠错）
    private string committedTranscript = "";
    //字典，key=sn 序列号（每一小段分配一个 sn 数字序列号），value = 这一段识别文字。讯飞 pgs 协议会做替换修正，用字典存储每一段，支持回退修改前面识别错的字
    //pgs="apd" append 追加：新增一小段文字，加到后面
    //pgs="rpl" replace 替换：发现前面某几段识别错了，要把前面 sn 范围的文字删掉，换成新识别结果
    //用字典resultSegments[sn]=文字存每一小段；需要替换的时候，根据 sn 编号删除旧片段，写入新文字
    private readonly Dictionary<int, string> resultSegments =new Dictionary<int, string>();

    public bool IsConnected => websocketOpened;
    public bool IsRecording => shouldBeRecording;

    public enum XunfeiLanguage
    {
        Chinese,
        English,
        Auto
    }

    private async void Start()
    {
        if (xunfeiConfig == null)
        {
            return;
        }

        if (Microphone.devices == null ||Microphone.devices.Length == 0)
        {
            return;
        }

        microphoneDevice =Microphone.devices[0];//取第一个麦克风设备

        micPersistentStarted = false;//麦克风是否已经持续启动

        //防止当前代码一次性跑满，卡住主线程
        await Task.Yield();//让出当前帧，回到 Unity 主线程，下一帧继续执行后面代码。防止异步代码霸占主线程，界面卡死  
    }

    private void Update()
    {
        //只在【不是 WebGL 平台】或者【在 Unity 编辑器里面运行】才执行里面代码
#if !UNITY_WEBGL || UNITY_EDITOR

        if (websocket != null)
        {
            try
            {
                //NativeWebSocket 库的函数：把 WebSocket 收到的消息队列拿出来，触发 OnMessage 回调
                //WebSocket 收到消息是存在消息队列里，需要手动调用这个函数把消息分发出来，才能触发 OnOpen/ OnMessage / OnClose 事件
                websocket.DispatchMessageQueue();
            }
            catch (Exception e)
            {
                Debug.LogWarning("XunfeiSTT: DispatchMessageQueue Exception: " +e.Message);
            }
        }

#endif

        //用户要录音、socket 已经连上、正在录音；调用SendMicrophoneAudio()读取麦克风音频、打包发送一小块音频给讯飞服务器
        if (isRecording &&websocketOpened &&shouldBeRecording)
        {
            SendMicrophoneAudio();
        }
    }

    //录音控制，外部调用入口
    public void SetActivation(bool activated)
    {
        if (activated)
        {
            BeginRecordingSession();//按下开始录音
        }
        else
        {
            EndRecordingSession();//松开结束录音
        }
    }

    //供外部调用，抑制输出，用户在 AskButtonManager 里发送消息后调用，阻止延迟的识别文字覆盖输入框
    public void SuppressOutput()
    {
        _suppressOutput = true;
    }

    //开始一次录音会话（按下按钮）
    //async void：UI 按钮事件只能用 async void，不能返回 Task
    private async void BeginRecordingSession()
    {
        if (shouldBeRecording)
        {
            return;//已经在录音，不要重复执行，直接 return
        }

        //Microphone.devices是电脑 / 手机所有麦克风设备数组
        if (Microphone.devices == null ||Microphone.devices.Length == 0)
        {
            return;
        }

        //用户重新开始录音时，恢复输出
        _suppressOutput = false;
        shouldBeRecording = true;

        // 递增会话令牌，并记录本次任务的令牌
        _sessionToken++;
        int currentToken = _sessionToken;

        //清空历史识别文字，清空字典，清空输入框 UI
        committedTranscript = "";
        currentTranscript = "";
        resultSegments.Clear();
        ClearInputField();

        //确定本次实际使用的识别语言
        XunfeiLanguage actualLanguage =GetActualRecognitionLanguage();

        if (actualLanguage == XunfeiLanguage.Chinese)
        {
            Debug.Log("讯飞 language：zh_cn");
            Debug.Log("讯飞 accent：mandarin");
        }
        else
        {
            Debug.Log("讯飞 language：en_us");
        }

        //麦克风只在第一次开启,后续识别继续复用
        if (!micPersistentStarted)
        {
            //Microphone.Start(设备名,true循环环形缓冲,最大秒数,采样率)；第二个参数 true 开启环形缓冲区，写满绕回开头
            microphoneClip = Microphone.Start(microphoneDevice,true,microphoneLengthSeconds,sampleRate);
            //拿到麦克风当前采样下标，赋值给 lastSamplePosition，作为读取音频的起始位置
            lastSamplePosition = Microphone.GetPosition(microphoneDevice);
            //麦克风启动标记置 true；后面再次录音，麦克风不用重复 Start，复用已经开启的麦克风，避免杂音
            micPersistentStarted = true;
        }

        //连接讯飞
        if (!websocketOpened)
        {
            //socket 没连上，调用连接讯飞；await 在这里暂停函数，等待 WebSocket 网络连接完成，游戏不卡死
            bool connected =await ConnectToXunfei();

            // await 醒来后，先检查会话令牌是否已改变
            if (currentToken != _sessionToken || !shouldBeRecording)
            {               
                return;
            }

            if (!connected)
            {              
                shouldBeRecording = false;//连接失败，关闭录音开关，return 退出
                return;
            }
        }

        if (!shouldBeRecording)
        {
            return;//二次防护，防止 await 回来用户已经松手
        }

        StartNewIatRequest();//一切正常，发起一次讯飞语音识别 IAT 请求
    }

    //获取当前实际识别语言
    private XunfeiLanguage GetActualRecognitionLanguage()
    {
        //如果Inspector手动指定中文
        if (language == XunfeiLanguage.Chinese)
        {
            return XunfeiLanguage.Chinese;
        }

        //如果Inspector手动指定英文
        if (language == XunfeiLanguage.English)
        {
            return XunfeiLanguage.English;
        }

        //Auto模式,跟随Unity Localization当前语言
        try
        {
            if (LocalizationSettings.SelectedLocale != null)
            {
                string code =LocalizationSettings.SelectedLocale.Identifier.Code;

                if (!string.IsNullOrWhiteSpace(code))
                {
                    code =code.ToLower();

                    if (code.StartsWith("zh"))
                    {
                        return XunfeiLanguage.Chinese;
                    }

                    if (code.StartsWith("en"))
                    {
                        return XunfeiLanguage.English;
                    }
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("XunfeiSTT: 获取Unity当前语言失败：" +e.Message);
        }

        //如果Localization还没有初始化,默认使用中文
        return XunfeiLanguage.Chinese;
    }

    //连接讯飞
    //async Task<bool>异步函数，返回 bool：true 连接成功；false 失败。
    //负责生成鉴权 url，创建 WebSocket，和讯飞服务器握手
    private async Task<bool> ConnectToXunfei()
    {
        if (connecting)
        {
            float waitTime = 0f;
            //不断等待，每 50ms 检查一次；要么连上了（connecting=false 跳出循环），要么等待总时间超过超时时间，判定连接失败
            while (connecting &&waitTime < connectTimeout)
            {
                await Task.Delay(50);
                waitTime += 0.05f;
            }
            return websocketOpened;
        }

        if (websocketOpened)
        {
            return true;
        }

        connecting = true;
        //如果不清空全局 websocket，新旧 socket 会混在一起，旧 socket 的回调还会继续触发，会出现多个 WebSocket 同时收发消息、乱回调、重复识别、内存泄漏
        WebSocket oldSocket =websocket;//保存旧socket引用
        websocket = null;//清空全局 socket 变量
        websocketOpened = false;

        if (oldSocket != null)
        {
            try
            {
                //如果存在旧连接，await 等待旧 websocket 关闭，吃掉异常
                await oldSocket.Close();
            }
            catch
            {
            }
        }

        //连接版本号 + 1，本次 socket 的版本，回调的时候判断 socket 是否已经废弃
        int currentGeneration =++websocketGeneration;

        string url;
        try
        {
            url =XunfeiAuth.CreateAuthUrl(Host,Path,xunfeiConfig.ApiKey,xunfeiConfig.ApiSecret);
        }
        catch (Exception e)
        {
            connecting = false;
            return false;
        }

        //new 出来 WebSocket 对象，填入鉴权 url；创建失败返回 false
        WebSocket newSocket;
        try
        {
            newSocket =new WebSocket(url);
        }
        catch (Exception e)
        {
            connecting = false;
            return false;
        }

        websocket =newSocket;
        //TaskCompletionSource<bool>手动控制 Task 完成；WebSocket 的 Open/Error/Close 是事件回调，不是 await 直接等待，用这个工具包装成可以 await 的任务
        TaskCompletionSource<bool> connectionSource =new TaskCompletionSource<bool>();

        //OnOpen事件：WebSocket 握手成功自动触发
        //() => { ... }是 Lambda 匿名函数，直接写事件里面，不用单独写一个函数
        //+= 订阅事件：告诉 newSocket，当 OnOpen 触发，执行大括号里面代码
        newSocket.OnOpen += () =>
        {
            if (currentGeneration !=websocketGeneration)
            {
                return;
            }

            websocketOpened = true;//WebSocket 握手通道已经打开
            connecting = false;

            if (!connectionSource.Task.IsCompleted)//这个 TCS 任务还没有被标记完成
            {
                //connectionSource.TrySetResult(true) → 标记任务成功，await connectionSource.Task就会返回 true
                //connectionSource.TrySetResult(false) → 标记任务失败，await 拿到 false
                connectionSource.TrySetResult(true);
            }
        };

        //网络错误回调，标记任务失败
        newSocket.OnError +=(error) =>
            {
                if (currentGeneration !=websocketGeneration)
                {
                    return;
                }

                if (!connectionSource.Task.IsCompleted)
                {
                    connectionSource.TrySetResult(false);
                }
            };

        //连接关闭事件，标记任务失败
        newSocket.OnClose +=(closeCode) =>
            {
                if (currentGeneration !=websocketGeneration)
                {
                    return;
                }

                websocketOpened = false;
                connecting = false;

                if (!connectionSource.Task.IsCompleted)
                {
                    connectionSource.TrySetResult(false);
                }
            };

        newSocket.OnMessage +=(bytes) =>
            {
                if (currentGeneration !=websocketGeneration)
                {
                    return;
                }
                string message;
                try
                {
                    //收到讯飞服务器二进制消息回调，bytes 字节数组转 UTF‑8 字符串 json
                    message = Encoding.UTF8.GetString(bytes);
                }
                catch (Exception e)
                {
                    return;
                }

                //调用ProcessXunfeiResponse()解析识别结果
                ProcessXunfeiResponse(message);
            };

        try
        {
            Task connectTask =newSocket.Connect();//TCP 底层连接任务，newSocket.Connect()发起网络连接
            //创建延时任务：等待connectTimeout秒，任务自动完成。单纯用来做超时计时器，不做连接
            Task timeoutTask =Task.Delay(TimeSpan.FromSeconds(connectTimeout));//Task.Delay(8秒)超时任务
            //等待三个任务中任意一个先完成就往下执行：要么连上、要么超时、要么事件回调触发完成。剩下任务继续后台跑，不会取消
            //TCS，等待 WebSocket 握手 OnOpen 回调
            Task completedTask =await Task.WhenAny(connectTask,timeoutTask,connectionSource.Task);

            //TCP 直接报错断开 → connectTask 先完成 → 捕获连接异常
            if (completedTask ==connectTask)
            {
                try
                {
                    await connectTask;//等待 TCP 连接完成，捕获网络异常
                }
                catch (Exception e)
                {                   
                    if (currentGeneration ==websocketGeneration)
                    {
                        websocketOpened = false;
                        connecting = false;
                    }

                    return false;
                }

                //TCP 只是打通底层网络管道，不等于 WebSocket 握手成功。TCP 通了之后，还要等 WebSocket 的 OnOpen 握手事件
                float openWait = 0f;
                while (!websocketOpened &&openWait < 1f)
                {
                    await Task.Delay(20);
                    openWait += 0.02f;
                }
            }
            else if (completedTask ==connectionSource.Task)
            {
                //await 拿到 TCS 手动设置的结果 true/false，代表握手成功与否
                bool result =await connectionSource.Task;

                if (!result)
                {
                    return false;
                }
            }
            else//超时
            {               
                if (currentGeneration ==websocketGeneration)
                {
                    websocketOpened = false;
                    connecting = false;
                }

                try
                {
                    await newSocket.Close();
                }
                catch
                {
                }
                return false;
            }

            //握手成功，并且是当前最新这一轮连接，返回 true 代表连接成功
            if (websocketOpened &&currentGeneration ==websocketGeneration)
            {
                connecting = false;
                return true;
            }
            return false;
        }
        catch (Exception e)//本次连接失败
        {
            connecting = false;
            websocketOpened = false;
            return false;
        }
    }

    //开启一轮讯飞 IAT 识别会话；WebSocket 连接可以复用，但是每一轮语音识别要发起新请求
    private void StartNewIatRequest()
    {
        if (!shouldBeRecording)
        {
            return;
        }

        //socket 断开，启动协程自动重连
        if (websocket == null ||!websocketOpened)
        {
            StartCoroutine(ReconnectAndStartRequest());
            return;
        }

        resultSegments.Clear();
        currentTranscript = "";
        //这两个标记：是否正在往 WebSocket 发送音频数据包。先置为 false，代表暂时不发送音频
        isSendingAudio = false;
        sendingAudio = false;
        //打开麦克风采集
        //麦克风开始录取声音，采集音频采样数据放到内存缓冲区；
        //但是暂时不把缓冲区音频发给讯飞服务器（isSendingAudio = false），先采集音频，等准备就绪之后，再开启发送音频流
        isRecording = true;
    }

    //协程重连；协程用 yield，不能真正多线程，全部在主线程，适合 Unity 动画、UI、等待，协程内部报错很难捕获
    //await Task.Delay，可以跑在子线程（也可以在主线程），可以用 try-catch 捕获异常，适合网络请求、WebSocket 长连接
    private IEnumerator ReconnectAndStartRequest()
    {
        if (restartingRequest)
        {
            yield break;//正在重连，直接退出协程，避免并发
        }

        //等待 0.3 秒，不卡游戏
        restartingRequest = true;
        yield return new WaitForSecondsRealtime(restartDelay);

        if (!shouldBeRecording)//用户已经停止录音就退出，否则执行连接协程
        {
            restartingRequest = false;
            yield break;
        }

        restartingRequest = false;
        yield return ConnectAndStartRequestCoroutine();
    }

    private IEnumerator ConnectAndStartRequestCoroutine()
    {
        Task<bool> connectTask =ConnectToXunfei();
        //作用：协程里面等待异步 Task 执行结束
        //为什么要等一帧：协程不能直接 await Task，只能每帧轮询看 Task 有没有做完。不能卡死游戏，每一帧检查一次
        while (!connectTask.IsCompleted)//connectTask 这个异步任务还没有执行完毕
        {
            yield return null;//协程暂停，等待下一帧回来继续循环判断
        }

        bool connected = false;//用来保存连接结果
        Exception taskEx = null;//初始为空，用来保存 Task 内部抛出的错误
        //不要在 Update 主线程随便用.Result，容易卡死游戏。一般是协程里面用，或者后台线程
        try//执行connectTask.Result，.Result：同步阻塞，等待异步连接任务结束，拿到 bool 结果存入 connected
        {
            //如果 Task 内部抛出异常，.Result会把异常包装抛出，进入 catch
            connected = connectTask.Result;
        }
        catch (Exception e)
        {
            taskEx = e;//把异常保存下来，后面可以判断是否报错
        }

        //重连成功，开启新一轮识别
        if (taskEx != null)
        {
            Debug.LogError("XunfeiSTT: Reconnect failed:\n" +taskEx);
        }
        if (!shouldBeRecording)
        {
            yield break;
        }
        if (!connected)
        {
            yield break;
        }
        StartNewIatRequest();
    }

    //结束录音
    private async void EndRecordingSession()
    {
        //关闭总开关，会话令牌 + 1，让所有还在跑的旧 async 任务全部作废
        if (!shouldBeRecording)
        {
            return;
        }   
        shouldBeRecording = false;
        isRecording = false;
        restartingRequest = false;        
        _sessionToken++;

        // WebSocket 握手通道打开成功，websocket != null：WebSocket 对象不为空，isSendingAudio：正在向讯飞服务器推送音频流
        //发送讯飞结束帧status=2，告诉服务器：语音数据发送完毕，请返回最终识别结果
        if (websocketOpened &&websocket != null &&isSendingAudio)
        {
            await SendEndFrame();            
        }

        //等待 150 毫秒，给服务器一点时间把最后识别结果推送回来
        isSendingAudio = false;//标记不再持续发送音频流
        sendingAudio = false;//发送锁，标记当前没有正在发送音频数据包（防止并发发送）
        //你停止录音后，讯飞服务器还有一小段缓存音频，需要一点时间把最后文字返回客户端，不能立刻关连接，否则丢最后识别结果
        await Task.Delay(150);//等待 150ms，不阻塞游戏

        //关闭麦克风
        if (!string.IsNullOrEmpty(microphoneDevice) &&micPersistentStarted)
        {
            try
            {
                //调用 UnityAPI 关闭麦克风采集；加 try-catch，防止麦克风异常报错崩游戏，出错直接忽略
                Microphone.End(microphoneDevice);
            }
            catch
            {
            }

            microphoneClip = null;//把录音音频资源置空，释放内存
            micPersistentStarted = false;//标记麦克风硬件已关闭
        }

        //关闭WebSocket
        if (websocket != null)
        {
            WebSocket oldSocket =websocket;//把当前 socket 保存临时变量 oldSocket
            websocket = null;//全局变量清空，防止其他地方继续用旧 socket
            websocketOpened = false;//标记通道关闭
            try
            {
                await oldSocket.Close();//异步关闭旧的 WebSocket 连接
            }
            catch
            {
            }

            websocketGeneration++;
        }

        currentTranscript = "";
        resultSegments.Clear();
        UpdateInputField();
    }

    //发送麦克风音频
    //`private async void`：私有异步函数，void 代表没有返回值。async void 一般用于回调 / 轮询，不用 await 接收返回
    private async void SendMicrophoneAudio()
    {
        //麦克风没有在采集，用户不希望录音，WebSocket 通道没打开，socket 对象空，录音 Clip 为空，正在发送音频（锁，防止同时发多包）
        if (!isRecording ||!shouldBeRecording ||!websocketOpened ||websocket == null ||microphoneClip == null ||sendingAudio)
        {
            return;
        }

        // 记录当前会话令牌
        int currentToken = _sessionToken;

        int currentPosition;//定义变量：麦克风当前采样位置（读到音频到第几个采样点）
        try
        {
            currentPosition = Microphone.GetPosition(microphoneDevice);//UnityAPI，拿到麦克风当前采样索引
        }
        catch
        {
            return;
        }

        if (currentPosition < 0)
        {
            return;//麦克风位置小于 0，异常，退出
        }

        int availableSamples;//本次新增的音频采样点数量
        if (currentPosition >=lastSamplePosition)
        {
            //没有绕回环形缓存，新采样数量 = 当前位置 - 上次位置
            availableSamples = currentPosition -lastSamplePosition;
        }
        else
        {
            //环形缓存写满，从头覆盖，可用采样 = 末尾剩余 + 开头新采样
            availableSamples = microphoneClip.samples -lastSamplePosition +currentPosition;
        }

        if (availableSamples <FrameSamples)
        {
            return;//如果新增采样数量，不足一帧需要的采样点数，不发送，等攒够一帧。FrameSamples是每一帧要发给讯飞的采样数量
        }

        float[] samples =new float[FrameSamples];//创建数组，存放读取到的音频浮点数
        try
        {
            //从 AudioClip 读取音频数据到 samples 数组，起始位置 lastSamplePosition
            microphoneClip.GetData(samples,lastSamplePosition);
        }
        catch (Exception e)
        {
            Debug.LogWarning("XunfeiSTT: Read mic data failed: " +e.Message);
            return;
        }

        //更新上次读取位置，取模 %，环形缓存循环。超过总采样数自动回到 0
        lastSamplePosition = (lastSamplePosition +FrameSamples) %microphoneClip.samples;

        byte[] pcm =ConvertSamplesToPCM16(samples);//把 Unity [-1~1] 浮点数音频，转为讯飞要求的 PCM16 字节数组

        //PCM 二进制字节数组转 Base64 字符串，JSON 只能传文本，不能直接传二进制
        string audioBase64 =Convert.ToBase64String(pcm);

        //讯飞 status 帧标记：status = 0：第一帧音频，要携带完整 common、business 参数，status = 1：中间音频帧，持续流
        //三元运算符：isSendingAudio 为 true→status = 1；false→status = 0，如果 status = 0（第一帧），标记 isSendingAudio = true
        int status =isSendingAudio ? 1 : 0;
        if (status == 0)
        {
            isSendingAudio = true;           
        }

        //调用 CreateAudioFrame，组装完整的讯飞请求 json 字符串
        string json = CreateAudioFrame(status,audioBase64);

        try
        {
            sendingAudio = true;//加发送锁：正在发送数据包，防止重复调用
            WebSocket socket =websocket;//临时保存 socket 引用（防止中途 socket 被置空）
            if (socket == null ||!websocketOpened)
            {
                return;
            }
            await socket.SendText(json);//通过 WebSocket 异步发送 json 文本给讯飞服务器

            // await 醒来后，检查会话令牌是否已改变
            if (currentToken != _sessionToken)
            {
                return;
            }
        }
        catch (Exception e)
        {
            Debug.LogError("XunfeiSTT: Send audio failed: " +e);
        }
        finally
        {
            //无论令牌是否改变，只要发送结束，必须释放锁，否则会导致新会话被旧的 sendingAudio=true 阻塞
            sendingAudio = false;
        }
    }

    //创建讯飞音频帧，使用 JsonUtility 序列化，替代手动拼接字符串
    private string CreateAudioFrame(int status,string audio)
    {
        XunfeiLanguage actualLanguage =GetActualRecognitionLanguage();

        //实例化请求对象，填充 common：讯飞 appid
        XunfeiSTTRequest request = new XunfeiSTTRequest();
        request.common = new XunfeiSTTCommon
        {
            app_id = xunfeiConfig.AppId
        };

        //business 业务参数：language 语言；domain 固定 iat 听写；vad_eos 静音断句阈值（静音多久判定一句话结束）
        request.business = new XunfeiSTTBusiness
        {
            language = actualLanguage == XunfeiLanguage.Chinese ? "zh_cn" : "en_us",
            domain = "iat",//讯飞语音听写服务固定参数：iat= Interactive Audio Transcription，实时语音听写
            vad_eos = vadEos
        };

        //中文设置口音 mandarin 普通话；英文清空 accent
        if (actualLanguage == XunfeiLanguage.Chinese)
        {
            request.business.accent = "mandarin";
        }
        else
        {
            request.business.accent = "";
        }

        //data 音频数据：status 帧标记；音频格式 16k 采样 PCM；audio 是 base64 音频字符串
        request.data = new XunfeiSTTData
        {
            status = status,
            format = "audio/L16;rate=16000",
            encoding = "raw",//原始 pcm 编码，没有压缩
            audio = audio
        };

        //JsonUtility.ToJson：把 C# 对象序列成 JSON 字符串，返回。替代手写拼接字符串，不容易 JSON 语法错误
        return JsonUtility.ToJson(request);
    }

    //发送最后一帧，使用 JsonUtility 序列化，替代手动拼接字符串
    private async Task SendEndFrame()
    {
        if (!websocketOpened ||websocket == null)
        {
            return;//通道没打开 /socket 空，直接 return
        }

        XunfeiSTTRequest request = new XunfeiSTTRequest();
        request.common = new XunfeiSTTCommon
        {
            app_id = xunfeiConfig.AppId
        };
        //status=2 代表音频全部发送完成，空 audio，告诉讯飞：不再发音频，请返回最终识别结果
        request.data = new XunfeiSTTData
        {
            status = 2,
            format = "audio/L16;rate=16000",
            encoding = "raw",
            audio = ""
        };

        //序列化 JSON，发送结束帧；失败打印错误
        string json = JsonUtility.ToJson(request);
        try
        {
            WebSocket socket =websocket;
            if (socket == null ||!websocketOpened)
            {
                return;
            }
            await socket.SendText(json);
        }
        catch (Exception e)
        {
            Debug.LogError("XunfeiSTT: Send end frame failed: " +e);
        }
    }

    //处理讯飞服务器返回的 JSON
    private void ProcessXunfeiResponse(string json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return;
        }

        try
        {
            //解析 json 字符串，WitResponseNode 是 Json 解析库节点对象，用来读取 json 字段
            WitResponseNode response =WitResponseNode.Parse(json);

            //读取返回码 code。code=0 代表成功；非 0 是错误，打印错误码和消息，return
            int code =response["code"].AsInt;
            if (code != 0)
            {               
                return;
            }

            WitResponseNode data =response["data"];
            if (data == null)
            {
                return;//读取 data 节点；data 为空直接 return
            }

            //读取 data 里的 status；result 不为空，调用 ProcessWpgsResult 处理 WPGS 增量文字结果
            int status =data["status"].AsInt;
            WitResponseNode result =data["result"];
            if (result != null)
            {
                ProcessWpgsResult(result);
            }

            //BuildCurrentTranscript 拼接当前实时识别全部文字，赋值给 currentTranscript，更新 UI 输入框
            string display =BuildCurrentTranscript();
            if (!string.IsNullOrWhiteSpace(display))
            {
                currentTranscript =display;
                UpdateInputField();
            }

            //当前IAT请求结束
            //status=2：讯飞返回本次语音识别会话结束。调用 CommitCurrentRequest，把本次识别文字追加保存到总记录 committedTranscript
            if (status == 2)
            {               
                CommitCurrentRequest();

                //重置录音、发送标记
                isRecording = false;
                isSendingAudio = false;
                sendingAudio = false;

                //shouldBeRecording=true：用户还按着录音按钮，启动协程 RestartNextRequest，重新创建 WebSocket 开启新一轮识别（长按连续听写）
                if (shouldBeRecording)
                {
                    if (!restartingRequest)
                    {
                        StartCoroutine(RestartNextRequest());
                    }
                }
                else
                {
                    //松手以后,这里不要立刻清掉已经提交的文字,只清当前临时识别缓存
                    currentTranscript = "";
                    resultSegments.Clear();
                    UpdateInputField();
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError("XunfeiSTT: Parse response failed:\n" +e +"\nRaw Json:" +json);
        }
    }

    //WPGS处理，增量文字处理（讯飞核心协议）
    private void ProcessWpgsResult(WitResponseNode result)
    {
        //pgs：apd / rpl。apd = append 追加：新增一段文字，rpl = replace 替换：前面某些识别片段要删掉，用新文字替换（修正识别错误）
        string pgs = result["pgs"];
        int sn = result["sn"].AsInt;//sn：序号，每一段识别片段编号
        //ws是讯飞返回的词数组，里面是本次识别出来的词语列表，传给 ParseWsText 拼接文字
        string text = ParseWsText( result["ws"]);
        //pgs 为空，直接保存这个序号对应的文本
        if (string.IsNullOrEmpty(pgs))
        {
            resultSegments[sn] =text;
            return;
        }

        //追加
        if (pgs == "apd")
        {
            resultSegments[sn] =text;
            return;
        }

        //替换
        //rpl 替换模式：rg 里面两个数字 start、end，代表sn 序号从 start 到 end 之间所有旧识别片段全部删掉
        if (pgs == "rpl")
        {
            WitResponseNode rg =result["rg"];

            if (rg != null &&rg.Count >= 2)
            {
                int start = rg[0].AsInt;
                int end = rg[1].AsInt;
               
                List<int> removeKeys = new List<int>();

                foreach (int key in resultSegments.Keys)
                {
                    if (key >= start &&key <= end)
                    {
                        removeKeys.Add(key);
                    }
                }

                foreach (int key in removeKeys)
                {
                    resultSegments.Remove(key);
                }
            }
            resultSegments[sn] =text;
        }
    }

    //给旧 WebSocket 完全关闭的缓冲时间，防止短时间疯狂重复建立连接，触发讯飞接口限流，等待期间代码暂停，等待结束才继续执行重连
    //解析讯飞ws节点，拼接单句文字，只解析服务器单次返回的一小段识别结果
    private string ParseWsText(WitResponseNode ws)
    {
        if (ws == null)
        {
            return "";
        }
        //StringBuilder 高效拼接字符串，避免大量字符串产生内存垃圾
        StringBuilder builder = new StringBuilder();

        for (int i = 0;i < ws.Count;i++)
        {
            WitResponseNode item =ws[i];
            WitResponseNode cw =item["cw"];//cw= candidate word 候选词数组

            if (cw == null ||cw.Count == 0)
            {
                continue;
            }

            //cw[0]：讯飞认为概率最高的候选词，"w"字段：就是识别出来的文字
            string word = cw[0]["w"];

            if (!string.IsNullOrEmpty(word))
            {
                builder.Append(word);
            }
        }
        return builder.ToString();//返回拼接好的完整句子
    }

    //拼接所有保存的识别片段，得到完整实时文本
    private string BuildCurrentTranscript()
    {
        StringBuilder builder =new StringBuilder();
        //取出 resultSegments 字典所有 sn 编号
        List<int> keys =new List<int>(resultSegments.Keys);
        keys.Sort();//排序，保证文字顺序按说话先后拼接

        //遍历排序后的编号，把文字依次拼接，返回完整实时识别文本
        foreach (int key in keys)
        {
            string value =resultSegments[key];
            if (!string.IsNullOrEmpty(value))
            {
                builder.Append(value);
            }
        }
        return builder.ToString();
    }

    //提交当前一句，提交本次会话文字，追加到总文本
    private void CommitCurrentRequest()
    {
        string text =BuildCurrentTranscript();
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }
        text =text.Trim();

        //committedTranscript：所有识别完成文字总缓存
        //如果总缓存是空，直接赋值；否则拼接空格 + 本次新文字，追加到总记录
        if (string.IsNullOrWhiteSpace(committedTranscript))
        {
            committedTranscript =text;
        }
        else
        {
            //英文单词之间需要空格,中文这里多一个空格问题不大
            committedTranscript +=" " +text;
        }

        //清空临时缓存，更新 UI 输入框
        currentTranscript = "";
        resultSegments.Clear();
        UpdateInputField();
    }

    //长按期间自动开启下一次IAT，长按录音自动重连协程
    private IEnumerator RestartNextRequest()
    {
        if (restartingRequest)
        {
            yield break;
        }

        restartingRequest = true;
        //等待 restartDelay 秒（真实时间，不受 Time.timeScale 影响）
        yield return new WaitForSecondsRealtime(restartDelay);

        //等待期间用户松开按钮，shouldBeRecording=false，停止重连
        if (!shouldBeRecording)
        {
            restartingRequest = false;
            yield break;
        }

        //保存旧 socket，全局置空；异步关闭旧连接，协程每帧循环等待关闭任务完成
        WebSocket oldSocket =websocket;
        websocket = null;
        websocketOpened = false;
        if (oldSocket != null)
        {
            var closeTask =oldSocket.Close();
            while (!closeTask.IsCompleted)
            {
                yield return null;
            }
        }

        //调用 ConnectToXunfei 异步连接函数，拿到 connectTask。协程每帧轮询等待连接完成；中途松手直接退出重连
        Task<bool> connectTask =ConnectToXunfei();
        while (!connectTask.IsCompleted)
        {
            if (!shouldBeRecording)
            {
                restartingRequest = false;
                yield break;
            }
            yield return null;
        }

        //阻塞拿到连接结果，捕获异常，和前面解释过的`.Result`逻辑完全一致
        bool connected = false;
        Exception ex = null;
        try
        {
            connected =connectTask.Result;
        }
        catch (Exception e)
        {
            ex = e;
        }

        if (ex != null)
        {
            Debug.LogError("XunfeiSTT: Next WebSocket connect Exception:\n" +ex);
        }

        if (!shouldBeRecording)
        {
            restartingRequest = false;
            yield break;
        }

        if (!connected)
        {
            restartingRequest = false;
            yield break;
        }

        //连接成功，关闭重连标记，启动新一轮 IAT 语音识别请求
        restartingRequest = false;
        StartNewIatRequest();
    }

    //更新输入框
    private void UpdateInputField()
    {
        if (promptInputField == null)
        {
            return;
        }
        //如果被抑制，就不写入输入框，防止讯飞延迟到达的最后一帧响应覆盖已发送的消息
        if (_suppressOutput)
        {
            return;
        }

        //最终 UI 文字 = 已经提交的总文字 + 当前实时临时识别文字
        string text =committedTranscript;
        if (!string.IsNullOrWhiteSpace(currentTranscript))
        {
            if (!string.IsNullOrWhiteSpace(text))
            {
                text += " ";
            }
            text +=currentTranscript;
        }

        promptInputField.SetTextWithoutNotify(text);//设置输入框文字，不触发输入框的 OnValueChanged 回调，避免循环触发事件
        promptInputField.caretPosition =text.Length;//光标放到文字末尾
        promptInputField.ForceLabelUpdate();//强制 TMP 更新渲染文本
    }

    //清空全部识别文字
    public void ResetTranscription()
    {
        // 清空识别时恢复输出
        _suppressOutput = false;
        committedTranscript = "";//总文字
        currentTranscript = "";
        resultSegments.Clear();
        ClearInputField();
    }

    //清空输入框
    private void ClearInputField()
    {
        if (promptInputField == null)
        {
            return;
        }
        promptInputField.SetTextWithoutNotify("");
        promptInputField.caretPosition = 0;//光标移动到 0 位置
        promptInputField.ForceLabelUpdate();
    }

    //float 音频转 PCM16 字节数组
    private byte[] ConvertSamplesToPCM16(float[] samples)
    {
        //PCM16：每个采样 2 字节，数组长度 = 采样数量 * 2
        byte[] pcm =new byte[samples.Length * 2];
        for (int i = 0;i < samples.Length;i++)
        {
            //Mathf.Clamp：把音频浮点数限制在 [-1,1]，防止爆音溢出
            float sample =Mathf.Clamp(samples[i],-1f,1f);
            //sample *32767f：[-1~1] 映射到 short 范围 -32767 ~ +32767
            short value =(short)(sample *32767f);
            //short 是 2 字节，拆分高低位字节存入 pcm 数组。小端序，讯飞 IAT 需要小端 PCM16
            pcm[i * 2] =(byte)(value &0xff);
            pcm[i * 2 + 1] =(byte)((value >> 8) &0xff);
        }
        return pcm;
    }

    //销毁
    private void OnDestroy()
    {
        shouldBeRecording = false;
        isRecording = false;
        restartingRequest = false;
        // 对象销毁时递增令牌，确保所有异步任务失效
        _sessionToken++;
        websocketGeneration++;

        //销毁时关闭麦克风，try-catch 防止报错
        if (!string.IsNullOrEmpty(microphoneDevice) &&micPersistentStarted)
        {
            try
            {
                Microphone.End(microphoneDevice);
            }
            catch
            {
            }
        }

        var socket =websocket;
        websocket = null;
        websocketOpened = false;
        if (socket != null)
        {
            _ = socket.Close();
        }
    }

    //程序切后台
    private void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus)//pauseStatus=true = 应用进入后台
        {
            EndRecordingSession(); //切后台直接结束录音会话，关闭websocket、释放麦克风
        }
    }
}

//讯飞 STT 请求数据结构
[Serializable]
public class XunfeiSTTRequest
{
    public XunfeiSTTCommon common;//公共信息
    public XunfeiSTTBusiness business;//业务参数
    public XunfeiSTTData data;//音频数据
}

[Serializable]
public class XunfeiSTTCommon
{
    public string app_id;
}

[Serializable]
public class XunfeiSTTBusiness
{
    public string language;
    public string domain;
    public string accent;//口音
    public int vad_eos;
}

[Serializable]
public class XunfeiSTTData
{
    public int status;
    public string format;//音频格式
    public string encoding;//编码
    public string audio;//base64 音频字符串
}