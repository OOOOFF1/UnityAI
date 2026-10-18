using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class ContentAutoScroll : MonoBehaviour
{
    [Header("Elements")]
    [SerializeField] private ScrollRect scrollRect;
    private RectTransform content;

    private void Start()
    {
        content = GetComponent<RectTransform>();
        //订阅静态事件：当DiscussionManager收到新消息时，触发DelayScrollDown
        DiscussionManager.onMessageReceived += DelayScrollDown;
    }

    private void OnDestroy()
    {
        //取消事件订阅，防止切换场景内存泄漏、多次重复执行滚动
        DiscussionManager.onMessageReceived -= DelayScrollDown;
    }

    //事件回调，收到新消息就进来，开启协程
    private void DelayScrollDown()
    {
        StartCoroutine(ScrollDown());
    }

    private IEnumerator ScrollDown()
    {
        // 等一帧，让气泡生成、LayoutGroup、ContentSizeFitter先计算完成
        // 刚实例化消息气泡，这一帧Layout布局还没有计算，content高度还是旧的
        yield return null;
        // 强制刷新整个画布，把UI变更提交
        Canvas.ForceUpdateCanvases();
        //强制立刻重新计算LayoutGroup、ContentSizeFitter布局，更新content真实高度
        LayoutRebuilder.ForceRebuildLayoutImmediate(content);
        // 再等一帧，给布局计算完成的时间
        yield return null;
        // verticalNormalizedPosition = 0代表滚动到最底部, =1 代表滚动到最顶部
        scrollRect.verticalNormalizedPosition = 0f;
    }
}
/*
 using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ContentAutoScroll : MonoBehaviour
{
    private RectTransform rt;

    void Start()
    {
        rt=GetComponent<RectTransform>();
        // 订阅事件：只要DiscussionManager执行onMessageReceived?.Invoke()，就运行DelayScrollDown
        // 也就是：一旦生成新气泡，就触发延迟滚动
        DiscussionManager.onMessageReceived += DelayScrollDown;
    }

    private void OnDestroy()
    {
        // 取消订阅！物体销毁时退群，防止重复调用、内存泄漏
        DiscussionManager.onMessageReceived -= DelayScrollDown;
    }

    void Update()
    {
        
    }

    private void DelayScrollDown()
    {
        // 收到事件广播后，不立刻滚动，延迟0.3秒再执行ScrollDown
        // 为什么要延迟？
        // Instantiate生成气泡，ContentSizeFitter需要一点点时间计算气泡高度。
        // 如果马上滚动，Content高度还没更新完成，滚动位置会算错。
        // Invoke("函数名字", 延迟秒数)，等待0.3秒再跑ScrollDown
        Invoke("ScrollDown", .3f);
    }

    private void ScrollDown()
    {
        //取出这个UI当前的锚点位置（UI坐标，Vector2只有X、Y）
        //anchoredPosition：UI 特有，物体相对于锚点的偏移
        //保留原来的 x 不变，只修改 y，最后再写回
        Vector2 anchoredPosition = rt.anchoredPosition;
        //rt.sizeDelta.y：这个 UI 物体自身的高度，Mathf.Max(a, b)：取两个数字里面更大那一个
        //如果sizeDelta.y大于 0 → y 就等于物体高度
        //如果 sizeDelta.y 是负数 → y 强制变成 0，避免负数出错
        anchoredPosition.y=Mathf.Max(0,rt.sizeDelta.y);
        //把算好的坐标赋值回去，真正移动UI
        rt.anchoredPosition=anchoredPosition;
    }
}
*/
