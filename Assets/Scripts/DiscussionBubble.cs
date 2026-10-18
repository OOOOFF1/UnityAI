using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;//用于更改消息
using UnityEngine.UI;//用于更改气泡的实际图像
using System;

public class DiscussionBubble : MonoBehaviour
{
    //给面板加一个粗体分割标题，把一堆拖拽赋值的变量分组，方便在Inspector里看
    [Header("Elements")]
    [SerializeField] private TextMeshProUGUI messageText;
    [SerializeField] private Image bubbleImage;
    [SerializeField] private Sprite userBubbleSprite;
    [SerializeField] private GameObject voiceButton;
    [SerializeField] private Image voiceIcon;//语音按钮里面的小图标Image
    [SerializeField] private Sprite playSprite;//▶图标：未播放，可以点击朗读
    [SerializeField] private Sprite stopSprite;//■图标：正在朗读语音

    [Header("Settings")]
    [SerializeField] private Color userBubbleColor;

    [Header("Events")]
    //静态事件：点击气泡上的语音按钮就广播出去，参数：(气泡自身对象, 当前气泡的消息文本)
    public static Action<DiscussionBubble, string> onVoiceButtonClicked;

    //记录本气泡是否正在播放语音
    private bool _isPlaying;

    // 初始化气泡，外部（DiscussionManager）实例化完预制体之后调用这个方法
    // message：要显示的文字，isUserMessage：true=用户发的消息；false=AI回复消息
    public void Configure(string message, bool isUserMessage)
    {
        if (isUserMessage)
        {
            bubbleImage.sprite = userBubbleSprite;
            bubbleImage.color = userBubbleColor;
            voiceButton.SetActive(false);
        }
        else
        {
            voiceButton.SetActive(true);
        }

        messageText.text = message;//给文本赋值消息内容

        //强制TMP立刻更新文字网格，让ContentSizeFitter马上计算气泡大小
        //如果不ForceMeshUpdate，文字改完，气泡尺寸不会立刻刷新，会出现气泡大小错乱BUG
        messageText.ForceMeshUpdate();

        //初始化图标为静音图标
        _isPlaying = false;
        if (voiceIcon != null)
        {
            voiceIcon.sprite = playSprite;
        }
    }

    //删除Update，避免每帧刷新Sprite导致的性能浪费
    /*
    void Update()
    {
        //每一帧根据状态切换本气泡图标
        if (voiceIcon == null) return;

        voiceIcon.sprite = _isPlaying ? stopSprite : playSprite;
    }
    */

    public void VoiceButtonCallback()
    {
        //onVoiceButtonClicked就像一个通讯录，存了一堆订阅好的回调函数
        //.Invoke(参数1, 参数2)：挨个调用通讯录里存的所有函数，并且把括号里的参数传给这些回调
        //?. 是空值判断：如果事件没有任何人订阅，就不执行 Invoke，防止报错
        onVoiceButtonClicked?.Invoke(this, messageText.text);
    }

    public void CopyToClipboardCallback()
    {
        //GUIUtility.systemCopyBuffer：Unity访问操作系统剪贴板的变量
        GUIUtility.systemCopyBuffer = messageText.text;
    }

    //外部TTSBridge调用，设置本气泡播放状态
    public void SetPlayState(bool isPlaying)
    {
        //状态未改变直接返回，状态改变时才刷新图标
        if (_isPlaying == isPlaying)
        {
            return;
        }

        _isPlaying = isPlaying;

        if (voiceIcon != null)
        {
            voiceIcon.sprite = _isPlaying ? stopSprite : playSprite;
        }
    }
}