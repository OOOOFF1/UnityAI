using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;
using UnityEngine.Localization.Settings;//Unity 官方本地化（多语言）系统，切换中英文等
using System.IO;

public class ActivityManager : MonoBehaviour
{
    //单例：静态实例，别的脚本可以直接 ActivityManager.instance 访问这个脚本，不用拖拽赋值
    public static ActivityManager instance;

    [Header("Elements")]
    [SerializeField] private TextMeshProUGUI messagesSentText;
    [SerializeField] private TextMeshProUGUI messagesLeftText;
    [SerializeField] private TextMeshProUGUI usageText;
    [SerializeField] private Image usageFillImage;//进度条填充图片（填充amount控制长短）

    [Header("Data")]
    [SerializeField] private int freeMessagesCount;
    [SerializeField] private int rewardedMessagesCount = 5;

    private int messagesSent;
    private int messagesLeft;

    //本地存储Key：存PlayerPrefs用，相当于文件里的变量名，不能乱改,PlayerPrefs 是 Unity 本地存档，存储数据靠字符串 Key 标记
    //const：常量 →写死不能改的变量，整个运行期间不能赋值修改
    //messagesSentKey（左边，无引号）：变量名，用来代替后面那串文字
    //"messagesSent"（右边，有引号）：真正传给 PlayerPrefs 存档用的标签名字，固定文字
    //以后想改存档标签，只需要改这一行右边引号里的文字，下面所有代码自动生效，不会拼写出错
    private const string messagesSentKey = "messagesSent";
    private const string messagesLeftKey = "messagesLeft";

    //记录上一次游戏使用的免费消息总数
    //作用：如果老师或者自己把 freeMessagesCount 从10改成20，程序可以发现免费消息总数发生变化，然后重新初始化消息次数
    private const string freeMessagesCountKey = "freeMessagesCount";

    private void Awake()
    {
        //修复完整单例判断
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
        DontDestroyOnLoad(gameObject);//DDOL，切换场景不销毁这个物体，存档数据一直保留

        //消息发送事件。DiscussionManager 发送一条消息，自动触发 MessageSentCallback
        DiscussionManager.onMessageSent += MessageSentCallback;
        //Unity 本地化事件，玩家切换语言（中文 / 英文）自动触发 OnLanguageChanged
        LocalizationSettings.SelectedLocaleChanged += OnLanguageChanged;
    }


    private void OnDestroy()
    {
        DiscussionManager.onMessageSent -= MessageSentCallback;
        //取消订阅语言切换事件
        LocalizationSettings.SelectedLocaleChanged -= OnLanguageChanged;
    }

    private void Start()
    {
        LoadData();//读取 PlayerPrefs 本地存档，恢复上次关闭游戏时的消息数据
        //启动协程，等待本地化系统加载完成，再刷新 UI 文字
        //这样可以防止刚进入页面时 Localization 还没有准备好
        StartCoroutine(RefreshUIAfterLocalizationReady());
    }

    private IEnumerator RefreshUIAfterLocalizationReady()
    {
        //等待 Localization 系统初始化完成
        //LocalizationSettings.InitializationOperation：本地化系统初始化异步操作
        yield return LocalizationSettings.InitializationOperation;
        //读取完成以后刷新界面文字、进度条
        UpdateUI();
    }

    private void Update()
    {
    }

    //Locale = 区域 / 语言对象。一个 Locale 代表一门语言，例如：中文(zh‑CN)、英文(en‑US)。里面存语言 ID、显示名字、字体、地域信息
    //语言发生变化以后调用,locale 参数是当前选中的语言包
    private void OnLanguageChanged(UnityEngine.Localization.Locale locale)
    {
        //重新刷新 Activity 页面
        UpdateUI();
    }

    private void MessageSentCallback()
    {
        //再次检查剩余消息数量,防止其他地方错误调用这个事件以后，messagesLeft变成负数
        if (messagesLeft <= 0)
        {
            Debug.LogWarning("没有剩余消息次数，不能继续发送！");
            return;
        }

        messagesSent++;
        messagesLeft--;
        UpdateUI();
        SaveData();//把最新数据保存到本地，下次打开游戏还在
    }

