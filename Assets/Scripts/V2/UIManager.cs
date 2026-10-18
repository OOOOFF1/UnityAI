using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;

public class UIManager : MonoBehaviour
{
    public static UIManager instance;

    [Header("Elements")]
    [SerializeField] private CanvasGroup chatCG;
    [SerializeField] private CanvasGroup noMessagesCG;
    [SerializeField] private CanvasGroup SettingsCG;

    [Header("Actions")]
    public static Action onBackFromChat;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
        DontDestroyOnLoad(gameObject);//切换场景不销毁这个管理器

        //只订阅属于UIManager自己的事件
        //DiscussionManager里面触发这个事件 → UIManager显示“暂无消息”面板
        DiscussionManager.onTryingToSendMessagesButCant += ShowNoMessages;
    }

    private void OnDestroy()
    {
        //销毁的时候，取消自己订阅的事件，防止内存泄漏
        DiscussionManager.onTryingToSendMessagesButCant -= ShowNoMessages;
    }

    void Start()
    {
        HideChat();
        HideNoMessages();
        HideSettingsPanel();
    }

    // 只打开聊天面板；角色切换、历史加载业务全部DiscussionManager内部回调处理
    public void ShowChat()
    {
        ShowCanvasGroup(chatCG);
    }

    // 历史条目点击触发Invoke(conversation)，DiscussionManager接收做加载，UIManager仅仅打开面板
    public void ShowChat(Conversation conversation)
    {
        ShowCanvasGroup(chatCG);
    }

    private void HideChat()
    {
        HideCanvasGroup(chatCG);
    }

    public void ShowSettingsPanel()
    {
        ShowCanvasGroup(SettingsCG);
    }

    private void HideSettingsPanel()
    {
        HideCanvasGroup(SettingsCG);
    }

    public void ShowNoMessages()
    {
        ShowCanvasGroup(noMessagesCG);
    }

    public void HideNoMessages()
    {
        HideCanvasGroup(noMessagesCG);
    }

    public void BackToMenuFromChat()
    {
        HideChat();
        HideSettingsPanel();
        onBackFromChat?.Invoke();//广播事件：告诉别的脚本，已经从聊天退回到菜单了
    }

    public void ShowCanvasGroup(CanvasGroup canvasGroup)
    {
        canvasGroup.alpha = 1;
        canvasGroup.interactable = true;
        canvasGroup.blocksRaycasts = true;
    }

    public void HideCanvasGroup(CanvasGroup canvasGroup)
    {
        canvasGroup.alpha = 0;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;
    }
}
