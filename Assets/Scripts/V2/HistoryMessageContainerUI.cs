using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using System;
using UnityEngine.UI;

public class HistoryMessageContainerUI : MonoBehaviour
{
    [Header("Elements")]
    [SerializeField] private TextMeshProUGUI dateText;//显示会话日期的文字
    [SerializeField] private TextMeshProUGUI firstPromptText;//预览第一条聊天内容
    [SerializeField] private Button itemButton;//这个条目本身的Button组件

    [Header("Data")]
    [SerializeField] private Conversation conversation;//绑定当前这一条对应的聊天会话数据
    [Header("Action")]
    //静态事件！所有历史条目共用这一个广播。点击条目，把自己的conversation广播出去
    public static Action<Conversation> onHistoryButtonClicked;

    //HistoryManager生成预制体之后调用containerInstance.Configure(conversation)
    //给这个 UI 条目填入聊天数据，刷新文字显示
    public void Configure(Conversation conversation)
    {
        //把传进来的会话存到脚本成员变量，这个UI就绑定这一段聊天
        this.conversation = conversation;
        string date = conversation.date;
        // 安全判断：如果会话没有消息，或者消息太少
        if (conversation.messages == null || conversation.messages.Length <= 1)
        {
            dateText.text = date;
            firstPromptText.text = "No message";//显示无消息
            return;
        }
        // messages[0]一般是系统提示词，用户第一条真实消息在索引1
        string firstMessage = conversation.messages[1].content;
        dateText.text = date;
        firstPromptText.text = firstMessage;//预览第一条用户消息
    }

    //手指松开触发普通点击
    public void ButtonClickedCallback()
    {
        //短按松手：通知外面打开历史聊天
        onHistoryButtonClicked?.Invoke(conversation);
    }

    //供LongPressButton的onLongPress事件绑定，长按触发删除弹窗
    public void OnLongPressDelete()
    {
        ShowDeletePopup();
    }

    //弹出删除确认框
    private void ShowDeletePopup()
    {
        if (RolePopupManager.instance == null) return;
        //把当前这条会话传给弹窗脚本，弹窗点确认就会调用HistoryManager删除这条会话
        RolePopupManager.instance.ShowHistoryDeletePopup(conversation);
    }
}