    private void UpdateUI()
    {
        if (messagesSentText == null ||messagesLeftText == null ||usageText == null ||usageFillImage == null)
        {
            Debug.LogWarning("ActivityManager：有UI组件没有绑定。");
            return;
        }

        //总可用消息 = 已发送 + 剩余
        int totalMsg =messagesSent + messagesLeft;

        //从本地化表读取当前语言的文本,messagesSentLabel：多语言拿出来的文字标签
        string messagesSentLabel =GetLocalizedText("MessagesSent");
        string messagesLeftLabel =GetLocalizedText("MessagesLeft");

        //如果Localization没有找到对应Key,使用英文作为备用文字
        if (string.IsNullOrWhiteSpace(messagesSentLabel))
        {
            messagesSentLabel = "Messages Sent";
        }
        if (string.IsNullOrWhiteSpace(messagesLeftLabel))
        {
            messagesLeftLabel = "Messages Left";
        }

        //防止分母为0报错
        if (totalMsg <= 0)
        {
            usageFillImage.fillAmount = 0;
            usageText.text = "0%";
            messagesSentText.text =messagesSent +" " +messagesSentLabel;
            messagesLeftText.text =messagesLeft +" " +messagesLeftLabel;
            return;
        }

        //使用率 = 已经发送的消息 / 当前总共可以发送的消息
        //(float)强制转浮点：C# 整数相除默认取整，必须转 float 才能拿到小数
        float usagePercent =(float)messagesSent / totalMsg;
        //防止进度条超过0~1范围
        usagePercent =Mathf.Clamp01(usagePercent);
        //设置进度条填充比例
        usageFillImage.fillAmount =usagePercent;
        //数字不能直接赋值给 TMP 文本框,TextMeshProUGUI.text只接收文字string
        //.ToString()：把数字转换成文字字符串
        float showPercent =usagePercent * 100;
        usageText.text =showPercent.ToString("0") + "%";

        //进度颜色规则：0~50绿，50~90黄，90~100红
        if (showPercent < 50)
        {
            usageFillImage.color =new Color(0f, 0.76f, 0.55f);
        }
        else if (showPercent < 90)
        {
            usageFillImage.color =new Color(1f, 0.76f, 0.03f);
        }
        else
        {
            usageFillImage.color =new Color(0.96f, 0.25f, 0.25f);
        }

        //显示最终文字,数字由ActivityManager控制,语言文字由Localization控制
        messagesSentText.text =messagesSent +" " +messagesSentLabel;
        messagesLeftText.text =messagesLeft +" " +messagesLeftLabel;
    }

