using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Meta.WitAi.TTS.Utilities;

public class TTSBridge : MonoBehaviour
{
    [Header("Elements")]
    [SerializeField] private TTSSpeaker speaker;

    //记录当前正在朗读的气泡
    private DiscussionBubble _currentPlayBubble;

    //启动保护标记：专门解决Wit云端语音延迟导致的图标闪烁bug
    private bool _isLaunching;

    // Start is called before the first frame update
    void Start()
    {
        //监听 AI 回复
        DiscussionBubble.onVoiceButtonClicked += VoiceButtonClickedCallback;

        //DiscussionManager.onAIMessageReceived += Speak;

        //初始状态：没有正在等待加载的语音
        _isLaunching = false;
    }

    private void OnDestroy()
    {
        //先停止正在进行的语音播放
        if (speaker != null)
        {
            speaker.Stop();
        }

        //取消监听
        DiscussionBubble.onVoiceButtonClicked -= VoiceButtonClickedCallback;

        //DiscussionManager.onAIMessageReceived -= Speak;
    }

    // Update is called once per frame
    void Update()
    {
        if (speaker == null)
        {
            return;
        }

        if (_isLaunching)
        {
            if (speaker.IsSpeaking)
            {
                _isLaunching = false;
            }

            return;//函数直接结束，直接结束本帧，不会运行下方“语音结束复原图标”代码
        }

        //如果有正在播放的气泡，并且语音已经停止 → 复原图标
        if (_currentPlayBubble != null && !speaker.IsSpeaking)
        {
            _currentPlayBubble.SetPlayState(false);
            _currentPlayBubble = null;
        }
    }

    private void VoiceButtonClickedCallback(
        DiscussionBubble bubble,
        string message)
    {
        if (speaker == null)
        {
            Debug.LogError("TTSBridge: TTSSpeaker 没有绑定！");
            return;
        }

        //如果现在正在说话 → 停止语音，把旧气泡状态复原
        if (speaker.IsSpeaking)
        {
            Debug.Log("Stopping the speaker");

            speaker.Stop();//强制终止语音播放

            if (_currentPlayBubble != null)
            {
                //把正在朗读的气泡恢复静音图标
                _currentPlayBubble.SetPlayState(false);
                _currentPlayBubble = null;
            }

            _isLaunching = false;

            return;
        }

        //没有在播放，开始朗读，标记当前点击的气泡为播放状态
        _currentPlayBubble = bubble;

        _currentPlayBubble.SetPlayState(true);

        _isLaunching = true;//标记：刚刚启动朗读，SDK还没就绪

        Debug.Log("Started Speakings");

        Speak(message);
    }

    public void StopSpeaking()
    {
        if (speaker == null)
        {
            return;
        }

        //停止当前语音
        speaker.Stop();

        //恢复当前气泡图标
        if (_currentPlayBubble != null)
        {
            _currentPlayBubble.SetPlayState(false);
            _currentPlayBubble = null;
        }

        //取消“正在启动语音”的状态
        _isLaunching = false;

        Debug.Log("TTSBridge：AI语音已停止");
    }

    private void Speak(string message)
    {
        if (speaker == null)
        {
            Debug.LogError("TTSBridge: TTSSpeaker 没有绑定！");
            return;
        }

        //文字完全空空荡荡或者只有空格、回车、换行
        if (string.IsNullOrWhiteSpace(message))
        {
            Debug.LogWarning("TTSBridge: 收到的语音文本为空，跳过朗读。");

            if (_currentPlayBubble != null)
            {
                _currentPlayBubble.SetPlayState(false);
                _currentPlayBubble = null;
            }

            _isLaunching = false;

            return;//直接终止这个朗读函数，后面拆分文字、播放语音的代码全部不再执行
        }

        //Wit 语音接口不能接收超级长的一整段文字，太长会报错、声音乱掉
        //消息太长，语音SDK无法处理，不要传递一个大块，而需要分割消息

        //按句号分割，并过滤掉空字符串
        string[] messages = message.Split(
            new char[] { '.' },

            //分割之后，如果出现空白内容，直接删掉，不保留
            System.StringSplitOptions.RemoveEmptyEntries
        );

        if (messages.Length == 0)
        {
            _isLaunching = false;

            return;
        }

        //SpeakQueuedAsync(messages)：排队朗读，接收数组（一堆短句）
        //SpeakQueuedAsync是协程函数（IEnumerator）,必须套上StartCoroutine()，协程才开始执行
        //并且是交给speaker那个物体跑协程，保证语音可以完整播完
        speaker.StartCoroutine(
            speaker.SpeakQueuedAsync(messages)
        );

        //speaker.Speak(message);
    }
}
