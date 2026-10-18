using System;//引入System命名空间 → 为了使用Action委托
using UnityEngine;
using Unity.Services.LevelPlay;//导入 LevelPlay 广告 SDK 的命名空间，所有广告相关类：LevelPlayRewardedAd、LevelPlay、LevelPlayAdInfo等都来自这个包

public class AdsManager : MonoBehaviour
{
    //单例：静态实例，别的脚本可以直接 AdsManager.instance 访问这个脚本
    //不用每个脚本都拖拽 AdsManager
    public static AdsManager instance;

    [Header("Settings")]
    //Rewarded Video 广告对象,这个对象负责加载广告、显示广告以及接收广告回调
    //LevelPlayRewardedAd是 SDK 自带的类，一条激励广告就靠这个对象管理：加载、展示、监听广告各种事件
    private LevelPlayRewardedAd rewardedAd;

    [Header("Events")]
    //保存当前播放广告之后需要执行的事件,点击看广告按钮时，把看完广告要给玩家的奖励逻辑存在这里
    //例如 RewardedVideoButton 点击以后，可以把 ActivityManager.ResetMessages() 放进这个事件
    private Action videoWatchedAction;

    //记录 LevelPlay SDK 是否已经初始化成功
    //广告 SDK 必须先初始化成功，才能创建、加载广告，没初始化就调用加载广告会报错，这个变量用来做校验
    private bool isInitialized;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
        //跨场景不销毁。切换场景的时候这个物体不会被销毁，广告 SDK 不用重复初始化，全程保留广告管理器
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        //注册 LevelPlay 初始化成功事件
        LevelPlay.OnInitSuccess += SdkInitializationCompletedEvent;

        //注册 LevelPlay 初始化失败事件
        LevelPlay.OnInitFailed += SdkInitializationFailedEvent;

