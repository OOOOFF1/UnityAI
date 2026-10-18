using System;
using System.Collections;
using UnityEngine;

//XunfeiTTSBridge是TTS 语音朗读的调度管理器（桥梁层）
//它不直接实现 WebSocket、音频解码，把底层 TTS 网络请求/播放交给XunfeiTTSSpeaker
public class XunfeiTTSBridge : MonoBehaviour
{
    [Header("讯飞 TTS Speaker")]
    //底层 TTS 播放器实例，真正对接讯飞接口、播放声音的对象
    [SerializeField] private XunfeiTTSSpeaker speaker;

    private DiscussionBubble currentPlayBubble;//当前正在朗读的那个聊天气泡

    private Coroutine speakQueueCoroutine;//保存分句朗读队列协程的引用

    //点击按钮，发起请求，网络握手、等待音频流回来这段时间，isLaunching=true
    //等 speaker 真正开始播放音频（speaker.IsSpeaking=true），isLaunching=false
    //作用：区分 正在发起请求还没出声 和 已经在播放声音 两种状态
    private bool isLaunching;//正在启动 TTS 的标记（启动过渡状态）

    private void Start()
    {
        DiscussionBubble.onVoiceButtonClicked +=VoiceButtonClickedCallback;
        isLaunching = false;//初始化，标记不在启动中
    }

    private void OnDestroy()
    {
        DiscussionBubble.onVoiceButtonClicked -=VoiceButtonClickedCallback;

        if (speaker != null)
        {
            speaker.StopSpeak();
        }
    }

    //每帧都会跑，负责实时监测播放状态，同步 UI 气泡
    private void Update()
    {
        if (speaker == null)
        {
            return;
        }

        if (isLaunching)//当前处于 正在发起 TTS 请求，等待音频 阶段
        {
            //如果 speaker 已经开始播放（speaker.IsSpeaking=true），把isLaunching关掉，过渡状态结束
            if (speaker.IsSpeaking)
            {
                isLaunching = false;
            }
            return;
        }

        //有正在播放的气泡，并且 speaker停止发声了
        if (currentPlayBubble != null &&!speaker.IsSpeaking)
        {
            currentPlayBubble.SetPlayState(false);//把气泡 UI 改成未播放状态
            currentPlayBubble = null;//清空当前气泡记录
        }
    }

    private void VoiceButtonClickedCallback(DiscussionBubble bubble,string message)
    {
        if (speaker == null)
        {         
            return;
        }

        //如果正在朗读，再次点击按钮 → 停止朗读
        if (speaker.IsSpeaking)
        {            
            StopSpeaking();
            return;
        }
        //文本是空 / 全空格，不发起朗读
        if (string.IsNullOrWhiteSpace(message))
        {          
            return;
        }

        currentPlayBubble =bubble;//记住当前气泡
        currentPlayBubble.SetPlayState(true);
        isLaunching = true;
        Speak(message);
    }

    public void StopSpeaking()
    {
        if (speakQueueCoroutine != null)
        {
            //如果分句排队协程还在跑，直接停止协程，清空引用
            StopCoroutine(speakQueueCoroutine);
            speakQueueCoroutine = null;
        }

        if (speaker != null)
        {
            speaker.StopSpeak();
        }

        if (currentPlayBubble != null)
        {
            currentPlayBubble.SetPlayState(false);
            currentPlayBubble = null;
        }

        isLaunching = false;
    }

    //接收完整文本，调用SplitText分句，启动协程队列，一句一句读
    private void Speak(string message)
    {
        if (speaker == null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        //把一长段 AI 回复，按中文标点切分成句子数组
        string[] messages =SplitText(message);
        //开启协程排队朗读，保存协程引用到speakQueueCoroutine
        speakQueueCoroutine = StartCoroutine(SpeakQueueCoroutine(messages));
    }

    private IEnumerator SpeakQueueCoroutine(string[] textList)
    {
        for (int i = 0; i < textList.Length; i++)
        {
            string text =textList[i].Trim();

            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            //speaker.SpeakAsync(text) 是一个自定义协程，内部去连接讯飞、接收音频、播放声音
            //yield return：暂停当前队列协程，等待这句朗读完全结束之后，才继续 for 循环，读下一句
            yield return speaker.SpeakAsync(text);

            // 如果用户主动停止
            if (speaker == null)
            {
                yield break;
            }
        }

        speakQueueCoroutine = null;
        isLaunching = false;
        if (currentPlayBubble != null)
        {
            currentPlayBubble.SetPlayState(false);
            currentPlayBubble = null;
        }     
    }

    private string[] SplitText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<string>();
        }

        char[] separators ={'。','！','？','!','?','\n'};

        //StringSplitOptions.RemoveEmptyEntries = 分割之后，自动扔掉空字符串,但原始文本如果没有任何分隔符，会保留整个文本
        string[] result =text.Split(separators,StringSplitOptions.RemoveEmptyEntries);

        if (result.Length == 0)
        {
            // 新建数组，里面放原始text（"   "）,返回数组长度 = 1，数组内容：["   "]
            //统一返回值类型，让调用方永远拿到一个非空数组，减少空数组带来的潜在 BUG
            return new[] { text };
        }

        return result;
    }
}

