using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

//把每一段完整聊天会话保存到手机/电脑本地磁盘文件History.txt
public class HistoryManager : MonoBehaviour
{
    //单例，全局访问 HistoryManager.instance
    public static HistoryManager instance;

    [Header("Elements")]
    [SerializeField] private Transform historyMessagesParent;//历史列表Content，预制体生成到这个物体下面
    [SerializeField] private HistoryMessageContainerUI historyMessagePrefab;//历史会话条目预制体

    [Header("Data")]
    private History history;//内存里全部历史会话的总容器
    private string dataPath;//本地存档文件完整路径
    private bool _isSavingHistory; //存档写入锁：防止同时多次写文件造成损坏

    //记录当前正在操作的会话
    //如果点开旧历史聊天，currentConversation就指向这个旧会话；返回时更新旧会话，而不是新建
    private Conversation currentConversation;

    private void Awake()
    {
        //单例逻辑
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
        DontDestroyOnLoad(gameObject);//切换场景，这个管理器不销毁，历史数据保留在内存

        //拼接本地文件路径
        //Application.persistentDataPath：Unity提供的读写目录，可以存存档，跨平台兼容Windows/安卓
        dataPath = Application.persistentDataPath +"/History.txt";

        LoadData();//游戏启动，读取本地历史文件加载到内存

        //订阅事件：从聊天页面点击返回按钮的时候触发 TrySaveConversation() 保存聊天
        UIManager.onBackFromChat += TrySaveConversation;
    }


    private void OnDestroy()
    {
        UIManager.onBackFromChat -= TrySaveConversation;//取消事件订阅
    }


    private void Start()
    {
        Initialize();//初始化，刷新历史列表UI0
    }

    // 初始化/刷新历史列表UI
    // 1.会话按时间排序 2.清空旧UI条目 3.遍历所有会话，实例化预制体生成每一条历史条目
    private void Initialize()
    {
        SortConversations();//先排序，最新聊天排在最上面

        //销毁全部旧的历史UI条目
        while (historyMessagesParent.childCount > 0)
        {
            Transform t = historyMessagesParent.GetChild(0);
            t.SetParent(null);
            Destroy(t.gameObject);
        }

        //循环每一段会话，生成UI条目
        for (int i = 0; i < history.conversations.Count; i++)
        {
            HistoryMessageContainerUI containerInstance =Instantiate(historyMessagePrefab,historyMessagesParent);
            
            //调用预制体脚本的Configure，把会话数据传给UI
            containerInstance.Configure(history.conversations[i]);
        }
    }

    // 设置“当前正在打开的会话”,打开旧历史记录的时候 DiscussionManager会调用这个
    public void SetCurrentConversation(Conversation conversation)
    {
        currentConversation = conversation;
    }

    // 删除某一条历史会话
    public void RemoveConversation(Conversation conversation)
    {
        history.conversations.Remove(conversation);//内存List移除这条会话
        SaveData();//删除历史记录以后重新保存数据     
        UpdateScrollView();//重新刷新历史记录UI
    }

    private void UpdateScrollView()
    {
        Initialize();//刷新历史UI界面，复用初始化逻辑
    }

    //用户从聊天界面点返回，触发此函数，保存当前聊天
    public void TrySaveConversation()
    {
        //调用DiscussionManager拿到当前内存聊天消息数组 Message[]
        ChatGPTxUnity.Message[] messages =DiscussionManager.instance.GetMessages();
        
        //聊天消息少于2条不保存（只有角色prompt，没有实际对话，不生成历史）
        if (messages.Length < 2)  return;

        //拷贝一份消息数组，防止外部篡改
        ChatGPTxUnity.Message[] myMessages =new ChatGPTxUnity.Message[messages.Length];
        for (int i = 0; i < myMessages.Length; i++)
        {
            myMessages[i] = new ChatGPTxUnity.Message(messages[i].isUser,messages[i].content);
        }

        //获取当前时间
        System.DateTime now = System.DateTime.Now;
        
        //如果打开的是旧聊天，就更新原来的记录，不新建
        if (currentConversation != null)
        {
            currentConversation.messages = myMessages;//覆盖更新聊天内容
            currentConversation.date =now.ToShortDateString();//短日期，UI展示用
            currentConversation.timestamp =now.ToString("O");//高精度完整时间，用来排序
        }
        else
        {
            //如果是第一次聊天，就创建新的历史记录
            Conversation conversation = new Conversation();
            conversation.date =now.ToShortDateString();
            //保存完整聊天时间，O格式会保存年月日、时、分、秒以及更精确的时间
            conversation.timestamp =now.ToString("O");
            conversation.messages = myMessages;
            history.conversations.Add(conversation);//加入总历史列表
            currentConversation = conversation;//标记现在操作的是这个新会话
        }
        SortConversations();//保存完重新排序
        SaveData();//写入本地txt存档
        UpdateScrollView();//刷新历史列表UI
    }