        //初始化 LevelPlay,App Key 从 AdConfigs 统一读取
        LevelPlay.Init(AdConfigs.AppKey);
    }


    private void OnDestroy()
    {
        //取消订阅 LevelPlay 初始化事件
        LevelPlay.OnInitSuccess -= SdkInitializationCompletedEvent;
        LevelPlay.OnInitFailed -= SdkInitializationFailedEvent;

        //如果 Rewarded Video 对象已经创建,需要取消之前注册的广告事件
        if (rewardedAd != null)
        {
            rewardedAd.OnAdLoaded -= RewardedVideoOnAdLoaded;
            rewardedAd.OnAdLoadFailed -= RewardedVideoOnAdLoadFailed;
            rewardedAd.OnAdDisplayed -= RewardedVideoOnAdDisplayed;
            rewardedAd.OnAdDisplayFailed -= RewardedVideoOnAdDisplayFailed;
            rewardedAd.OnAdRewarded -= RewardedVideoOnAdRewarded;
            rewardedAd.OnAdClosed -= RewardedVideoOnAdClosed;
            rewardedAd.OnAdClicked -= RewardedVideoOnAdClicked;
            rewardedAd.OnAdInfoChanged -= RewardedVideoOnAdInfoChanged;
        }
    }

    //LevelPlay SDK 初始化成功
    private void SdkInitializationCompletedEvent(LevelPlayConfiguration configuration)
    {
        //Debug.Log("LevelPlay SDK 初始化成功。");
        isInitialized = true;

        //LevelPlay 初始化成功以后,才能创建 Rewarded Video 广告对象
        CreateRewardedVideo();

        //创建完成以后马上加载第一条广告
        LoadRewardedVideo();
    }

    //LevelPlay SDK 初始化失败
    private void SdkInitializationFailedEvent(LevelPlayInitError error)
    {
        isInitialized = false;
        Debug.LogError("LevelPlay SDK 初始化失败："+ error.ErrorMessage);
    }


    //创建 Rewarded Video 广告对象
    private void CreateRewardedVideo()
    {
        //避免重复 new，重复注册事件，防止多次订阅导致回调执行多次
        if (rewardedAd != null)
        {
            return;
        }

        //从 AdConfigs 获取 Rewarded Video Ad Unit ID
        rewardedAd = new LevelPlayRewardedAd(AdConfigs.RewardedVideoAdUnitId);

        //注册 Rewarded Video 广告事件
        //广告加载成功
        rewardedAd.OnAdLoaded +=RewardedVideoOnAdLoaded;
        //广告加载失败
        rewardedAd.OnAdLoadFailed +=RewardedVideoOnAdLoadFailed;
        //广告显示成功
        rewardedAd.OnAdDisplayed +=RewardedVideoOnAdDisplayed;
        //广告显示失败
        rewardedAd.OnAdDisplayFailed +=RewardedVideoOnAdDisplayFailed;
        //用户完成广告以后获得奖励
        rewardedAd.OnAdRewarded +=RewardedVideoOnAdRewarded;
        //广告关闭
        rewardedAd.OnAdClosed +=RewardedVideoOnAdClosed;
        //广告点击
        rewardedAd.OnAdClicked +=RewardedVideoOnAdClicked;
        //广告信息发生变化
        rewardedAd.OnAdInfoChanged +=RewardedVideoOnAdInfoChanged;
    }

    //加载 Rewarded Video
    public void LoadRewardedVideo()
    {
        if (!isInitialized)
        {
            Debug.LogWarning("LevelPlay 还没有初始化完成，暂时不能加载 Rewarded Video。" );
            return;
        }

        if (rewardedAd == null)
        {
            Debug.LogWarning("Rewarded Video 广告对象还没有创建。");
            return;
        }

        //调用 SDK 接口,向 LevelPlay 请求加载广告
        rewardedAd.LoadAd();
    }


    //显示 Rewarded Video,播放广告，不带奖励回调
    public void ShowRewardedVideo()
    {
        //没有初始化完成
        if (!isInitialized)
        {
            Debug.LogWarning("LevelPlay 还没有初始化完成，不能播放 Rewarded Video。");
            return;
        }

        //广告对象不存在
        if (rewardedAd == null)
        {
            Debug.LogWarning("Rewarded Video 广告对象不存在。");
            return;
        }

        //检查广告是否已经准备好
        if (!rewardedAd.IsAdReady())
        {
            Debug.LogWarning("Rewarded Video 还没有准备好。");
            //顺便重新请求一次广告
            LoadRewardedVideo();
            return;
        }

        //播放 Rewarded Video
        rewardedAd.ShowAd();
    }

    //显示 Rewarded Video,并且记录广告播放完成以后需要执行的事件
    public void ShowRewardedVideo(Action videoWatchedAction)
    {
        //先保存广告完成以后要执行的事件
        this.videoWatchedAction =videoWatchedAction;
        //开始播放广告
        ShowRewardedVideo();
    }

    //判断 Rewarded Video 当前是否已经准备好,UI 看广告按钮，广告没加载好的时候直接置灰按钮
    public bool IsRewardedVideoAvailable()
    {
        if (!isInitialized)
        {
            return false;
        }

        if (rewardedAd == null)
        {
            return false;
        }
        return rewardedAd.IsAdReady();
    }

    //Rewarded Video 加载成功
    //adInfo 里面是广告信息（广告源、竞价信息等）
    private void RewardedVideoOnAdLoaded(LevelPlayAdInfo adInfo)
    {
        Debug.Log( "Rewarded Video 加载成功。");
    }

    //Rewarded Video 加载失败
    private void RewardedVideoOnAdLoadFailed(LevelPlayAdError error)
    {
        Debug.LogWarning("Rewarded Video 加载失败："+ error.ErrorMessage);
    }

    //Rewarded Video 成功显示
    private void RewardedVideoOnAdDisplayed(LevelPlayAdInfo adInfo)
    {
        Debug.Log("Rewarded Video 已经显示。");
    }

    //Rewarded Video 显示失败
    private void RewardedVideoOnAdDisplayFailed(LevelPlayAdInfo adInfo, LevelPlayAdError error)
    {
        Debug.LogError("Rewarded Video 播放失败：" + error);
    }

    //Rewarded Video 奖励回调,只有真正获得广告奖励以后才执行这里
    private void RewardedVideoOnAdRewarded(LevelPlayAdInfo adInfo,LevelPlayReward reward)
    {
        //Debug.Log("Rewarded Video 奖励成功。");

        //广告真正获得奖励以后,执行之前保存的事件
        //例如 RewardedVideoButton 传进来的ActivityManager.ResetMessages()
        videoWatchedAction?.Invoke();//如果 videoWatchedAction 不为空，就执行存进去的奖励方法

        //执行完成以后清空事件,防止下一次播放广告的时候重复执行上一次事件
        videoWatchedAction = null;
    }

    //Rewarded Video 关闭
    private void RewardedVideoOnAdClosed(LevelPlayAdInfo adInfo)
    {
        Debug.Log("Rewarded Video 已关闭。");

        //广告关闭以后,继续提前加载下一条广告,这样下一次点击的时候不用重新等待
        LoadRewardedVideo();
    }

    //Rewarded Video 被点击
    private void RewardedVideoOnAdClicked(LevelPlayAdInfo adInfo)
    {
        Debug.Log("Rewarded Video 被点击。");
    }

    //Rewarded Video 广告信息发生变化
    private void RewardedVideoOnAdInfoChanged(LevelPlayAdInfo adInfo)
    {
        Debug.Log("Rewarded Video 广告信息发生变化。");
    }
}