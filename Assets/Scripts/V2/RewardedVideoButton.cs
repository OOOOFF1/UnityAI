using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;

public class RewardedVideoButton : MonoBehaviour
{
    [Header("Elements")]
    [SerializeField] private Button button;//当前这个奖励广告按钮

    [Header("Events")]
    [SerializeField] private UnityEvent onVideoWatched;

    //定时检查间隔（秒），不用每帧检查，节省CPU开销
    [SerializeField] private float checkInterval = 0.5f;

    private void Start()
    {
        //用InvokeRepeating替代每帧Update，nameof(RefreshButtonState)等价写字符串"RefreshButtonState"
        // InvokeRepeating("方法名", 延迟多久第一次执行, 之后每隔多少秒重复执行)
        // 0f：游戏启动立刻执行第一次RefreshButtonState
        // checkInterval=0.5f：之后每0.5秒调用一次RefreshButtonState
        InvokeRepeating(nameof(RefreshButtonState),0f,checkInterval);
    }

    private void OnDestroy()
    {
        //对象销毁时取消定时器
        //物体销毁，必须把InvokeRepeating定时任务关掉；不然物体没了定时器还在跑，会空引用报错
        CancelInvoke(nameof(RefreshButtonState));
    }

    //原来每帧执行的逻辑，抽成独立方法
    private void RefreshButtonState()
    {
        if (button == null)
        {
            return;
        }

        //根据当前 Rewarded Video 是否已经准备好，决定按钮能不能点击
        if (AdsManager.instance != null)
        {
            //调用AdsManager的方法，查询激励广告是否加载完成
            //true → button.interactable=true 按钮可以点；false→按钮灰色不能点
            button.interactable =AdsManager.instance.IsRewardedVideoAvailable();
        }
        else
        {
            button.interactable = false;//广告管理器不存在，按钮直接禁用
        }
    }

    //按钮点击回调
    public void ButtonClickedCallback()
    {
        //防止 AdsManager 不存在
        if (AdsManager.instance == null)
        {
            UnityEngine.Debug.LogWarning("AdsManager 不存在，无法播放 Rewarded Video。");
            return;
        }

        //加括号OnVideoWatched()代表现在马上就执行这个函数，把返回值传给方法，广告还没弹出来就直接给奖励，直接 BUG
        //传OnVideoWatched → 把函数本身当做一个参数，交给 ShowRewardedVideo。不是现在立刻运行 OnVideoWatched
        //播放 Rewarded Video，广告完整获得奖励以后，才会执行 OnVideoWatched
        AdsManager.instance.ShowRewardedVideo(OnVideoWatched);
    }

    //广告播放完成、成功拿到奖励以后执行
    private void OnVideoWatched()
    {
        //增加5条消息（在原有剩余数量上叠加）
        if (ActivityManager.instance != null)
        {
            ActivityManager.instance.AddRewardedMessages();
        }

        //隐藏无消息提示面板
        if (UIManager.instance != null)
        {
            UIManager.instance.HideNoMessages();
        }

        //看完广告立刻保存聊天历史
        if (HistoryManager.instance != null)
        {
            HistoryManager.instance.TrySaveConversation();
        }

        //执行 Inspector 里面额外设置的事件
        onVideoWatched?.Invoke();
    }
}