    // 读取本地 History.txt，加载历史数据进内存
    private void LoadData()
    {
        // 确保使用类成员 dataPath，而不是创建同名局部变量
        if (string.IsNullOrWhiteSpace(dataPath))
        {
            dataPath = Path.Combine(Application.persistentDataPath,"History.txt");
        }

        try
        {
            if (!File.Exists(dataPath))
            {
                history = new History();
                SaveData();
                return;
            }

            string data = File.ReadAllText(dataPath);
            history = JsonUtility.FromJson<History>(data);

            if (history == null || history.conversations == null)
            {
                history = new History();
                SaveData();
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"History 文件读取失败，将使用空历史：{ex.Message}");

            history = new History();
        }
    }

    // 把内存history对象序列化为Json，写入本地History.txt
    // _isSavingHistory 写入锁：防止短时间多次调用SaveData，同时写文件造成文件损坏
    // 把内存中的历史记录保存为 JSON
    private void SaveData()
    {
        if (_isSavingHistory)
        {
            Debug.LogWarning("History 正在保存，跳过重复写入。");
            return;
        }

        _isSavingHistory = true;

        try
        {
            // 防止路径没有初始化
            if (string.IsNullOrWhiteSpace(dataPath))
            {
                dataPath = Path.Combine(Application.persistentDataPath,"History.txt");
            }

            // 防止历史容器或列表为空
            if (history == null)
            {
                history = new History();
            }

            if (history.conversations == null)
            {
                history.conversations = new List<Conversation>();
            }

            // 确保目标目录存在
            string directory = Path.GetDirectoryName(dataPath);

            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string json = JsonUtility.ToJson(history, true);
            File.WriteAllText(dataPath, json);

            Debug.Log("History 存档写入成功：" + dataPath);
        }
        catch (Exception ex)
        {
            Debug.LogError($"History 存档写入失败：{ex}");
        }
        finally
        {
            _isSavingHistory = false;
        }
    }

    // 会话排序：最新聊天排在列表最上方
    // history.conversations.Sort 自定义排序lambda表达式
    private void SortConversations()
    {
        history.conversations.Sort((a, b) =>
            {
                //a、b代表List里面任意两个Conversation会话
                System.DateTime timeA;
                System.DateTime timeB;
                //尝试解析a会话的高精度时间戳timestamp到timeA变量
                //TryParse：解析成功返回true，失败返回false；out把解析出来时间放进timeA
                if (!System.DateTime.TryParse(a.timestamp, out timeA))
                {
                    //解析失败！就降级用旧的date字段去解析
                    System.DateTime.TryParse(a.date, out timeA);
                }
                if (!System.DateTime.TryParse(b.timestamp, out timeB))
                {
                    System.DateTime.TryParse(b.date, out timeB);
                }
                //CompareTo比较两个时间，最新时间排前面
                return timeB.CompareTo(timeA);
            }
        );
    }

    // 清空当前会话标记；切换新角色的时候DiscussionManager调用
    // currentConversation=null →下次保存就会新建会话，而不是更新旧会话
    public void ClearCurrentConversation()
    {
        //切换角色必须置 null，不然你新聊的内容会覆盖上一次旧历史
        currentConversation = null; //currentConversation 为 null → 创建全新会话
    }


    /// <summary>
    /// 清空所有聊天历史，并保存空历史文件。
    /// </summary>
    public void ResetAllHistory()
    {
        // 清空内存中的所有历史会话
        history = new History();

        // 取消当前会话关联，防止继续覆盖旧会话
        currentConversation = null;

        // 保存空历史
        SaveData();

        // 刷新历史列表 UI
        UpdateScrollView();

        Debug.Log("HistoryManager：已执行清空历史操作。");
    }
}

//只有标记 [System.Serializable] 的类，Unity 才能序列化存 Json
[System.Serializable]
public class History//类，声明一个新种类，名字叫 History
{
    //全部聊天会话集合
    public List<Conversation> conversations;
    public History()//构造函数，new 这个对象那一刻自动运行，名字强制必须跟类名一样
    {
        conversations = new List<Conversation>();
    }
}

[System.Serializable]
// 一段完整聊天会话：一次聊天全部消息+时间
public class Conversation
{
    public string date;//短日期，UI显示用，例如"2026/10/3"
    public string timestamp;//高精度完整时间字符串，用来排序
    public ChatGPTxUnity.Message[] messages;//这一段会话的所有消息数组
}

namespace ChatGPTxUnity
{
    // 单条消息：是否用户消息、文本内容
    [System.Serializable]
    public class Message
    {
        public bool isUser;
        public string content;
        public Message(bool isUser, string content)
        {
            this.isUser = isUser;
            this.content = content;
        }
    }
}
