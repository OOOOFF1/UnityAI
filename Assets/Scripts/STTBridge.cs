using Meta.WitAi.Json;
using Meta.WitAi;
using Oculus.Voice;
using System.Collections;
using UnityEngine;
using TMPro;

public class STTBridge : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private TMP_InputField promptInputField;

    [Header("Recording")]
    [SerializeField] private AskButtonManager askButtonManager;

    [Header("Voice")]
    [SerializeField] private AppVoiceExperience appVoiceExperience;//Wit SDK核心组件，管理麦克风

    [Header("Settings")]
    [SerializeField] private float restartDelay = 0.05f;//两轮识别之间短暂间隔

    public bool IsActive => _active;

    private bool _active = false;//Wit是否激活

    private bool _shouldBeRecording = false;//用户是否还按住按钮（是否需要保存识别文字）

    private bool _isListening = false;// Wit 当前是否正在监听

    //_isFinishingRequest = true：上一轮语音还在上传/云端处理中，还没拿到最终结果，不能立刻开启下一轮录音，否则会冲突报错、文字错乱
    //_isFinishingRequest = false：上一轮全部处理完毕，可以开启下一轮收音
    private bool _isFinishingRequest = false;//上一轮语音请求是否还在收尾

    private string _committedTranscript = "";//已经确认保存的识别文字（多轮拼接历史）

    private string _currentRequestTranscript = "";//当前这一轮识别出来的文字

    private string currentTranscript = "";//最终合并完整文字，用于赋值输入框

    private bool _restartScheduled = false;//是否已经安排重启下一轮监听

    private float _recordingStartTime = 0f;//按下按钮的时间戳（用来打印延迟调试）

    private float _listeningStartTime = 0f;//Wit真正开始收音的时间戳

    private bool _hasReceivedFirstTranscript = false;//标记是否收到本轮第一条识别文字（调试首字延迟）

    //新增：标记正式录音Session是否因为长时间停顿而结束
    //注意：Session和Wit Request不是同一个概念
    //Wit Request可以因为Inactivity结束，但只要用户还按着按钮，Session逻辑仍然由这里控制
    private bool _sessionFinishedByInactivity = false;

    //新增：标记当前是否正在等待Wit Request最终Response
    //防止同一个Request重复提交文字
    private bool _waitingForResponse = false;

    private void OnEnable()
    {
        //绑定Wit所有回调事件：收音开始、片段文字、完整文字、响应返回、报错等
        appVoiceExperience.events.OnRequestCreated.AddListener(OnRequestStarted);
        appVoiceExperience.events.OnPartialTranscription.AddListener(OnPartialTranscription);
        appVoiceExperience.events.OnFullTranscription.AddListener(OnFullTranscription);
        appVoiceExperience.events.OnStartListening.AddListener(OnListenStart);
        appVoiceExperience.events.OnStoppedListening.AddListener(OnListenStop);
        appVoiceExperience.events.OnStoppedListeningDueToDeactivation.AddListener(OnListenStoppedByDeactivation);
        appVoiceExperience.events.OnStoppedListeningDueToInactivity.AddListener(OnListenStoppedByInactivity);
        appVoiceExperience.events.OnResponse.AddListener(OnRequestResponse);
        appVoiceExperience.events.OnError.AddListener(OnRequestError);

        StartCoroutine(WarmUpVoice());//启动预热协程，后台待命麦克风
    }

    private void OnDisable()
    {
        StopAllCoroutines();//停止全部正在跑的协程，防止协程后台继续跑逻辑报错

        appVoiceExperience.Deactivate();//告诉Wit关闭麦克风，停止语音

        appVoiceExperience.events.OnRequestCreated.RemoveListener(OnRequestStarted);
        appVoiceExperience.events.OnPartialTranscription.RemoveListener(OnPartialTranscription);
        appVoiceExperience.events.OnFullTranscription.RemoveListener(OnFullTranscription);
        appVoiceExperience.events.OnStartListening.RemoveListener(OnListenStart);
        appVoiceExperience.events.OnStoppedListening.RemoveListener(OnListenStop);
        appVoiceExperience.events.OnStoppedListeningDueToDeactivation.RemoveListener(OnListenStoppedByDeactivation);
        appVoiceExperience.events.OnStoppedListeningDueToInactivity.RemoveListener(OnListenStoppedByInactivity);
        appVoiceExperience.events.OnResponse.RemoveListener(OnRequestResponse);
        appVoiceExperience.events.OnError.RemoveListener(OnRequestError);
    }

    private IEnumerator WarmUpVoice()
    {
        Debug.Log("========== 开始预热 Wit 语音系统 ==========");

        yield return null;//等待一帧,给Unity一帧时间初始化SDK，避免过早调用报错

        _shouldBeRecording = false;
        _active = false;
        _isListening = false;
        _isFinishingRequest = false;
        _restartScheduled = false;
        _sessionFinishedByInactivity = false;
        _waitingForResponse = false;

        appVoiceExperience.Activate();//启动Wit麦克风后台监听

        Debug.Log("========== Wit 预热完成，后台保持监听 ==========");
    }

    private void OnRequestStarted(WitRequest request)
    {
        Debug.Log("========== Wit Request Created ==========\n" +
                  "正式录音：" + _shouldBeRecording);

        _currentRequestTranscript = "";//清空本轮临时识别文本

        _isFinishingRequest = false;
        _waitingForResponse = true;

        //只要用户仍然按着按钮，就说明当前属于正式录音Session
        //后台预热Request虽然也会创建，但不会把文字显示出来
        if (_shouldBeRecording)
        {
            _sessionFinishedByInactivity = false;
        }
    }

    //边说边识别，实时返回
    private void OnPartialTranscription(string transcript)
    {
        Debug.Log("========== 收到 Wit Partial ==========\n" +
                  "transcript = [" + transcript + "]");

        //如果不是按住按钮正式录音，后台预热识别到的文字直接忽略
        if (!_shouldBeRecording)
        {
            Debug.Log("========== 当前不是正式录音，忽略后台 Partial ==========");
            return;
        }

        //如果正式录音Session已经因为长时间停顿结束
        //即使后台又收到文字，也不能再显示到输入框
        if (_sessionFinishedByInactivity)
        {
            Debug.Log("========== 当前正式录音 Session 已结束，忽略 Partial ==========");
            return;
        }

        //返回文字是空或者空格，直接跳过
        if (string.IsNullOrWhiteSpace(transcript))
        {
            return;
        }

        transcript = transcript.Trim();//Trim()：把字符串前后空格删掉

        if (!_hasReceivedFirstTranscript)
        {
            _hasReceivedFirstTranscript = true;

            //计算：按下按钮到收到文字的毫秒时间
            float totalDelay = (Time.unscaledTime - _recordingStartTime) * 1000f;

            //计算：麦克风真正开始收音到收到文字的毫秒
            float listeningDelay = (Time.unscaledTime - _listeningStartTime) * 1000f;

            Debug.Log("========== 收到第一条正式识别文字 ==========\n" +
                      "按下按钮 → 第一条文字：" + totalDelay.ToString("F0") + " ms\n" +
                      "开始监听 → 第一条文字：" + listeningDelay.ToString("F0") + " ms\n" +
                      "第一条文字：" + transcript);
        }

        Debug.Log("当前 Request Partial：" + transcript);

        _currentRequestTranscript = transcript;//保存本轮实时识别文字

        UpdateDisplayedTranscript();//调用函数更新UI输入框，实时显示说话内容
    }

    //本轮收音结束，Wit返回本轮完整转录文本
    private void OnFullTranscription(string transcript)
    {
        Debug.Log("========== 收到 Wit Full Transcription ==========\n" +
                  "transcript = [" + transcript + "]");

        //后台预热状态，丢弃结果
        if (!_shouldBeRecording)
        {
            Debug.Log("========== 当前不是正式录音，忽略后台 Full ==========");
            return;
        }

        //正式录音Session已经因为长时间停顿结束
        //此时即使收到Full，也不能再写入输入框
        if (_sessionFinishedByInactivity)
        {
            Debug.Log("========== 当前正式录音 Session 已结束，忽略 Full ==========");
            return;
        }

        if (string.IsNullOrWhiteSpace(transcript))
        {
            return;
        }

        transcript = transcript.Trim();

        _currentRequestTranscript = transcript;

        UpdateDisplayedTranscript();
    }

    //云端完整处理完毕，返回最终response数据包，一轮Request彻底结束
    private void OnRequestResponse(WitResponseNode response)
    {
        Debug.Log("========== Wit 收到最终 Response ==========");

        _waitingForResponse = false;
        _isFinishingRequest = false;

        string finalText = "";

        //response不为空，取出云端识别出来的text字段
        if (response != null)
        {
            Debug.Log("========== Wit 原始 Response ==========\n" +
                      response.ToString());

            finalText = response["text"];
        }

        //用户已经松手，只是后台预热的请求，不保存文字，重启后台监听然后return退出
        if (!_shouldBeRecording)
        {
            Debug.Log("========== 当前不是正式录音，忽略后台 Response ==========");

            _currentRequestTranscript = "";

            //安排协程：本轮请求结束之后重启后台监听
            ScheduleRestartBackgroundListening();

            return;
        }

        //正式录音Session已经因为长时间停顿结束
        //当前Request只是完成最后收尾，不能再次开始正式录音
        if (_sessionFinishedByInactivity)
        {
            Debug.Log("========== 正式录音 Session 已因长时间停顿结束 ==========");

            if (!string.IsNullOrWhiteSpace(finalText))
            {
                finalText = finalText.Trim();

                Debug.Log("========== 长时间停顿后的最终文字 ==========\n" +
                          finalText);

                _currentRequestTranscript = finalText;

                //最后一轮Request的文字仍然需要保存
                CommitCurrentRequest();
            }

            _currentRequestTranscript = "";

            //正式Session结束以后，输入框保留最终正式录音文字
            UpdateDisplayedTranscript();

            //切回后台预热
            FinishRecordingSession();

            return;
        }

        //拿到最终文字赋值给本轮临时变量
        if (!string.IsNullOrWhiteSpace(finalText))
        {
            finalText = finalText.Trim();

            Debug.Log("========== 本轮 Request 最终文字 ==========\n" +
                      finalText);

            _currentRequestTranscript = finalText;
        }

        //把本轮识别文字合并保存到历史记录 _committedTranscript
        CommitCurrentRequest();

        Debug.Log("========== 当前累计历史文字 ==========\n" +
                  _committedTranscript);

        _currentRequestTranscript = "";//清空本轮临时文本

        UpdateDisplayedTranscript();//刷新输入框UI

        //用户还按住按钮没有松手！
        //这里只代表当前Request结束，不代表整个正式录音Session结束
        if (_shouldBeRecording)
        {
            Debug.Log("========== 当前 Request 完成，但用户仍然按住按钮 ==========");

            _hasReceivedFirstTranscript = false;

            ScheduleRestartListening();

            return;
        }

        //用户松手，结束这一整段录音会话
        Debug.Log("========== 用户已经松手，正式录音 Session 完成 ==========");

        FinishRecordingSession();
    }

    //把本轮识别文字，拼接保存到历史字符串 _committedTranscript
    private void CommitCurrentRequest()
    {
        if (string.IsNullOrWhiteSpace(_currentRequestTranscript))
        {
            return;
        }

        string textToCommit = _currentRequestTranscript.Trim();

        if (string.IsNullOrWhiteSpace(textToCommit))
        {
            return;
        }

        //如果历史是空，直接赋值；不是空，中间加空格拼接新文字
        if (string.IsNullOrWhiteSpace(_committedTranscript))
        {
            _committedTranscript = textToCommit;
        }
        else
        {
            _committedTranscript += " " + textToCommit;
        }

        Debug.Log("========== Request 已提交到历史 ==========\n" +
                  _committedTranscript);
    }

    //拼接 历史文字+当前临时文字 写入TMP输入框
    private void UpdateDisplayedTranscript()
    {
        if (string.IsNullOrWhiteSpace(_currentRequestTranscript))
        {
            //没有临时文字 → 只显示历史保存的
            currentTranscript = _committedTranscript;
        }
        else if (string.IsNullOrWhiteSpace(_committedTranscript))
        {
            //没有历史，只显示本轮临时识别文字
            currentTranscript = _currentRequestTranscript;
        }
        else
        {
            //历史+本轮临时，中间空格隔开
            currentTranscript = _committedTranscript + " " + _currentRequestTranscript;
        }

        Debug.Log("========== 更新输入框 ==========\n" +
                  "currentTranscript = [" + currentTranscript + "]");

        if (promptInputField != null)
        {
            //修改输入框文字,不会触发 onValueChanged 回调
            //专门用于代码自动填充文字，避免循环报错
            promptInputField.SetTextWithoutNotify(currentTranscript);

            //把光标移动到文字最后面
            promptInputField.caretPosition = promptInputField.text.Length;

            //TMP强制刷新渲染文字，有时候改文字UI不刷新就要调用这个
            promptInputField.ForceLabelUpdate();
        }
        else
        {
            Debug.LogWarning("STTBridge: UpdateDisplayedTranscript 时没有找到 TMP_InputField！");
        }
    }

    //清空全部识别文字，每次长按开始录音调用
    public void ResetTranscription()
    {
        _committedTranscript = "";
        _currentRequestTranscript = "";
        currentTranscript = "";

        _hasReceivedFirstTranscript = false;

        //新增：开始新的正式Session时，取消之前的Session结束状态
        _sessionFinishedByInactivity = false;
        _waitingForResponse = false;

        if (promptInputField != null)
        {
            promptInputField.SetTextWithoutNotify("");
            promptInputField.caretPosition = 0;
            promptInputField.ForceLabelUpdate();
        }

        Debug.Log("========== 开始新的录音，清空全部历史文字 ==========");
    }

    //Wit麦克风真正开始收音触发
    private void OnListenStart()
    {
        //记录麦克风开始收音的时间
        _listeningStartTime = Time.unscaledTime;

        //标记麦克风正在收音
        _isListening = true;

        _isFinishingRequest = false;

        _restartScheduled = false;

        if (_shouldBeRecording)
        {
            float delay = (_listeningStartTime - _recordingStartTime) * 1000f;

            Debug.Log("========== Wit 真正开始监听 ==========\n" +
                      "按下按钮 → 开始监听：" +
                      delay.ToString("F0") +
                      " ms");
        }
        else
        {
            Debug.Log("========== Wit 后台预热监听开始 ==========");
        }
    }

    //麦克风停止收音触发
    private void OnListenStop()
    {
        Debug.Log("========== Wit 开始停止监听 ==========");

        _isListening = false;
    }

    //因为代码调用Deactivate关闭，麦克风停止
    private void OnListenStoppedByDeactivation()
    {
        Debug.Log("========== Wit 因 Deactivation 停止监听 ==========");

        _isListening = false;

        //标记进入请求收尾状态，等待云端返回结果
        _isFinishingRequest = true;

        //后台预热状态，不需要收尾
        if (!_shouldBeRecording)
        {
            _isFinishingRequest = false;
        }
    }

    //长时间没有说话，静默超时Wit自动停止麦克风
    private void OnListenStoppedByInactivity()
    {
        Debug.Log("========== Wit 因 Inactivity 停止监听 ==========");

        _isListening = false;

        //这里非常重要：
        //Inactivity表示Wit当前这一轮Request已经停止收音
        //但是只有“用户仍然按住按钮”并且我们决定把它视为长时间停顿时，
        //才结束整个正式录音Session
        if (_shouldBeRecording)
        {
            Debug.Log("========== Wit 长时间没有收到声音，正式录音 Session 结束 ==========");

            //标记正式Session已经结束
            _sessionFinishedByInactivity = true;

            //禁止后续Partial继续进入输入框
            _shouldBeRecording = false;

            _active = false;

            //当前Request仍然可能正在等待最终Response
            _isFinishingRequest = true;

            Debug.Log("========== 等待当前 Request 最终 Response ==========");

            return;
        }

        //用户已经松手，属于后台预热状态
        Debug.Log("========== 后台预热 Inactivity，文字全部忽略 ==========");

        _isFinishingRequest = false;

        ScheduleRestartBackgroundListening();
    }

    //安排协程：本轮请求结束之后重启下一轮正式录音（用户按住不放）
    private void ScheduleRestartListening()
    {
        //已经安排过重启，直接返回，避免重复启动协程
        if (_restartScheduled)
        {
            return;
        }

        _restartScheduled = true;

        StartCoroutine(RestartListeningAfterRequest());
    }

    //RestartListeningAfterRequest协程：等待上一轮请求收尾完成，再开启下一轮麦克风
    private IEnumerator RestartListeningAfterRequest()
    {
        yield return null;

        //while循环：只要上一轮还在收尾，就不停等待每一帧
        while (_isFinishingRequest)
        {
            yield return null;

            //中途用户松手了，直接结束录音会话，退出协程
            if (!_shouldBeRecording)
            {
                _restartScheduled = false;

                //如果不是Inactivity导致的结束，才执行Session结束
                if (!_sessionFinishedByInactivity)
                {
                    FinishRecordingSession();
                }

                yield break;
            }
        }

        //等待设置好的间隔时间
        if (restartDelay > 0f)
        {
            //new=新建一个对象,new WaitForSecondsRealtime(0.05f)→ 创建一个「等待 0.05 秒」的等待对象
            yield return new WaitForSecondsRealtime(restartDelay);
        }

        _restartScheduled = false;

        //间隔等待完用户松手，结束会话
        if (!_shouldBeRecording)
        {
            if (!_sessionFinishedByInactivity)
            {
                FinishRecordingSession();
            }

            yield break;
        }

        if (appVoiceExperience == null)
        {
            yield break;
        }

        //麦克风已经在听，不重复激活
        if (_isListening)
        {
            Debug.Log("========== Wit 已经重新监听，不重复 Activate ==========");

            yield break;
        }

        Debug.Log("========== 用户仍然按住按钮，启动下一轮 Request ==========");

        _active = true;

        //开启新一轮麦克风收音
        appVoiceExperience.Activate();
    }

    //安排协程：重启后台预热监听（用户松手之后，麦克风回到待命状态）
    private void ScheduleRestartBackgroundListening()
    {
        if (_restartScheduled)
        {
            return;
        }

        _restartScheduled = true;

        StartCoroutine(
            RestartBackgroundListening()
        );
    }

    private IEnumerator RestartBackgroundListening()
    {
        yield return null;

        if (appVoiceExperience == null)
        {
            _restartScheduled = false;

            yield break;
        }

        //如果此时用户又按下按钮，直接进入正式录音
        if (_shouldBeRecording)
        {
            Debug.Log("========== 用户已经按下按钮，进入正式录音 ==========");

            _restartScheduled = false;

            _active = true;

            if (_isListening)
            {
                yield break;
            }

            appVoiceExperience.Activate();

            yield break;
        }

        //已经后台监听，不用重复启动
        if (_isListening)
        {
            Debug.Log("========== Wit 已经处于后台监听状态 ==========");

            _restartScheduled = false;

            yield break;
        }

        Debug.Log("========== 重新启动 Wit 后台预热监听 ==========");

        _active = false;
        _isListening = false;
        _restartScheduled = false;

        appVoiceExperience.Activate();
    }

    // FinishRecordingSession：完整结束一整段长按录音会话
    //用户松手之后调用，重置录音标记，麦克风切回后台预热待命
    private void FinishRecordingSession()
    {
        _active = false;

        _shouldBeRecording = false;

        _isFinishingRequest = false;

        _currentRequestTranscript = "";

        _waitingForResponse = false;

        _sessionFinishedByInactivity = false;

        //刷新最终完整文字到输入框
        UpdateDisplayedTranscript();

        Debug.Log("========== 正式录音 Session 结束 ==========\n" +
                  "最终文字：" + _committedTranscript);

        //麦克风还开着，保持后台监听，不用操作
        if (_isListening)
        {
            Debug.Log("========== Wit 当前仍然后台监听 ==========");

            return;
        }

        //麦克风关闭了，安排重启后台预热监听，待命下一次按下按钮
        ScheduleRestartBackgroundListening();
    }

    //OnRequestError：Wit识别出错（麦克风失效、网络断开、权限不足）触发
    private void OnRequestError(string error, string message)
    {
        Debug.LogError("========== Wit 语音识别错误 ==========\n" +
                       "Error: " + error +
                       "\nMessage: " + message);

        // 如果正在长按正式录音：出错后尝试自动恢复收音，不用松手重按
        if (_shouldBeRecording)
        {
            Debug.Log("========== 正式录音期间发生错误，保持 Session ==========");

            _isListening = false;
            _isFinishingRequest = false;
            _restartScheduled = false;

            //启动协程，延迟后重启麦克风
            StartCoroutine(RestartListeningAfterError());

            return;
        }

        //如果正式Session已经因为Inactivity结束
        //错误发生时不再重新开启正式录音
        if (_sessionFinishedByInactivity)
        {
            Debug.Log("========== Session 已经因为长时间停顿结束，不恢复正式录音 ==========");

            _isListening = false;
            _isFinishingRequest = false;

            FinishRecordingSession();

            return;
        }

        //不是正式录音（后台预热报错），重置标记，重启后台监听
        _active = false;
        _isListening = false;
        _isFinishingRequest = false;

        ScheduleRestartBackgroundListening();
    }

    // RestartListeningAfterError：出错之后等待一小段时间，重新开启麦克风收音
    private IEnumerator RestartListeningAfterError()
    {
        //配置了延迟就等待，否则等一帧
        if (restartDelay > 0f)
        {
            yield return new WaitForSecondsRealtime(restartDelay);
        }
        else
        {
            yield return null;
        }

        //等待过程用户松手 → 结束录音会话
        if (!_shouldBeRecording)
        {
            FinishRecordingSession();

            yield break;
        }

        if (appVoiceExperience == null)
        {
            yield break;
        }

        //麦克风已经正常监听，不用重复启动
        if (_isListening)
        {
            yield break;
        }

        Debug.Log("========== Wit 错误恢复，继续当前录音 Session ==========");

        _active = true;

        appVoiceExperience.Activate();
    }

    //切换激活状态（预留外部调用）
    public void ToggleActivation()
    {
        SetActivation(!_active);
    }

    //SetActivation：核心对外接口,由 AskButtonManager 按钮脚本调用
    //true = 用户按下按钮，开始正式录音,false = 用户松开按钮，结束正式录音
    public void SetActivation(bool toActivated)
    {
        if (appVoiceExperience == null)
        {
            Debug.LogError("STTBridge: AppVoiceExperience 没有绑定！");

            return;
        }

        //用户按下按钮,toActivated = true
        if (toActivated)
        {
            //防止重复触发：已经在录音，直接返回
            if (_shouldBeRecording)
            {
                Debug.Log("========== 已经处于正式录音状态，不重复开始 ==========");

                return;
            }

            //记录按下按钮的时间戳，用于调试延迟
            _recordingStartTime = Time.unscaledTime;

            _shouldBeRecording = true;

            _active = true;

            _hasReceivedFirstTranscript = false;

            _currentRequestTranscript = "";

            _sessionFinishedByInactivity = false;

            _waitingForResponse = false;

            Debug.Log("========== 用户按下按钮，进入正式语音转文字 ==========");

            //麦克风已经后台预热开着了！不用等待初始化，直接识别（解决首字丢失）
            if (_isListening)
            {
                Debug.Log("========== Wit 已经后台监听，直接进入正式录音 ==========");

                UpdateDisplayedTranscript();

                return;
            }

            //上一轮请求还在收尾，等待收尾完成
            if (_isFinishingRequest)
            {
                Debug.Log("========== Wit 正在完成上一轮 Request，等待收尾 ==========");

                return;
            }

            //麦克风没开，手动激活Wit
            Debug.Log("========== Wit 当前没有监听，启动 Activate ==========");

            appVoiceExperience.Activate();

            return;
        }

        //用户松开按钮,toActivated = false
        if (!_shouldBeRecording)
        {
            return;
        }

        _shouldBeRecording = false;

        _active = false;

        Debug.Log("========== 用户松手，结束正式录音 Session ==========");

        //如果没有在云端收尾，直接保存当前识别到的文字
        if (!_isFinishingRequest)
        {
            if (!string.IsNullOrWhiteSpace(_currentRequestTranscript))
            {
                CommitCurrentRequest();

                _currentRequestTranscript = "";

                UpdateDisplayedTranscript();
            }

            //用户松手以后正式Session结束
            FinishRecordingSession();

            return;
        }

        //如果上一轮Request还在云端收尾
        //这里不能立刻开启后台预热，否则可能和当前Request冲突
        Debug.Log("========== 用户松手，但当前 Request 仍在收尾 ==========");
    }
}