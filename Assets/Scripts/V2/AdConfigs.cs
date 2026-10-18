using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;

//静态配置类，专门存放 LevelPlay 广告的ID，不用挂在 GameObject上，别的脚本直接 AdConfigs.xxx 就能拿值
// static 静态类：这个类不能挂物体，整个游戏全局只有一份，直接类名.属性访问
public static class AdConfigs
{
    public static string AppKey => GetAppKey();// 只读属性：广告AppKey
    public static string BannerAdUnitId =>GetBannerAdUnitId();//Banner横幅广告ID
    public static string InterstitialAdUnitId =>GetInterstitialAdUnitId();// 插屏广告ID
    public static string RewardedVideoAdUnitId =>GetRewardedVideoAdUnitId();// 激励视频广告ID

    // #if 条件编译：编译的时候判断当前是什么平台，只编译对应平台的代码.打包编译阶段就生效
    private static string GetAppKey()
    {
#if UNITY_ANDROID// 如果当前打包是安卓，执行这行
        return "25b63cf85";

#elif UNITY_IOS// 如果当前打包是苹果iOS，执行这行
        return "25c43a4a5";

#else// 其他平台（编辑器、Windows）返回这个字符串
        return "unexpected_platform";
#endif
    }

    // 获取横幅广告ID
    private static string GetBannerAdUnitId()
    {
#if UNITY_ANDROID
        return "4fpetq4lhe5lsw3e";

#elif UNITY_IOS
        return "xc2bsuntn9ea734t";

#else
        return "unexpected_platform";

#endif
    }

    // 获取插屏广告ID
    private static string GetInterstitialAdUnitId()
    {
#if UNITY_ANDROID
        return "h3xw38h9214adgxo";

#elif UNITY_IOS
        return "obg6ohwts3y690ks";

#else
        return "unexpected_platform";
#endif
    }

    // 获取激励视频ID
    private static string GetRewardedVideoAdUnitId()
    {
#if UNITY_ANDROID
        return "syz3d8ekts22q0or";

#elif UNITY_IOS
        return "l1quzz1xmmdhw5er";

#else
        return "unexpected_platform";
#endif
    }
}