using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;

//一个通用长按按钮组件，可以挂在任意 UI 物体上
//实现三个接口，用来接收UGUI的按下、抬起、滑出事件
public class LongPressButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    [Header("Settings")]
    [SerializeField] private float longPressTime = 0.5f;

    [Header("Events")]
    //UnityEvent既可以代码订阅，也可以在编辑器面板直接拖物体选函数，不用写一行订阅代码
    [SerializeField] private UnityEvent onLongPress;

    private bool pointerDown;//标记：是否正在按住按钮
    private bool longPressTriggered;//标记：长按是否已经触发过一次
    private Coroutine longPressCoroutine;//协程的引用，用来随时停止计时协程

    //PointerEventData是 UGUI 事件系统自带的类，当点击 UI 触发OnPointerDown / OnPointerUp / OnPointerExit这些接口方法时，Unity 自动给你传入这个eventData参数
    //里面装的是这一次点击 / 触摸的全部信息：
    //鼠标 / 手指点击的屏幕坐标位置，是左键还是右键、按压状态，当前点到了哪个 GameObject，是否拖拽、点击按下的压力等等
    //按下瞬间
    public void OnPointerDown(PointerEventData eventData)
    {
        pointerDown = true;//标记：现在处于按住状态
        longPressTriggered = false;//重置标记，还没触发长按

        //如果之前有正在跑的长按协程，先把它停掉，防止多个协程同时跑
        if (longPressCoroutine != null)
        {
            StopCoroutine(longPressCoroutine);
        }
        //开启协程，开始倒计时0.6秒
        longPressCoroutine = StartCoroutine(CheckLongPress());
    }

    //手指抬起
    public void OnPointerUp(PointerEventData eventData)
    {
        pointerDown = false;//取消按住标记

        //停止计时协程，长按不会触发
        if (longPressCoroutine != null)
        {
            StopCoroutine(longPressCoroutine);
            longPressCoroutine = null;
        }
    }

    //按住的时候手指滑出按钮范围
    public void OnPointerExit(PointerEventData eventData)
    {
        pointerDown = false;

        if (longPressCoroutine != null)
        {
            StopCoroutine(longPressCoroutine);
            longPressCoroutine = null;
        }
    }

    private IEnumerator CheckLongPress()
    {
        //等待 longPressTime 秒，这里就是等待0.6秒
        yield return new WaitForSeconds(longPressTime);

        //等待时间到之后，再做一次判断：手指仍然按着按钮 && 长按还没有触发过
        if (pointerDown && !longPressTriggered)
        {
            longPressTriggered = true;//标记已经触发长按，避免重复执行
            onLongPress?.Invoke();
        }
    }
}