    //从Unity Localization String Table获取文字
    private string GetLocalizedText(string key)
    {
        try
        {
            //参数 1：本地化字符串表名字,参数 2：表格里面的 Key 字段
            return LocalizationSettings.StringDatabase.GetLocalizedString("UITextTable", key);
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"本地化读取失败 Key:{key} 错误:{ex.Message}");
            return "";
        }
    }


    //对外提供方法：供DiscussionManager调用，判断能不能继续发消息
    public bool CanSendMessage()
    {
        return messagesLeft > 0;//剩余条数>0 返回true，可以发送；等于0返回false禁止发送
    }

    //看完奖励广告以后增加免费消息次数
    //RewardedVideoButton会在广告成功完成以后调用这个方法
    //逻辑：在当前原有剩余消息数量基础上叠加增加条数，不是重置免费消息总条数
    public void AddRewardedMessages()
    {
        //防止Inspector里面填了0或者负数
        if (rewardedMessagesCount <= 0)
        {
            Debug.LogWarning("奖励广告增加的消息次数必须大于0。");
            return;
        }

        //增加奖励广告提供的免费消息次数
        messagesLeft += rewardedMessagesCount;
        UpdateUI();
        SaveData();//保存到PlayerPrefs
        Debug.Log("奖励广告完成！增加消息次数：" +rewardedMessagesCount +"，当前剩余：" +messagesLeft);
    }

    [NaughtyAttributes.Button]
    public void ResetMessages()
    {
        messagesLeft = freeMessagesCount;
        UpdateUI();
        SaveData();
    }

    //广告播放完成以后调用
    //【旧逻辑：看完广告直接重置免费消息总数，本次需求不再使用这个方法，改用AddRewardedMessages叠加增加】
    /*
    public void RewardedVideoWatchedCallback()
    {
        ResetMessages();
        Debug.Log("Rewarded Video 播放完成，消息次数已经恢复。");
    }
    */

    //读取本地存储数据
    private void LoadData()
    {
        //GetInt：读硬盘到脚本变量,SetInt：把脚本变量当前值复制一份存到硬盘
        //PlayerPrefs是Unity自带简易本地存档工具，作用：把数据保存在电脑/头盔本地，关闭游戏、重启项目，数据还保留
        //PlayerPrefs.GetInt(key, 默认值)：读取 int；找不到存档时返回第二个参数 - 1，用来判断是否第一次打开游戏
        //参数1 freeMessagesCountKey：字符串常量，存档的唯一名字,读取上一次保存的免费消息总数,-1代表以前没有保存过这个数据，是第一次运行
        int savedFreeMessagesCount =PlayerPrefs.GetInt(freeMessagesCountKey,-1);

        //如果以前保存的免费消息总数和现在Inspector里面设置的不一样
        //说明自己修改了freeMessagesCount，这时候需要重新开始计算免费消息次数
        if (savedFreeMessagesCount != freeMessagesCount)
        {
            Debug.Log("检测到免费消息总数发生变化：" +savedFreeMessagesCount +" → " +freeMessagesCount +"，重新初始化消息次数。");

            //重新开始统计
            messagesSent = 0;
            messagesLeft = freeMessagesCount;

            //保存现在新的免费消息总数
            PlayerPrefs.SetInt(freeMessagesCountKey,freeMessagesCount);

            //保存新的消息数据
            SaveData();
            return;
        }

        //如果免费消息总数没有变化,就继续读取之前保存的发送记录和剩余次数
        messagesSent =PlayerPrefs.GetInt(messagesSentKey,0);

        //读取保存的剩余消息次数
        //第一次打开游戏没有存档时，messagesLeft 默认读取为freeMessagesCount
        messagesLeft = PlayerPrefs.GetInt(messagesLeftKey,freeMessagesCount);
    }

    //保存数据到本地 PlayerPrefs（Unity简易本地存档）
    private void SaveData()
    {
        //找到钥匙 messagesSentKey，把后面这个数字存进去
        PlayerPrefs.SetInt(messagesSentKey,messagesSent);
        PlayerPrefs.SetInt(messagesLeftKey,messagesLeft);

        //保存当前免费消息总数
        PlayerPrefs.SetInt(freeMessagesCountKey,freeMessagesCount);

        //PlayerPrefs.SetInt只是把数据放进PlayerPrefs缓存
        //PlayerPrefs.Save()才是真正要求Unity立即把数据保存到本地
        //这样即使马上关闭游戏，最新数据也不会因为还没来得及保存而丢失
        PlayerPrefs.Save();
    }

    //手动重置消息次数
    //可以在Unity Inspector右上角的组件菜单里面执行
    //作用：测试项目的时候，可以不用删除整个PlayerPrefs

    [ContextMenu("恢复初始状态（保留 API 配置）")]
    private void ResetProjectData()
    {
        Debug.Log("开始恢复项目初始状态……");

        // 1. 重置消息次数
        messagesSent = 0;
        messagesLeft = freeMessagesCount;

        SaveData();
        UpdateUI();

        // 2. 清理自定义角色，并重新加载默认角色
        RolesManager rolesManager =FindObjectOfType<RolesManager>();

        if (rolesManager != null)
        {
            rolesManager.ResetRolesToDefaults();
        }
        else
        {
            Debug.LogWarning("没有找到 RolesManager，角色数据未重置。");
        }

        // 3. 清空聊天历史
        HistoryManager historyManager =FindObjectOfType<HistoryManager>();

        if (historyManager != null)
        {
            historyManager.ResetAllHistory();
        }
        else
        {
            Debug.LogWarning("没有找到 HistoryManager，聊天历史未重置。");
        }

        // 4. 明确保护讯飞 API 配置文件
        string xunfeiConfigPath =Path.Combine(Application.persistentDataPath,"xunfei_config.json");

        Debug.Log("重置操作未主动删除讯飞配置文件：" +xunfeiConfigPath);

        Debug.Log("项目初始状态恢复操作已执行。");
    }

}