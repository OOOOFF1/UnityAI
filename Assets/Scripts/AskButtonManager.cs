using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class AskButtonManager : MonoBehaviour
{
    [Header("Elements")]
    [SerializeField] private DiscussionManager discussionManager;
    [SerializeField] private XunfeiSTTBridge sttBridge;// 讯飞语音识别：把人声转文字（STT语音转文字）

    //保留原来的 TTSBridge（暂时不用，未来可能切回 Wit）
    [SerializeField] private TTSBridge ttsBridge;

    //当前实际在用的讯飞 TTS,优先级高于 ttsBridge，两者都为空时跳过停止逻辑
    [SerializeField] private XunfeiTTSBridge xunfeiTtsBridge;// 当前在用讯飞语音合成：AI回答转语音朗读TTS
    [SerializeField] private TMP_InputField promptInputField;

    [Header("Graphics")]
    [SerializeField] private GameObject askText;
    [SerializeField] private GameObject micImage;

    [Header("Settings")]
    private bool recording;//标记：是否现在正在录音 true=录音中 false=没录音
    //只读属性，别的脚本可以读 IsRecording 看是否正在录音，不能外部修改recording变量
    public bool IsRecording => recording;

    //对外公开方法，别的脚本拿到输入框组件
    public TMP_InputField GetPromptInputField()
    {
        return promptInputField;
    }

    private void Start()
    {
        ShowMicImage();

        if (promptInputField != null)
        {
            //给输入框注册回调：输入框文字一变，就执行 InputFieldValueChangedCallback
            promptInputField.onValueChanged.AddListener(InputFieldValueChangedCallback);
        }

        RefreshButtonState();//刷新按钮该显示图标还是文字
    }

    //销毁物体的时候，移除事件监听！防止内存泄漏、重复触发bug
    private void OnDestroy()
    {
        if (promptInputField != null)
        {
            promptInputField.onValueChanged.RemoveListener(InputFieldValueChangedCallback);
        }
    }

    //按钮按下事件,对应UI的 PointerDown（鼠标/手指按下按钮那一刻）
    public void PointerDownCallback()
    {
        //判空，如果讯飞识别组件、输入框没拖拽赋值，直接报错返回
        if (sttBridge == null || promptInputField == null)
        {
            Debug.LogError("AskButtonManager：sttBridge 或 promptInputField 没有绑定！");
            return;
        }

        // 输入框有文字 → 发送消息
        if (!string.IsNullOrWhiteSpace(promptInputField.text))
        {
            if (discussionManager != null)
            {
                discussionManager.AskButtonCallback();//交给对话管理器发送提问
            }

            //发送文字之后，告诉语音识别：不要把录音结果再写回输入框
            if (sttBridge != null)
            {
                sttBridge.SuppressOutput();
            }
            RefreshButtonState();
            return;
        }

        // 已在录音状态，防止重复启动
        if (recording)
        {
            return;
        }

        // 按下按钮录音前：先把AI正在说话的语音朗读给停掉
        //优先停止讯飞 TTS，为空时才回退到 Wit TTS
        if (xunfeiTtsBridge != null)
        {
            xunfeiTtsBridge.StopSpeaking();
        }
        else if (ttsBridge != null)
        {
            ttsBridge.StopSpeaking();
        }

        // 清空上一轮 STT
        sttBridge.ResetTranscription();//清空上一次录音识别出来的文字，防止残留旧内容
        recording = true;//标记状态：进入录音
        RefreshButtonState();
        sttBridge.SetActivation(true);//打开讯飞语音识别，开始拾音录音
        Debug.Log("========== AskButtonManager：开始语音输入 ==========");
    }

    public void PointerUpCallback()
    {
        Debug.Log("========== AskButtonManager 收到 PointerUp ==========");

        if (!recording)//如果根本不在录音，直接退出，不做处理
        {
            return;
        }

        recording = false;//标记结束录音

        if (sttBridge != null)
        {
            sttBridge.SetActivation(false);//关闭讯飞语音识别，停止拾音
        }

        RefreshButtonState();
        Debug.Log("========== AskButtonManager：结束语音输入 ==========");
    }

    //输入框文字发生变化时触发这个回调函数
    private void InputFieldValueChangedCallback(string value)
    {
        RefreshButtonState();//文字一改，立刻刷新按钮：有字显示发送、无字显示麦克风
    }

    //刷新按钮外观状态
    private void RefreshButtonState()
    {
        if (promptInputField == null)
        {
            return;
        }

        //如果正在录音：永远显示麦克风图标
        if (recording)
        {
            ShowMicImage();
            return;
        }

        //输入框不为空，有文字：显示发送文字
        if (!string.IsNullOrWhiteSpace(promptInputField.text))
        {
            ShowAskText();
            return;
        }
        //否则输入框为空，显示麦克风图标
        ShowMicImage();
    }

    //显示麦克风图标，隐藏发送文字
    private void ShowMicImage()
    {
        if (micImage != null)
        {
            micImage.SetActive(true);
        }

        if (askText != null)
        {
            askText.SetActive(false);
        }
    }

    //显示“发送”文字，隐藏麦克风图标
    private void ShowAskText()
    {
        if (micImage != null)
        {
            micImage.SetActive(false);
        }

        if (askText != null)
        {
            askText.SetActive(true);
        }
    }
}