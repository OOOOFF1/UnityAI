using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using System;
using NUnit.Framework;
/*
using OpenAI;//OpenAI插件基础
using OpenAI.Chat;//OpenAI聊天相关功能
using OpenAI.Audio;
using OpenAI.Images;
*/
public class DiscussionManager : MonoBehaviour
{
    //单例：全局唯一，别的脚本直接 DiscussionManager.instance 访问，不用拖拽
    public static DiscussionManager instance;

    [Header("Elements")]
    [SerializeField] private DiscussionBubble bubblePrefab;
    [SerializeField] private TMP_InputField inputField;
    //拿 Transform，本质就是拿到一个物体，专门用来做父物体，往下面生成子物体
    [SerializeField] private Transform bubblesParent;//持有所有讨论气泡

    // 事件：onMessageReceived 相当于一个广播喇叭,CreateBubble执行完之后，就按这个喇叭喊一声
    //Action：无参数、无返回值的委托，可以存一个 / 多个函数
    //static → 静态，不需要挂物体、不需要拿组件，别的脚本直接 脚本名.onMessageReceived 就能访问
    [Header("Events")]
    public static Action onMessageReceived;
    public static Action onMessageSent;
    public static Action onTryingToSendMessagesButCant;
    public static Action<string> onAIMessageReceived;

    /*
    //身份验证
    [Header("Authentication")]
    [SerializeField] private string[] apiKey;
    [SerializeField] private string[] organizationId;
    //OpenAI 客户端对象，相当于电脑和 OpenAI 服务器之间的通道，所有发消息都靠它
    private OpenAIClient api;
    */

    [Header("Ollama")]
    [SerializeField] private OllamaManager ollamaManager;

    //内存保存当前整段对话全部消息：用户消息 / AI回复 / 角色人设Prompt
    private List<ChatGPTxUnity.Message> chatMessages =new List<ChatGPTxUnity.Message>();
    
    private void Awake()
    {
        //单例标准写法：保证场景只会有一个DiscussionManager
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
    }

    private void OnEnable()
    {
        //选择角色按钮被点击
        RoleContainerUI.onButtonClicked += RoleButtonClickedCallback;
        //历史会话列表按钮点击，读取旧聊天记录
        HistoryMessageContainerUI.onHistoryButtonClicked += LoadHistoryConversationCallback;
        //Ollama拿到AI回答之后触发，执行AIReply
        OllamaManager.OnResponseReceived += AIReply;
    }

    private void OnDisable()
    {
        RoleContainerUI.onButtonClicked -= RoleButtonClickedCallback;
        HistoryMessageContainerUI.onHistoryButtonClicked -= LoadHistoryConversationCallback;
        OllamaManager.OnResponseReceived -= AIReply;
    }

    void Start()
    {
        // ❗删除错误GetComponent，跨物体无法获取OllamaManager，必须Inspector拖拽赋值
        if (ollamaManager == null)
        {
            Debug.LogError("DiscussionManager: OllamaManager 没有绑定！请在Inspector拖拽赋值");
            return;
        }

        //CreateBubble("Hey There! How can I help you?", false);
        //订阅事件：监听 OllamaManager 的 OnResponseReceived
        //含义：当Ollama拿到AI回答文字之后，自动执行这个函数 AIReply(message)
        //OllamaManager.OnResponseReceived += AIReply;
        /*
        //执行身份验证，把密钥交给 Ollama / OpenAI，建立连接
        Authenticate();
        */
    }

    // 用户在角色选择界面点击某一个AI角色按钮，触发这个回调
    private void RoleButtonClickedCallback(ChatGPTxUnity.Role role)
    {
        if (role == null)
        {
            Debug.LogWarning("RoleButtonClickedCallback role为空");
            return;
        }

        //选择新的角色以后，说明接下来是新的聊天,不能继续修改之前打开的历史Conversation
        if (HistoryManager.instance != null)
        {
            HistoryManager.instance.ClearCurrentConversation();
        }
        Debug.Log("选择角色：" + role.title);
     
        ClearAllBubbles(); //清空聊天界面的旧气泡
        //清空DiscussionManager里面保存的旧聊天记录,否则切换角色以后，GetMessages()还会拿到上一个角色的聊天内容
        chatMessages.Clear();

        //把当前角色的人设保存成第一条消息
        //因为History里面没有单独的System类型,所以这里用isUser=false来保存角色Prompt
        chatMessages.Add(new ChatGPTxUnity.Message(false,role.firstPrompt));

        //把人设交给Ollama，告诉大模型现在要用这个角色说话
        if (ollamaManager != null)
        {
            ollamaManager.SetRole(role.firstPrompt);
            Debug.Log("AI角色已经设置：" + role.firstPrompt);
        }
        else
        {
            Debug.LogError("DiscussionManager: OllamaManager 没有绑定！");
        }

        //切到聊天UI页面
        if (UIManager.instance != null)
        {
            Debug.Log("准备切换到聊天界面");
            UIManager.instance.ShowChat();
        }
        else
        {
            Debug.LogError("UIManager.instance 为空，无法打开聊天界面！");
        }
    }
 
    //回调函数：Ollama拿到AI回复后，会调用这个函数，message就是AI回答的文字
    private void AIReply(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            Debug.LogWarning("DiscussionManager: AI返回了空文字。");
            return;
        }
        
        CreateBubble(message,false);//显示 AI 回复气泡

        //保存AI回复到当前聊天记录
        //这样点击返回菜单以后，HistoryManager才能把AI的回答一起保存下来
        chatMessages.Add(new ChatGPTxUnity.Message(false,message));

        //把 AI 回复广播给其他系统
        onAIMessageReceived?.Invoke(message);
    }

    /*private void Authenticate()
    {
        //`new OpenAIClient(...)`创建连接通道
        //拿着你的钥匙，登录 OpenAI 服务器
        //OpenAIClient只接收一个类型为OpenAIAuthentication的对象
        //类似于 OpenAIAuthentication = 一张完整的通行证卡片
        //OpenAIClient（电话）只接受整张卡片
        api = new OpenAIClient(new OpenAIAuthentication(apiKey[0], organizationId[0]));
    }*/

    // 加载历史聊天会话回调：点击历史记录条目执行
    private void LoadHistoryConversationCallback(Conversation conversation)
    {
        if (conversation == null)
        {
            Debug.LogWarning("LoadHistoryConversationCallback conversation为空");
            return;
        }

        //设置当前正在使用的会话
        if (HistoryManager.instance != null)
        {
            HistoryManager.instance.SetCurrentConversation(conversation);
        }
     
        chatMessages.Clear();//清空当前界面的旧聊天记录        
        ClearAllBubbles();//清空当前聊天界面的旧气泡

        //把历史对话喂给Ollama，让大模型知道上下文历史
        if (ollamaManager != null)
        {
            ollamaManager.LoadHistory(conversation.messages);
        }

        //循环遍历历史所有消息，重建UI气泡
        if (conversation.messages != null)
        {
            for (int i = 0;i < conversation.messages.Length;i++)
            {
                ChatGPTxUnity.Message message =conversation.messages[i];
                //保存到当前聊天记录
                chatMessages.Add(new ChatGPTxUnity.Message(message.isUser,message.content));

                //第一条是角色Prompt，所以不显示成聊天气泡
                if (i == 0)
                {
                    continue;
                }
                CreateBubble(message.content,message.isUser);
            }
        }

        //加载完历史会话，跳转到聊天界面
        if (UIManager.instance != null)
        {
            Debug.Log("加载历史会话，切换聊天界面");
            UIManager.instance.ShowChat();
        }
        else
        {
            Debug.LogError("UIManager.instance 为空，无法打开聊天界面！");
        }
    }

    // 【外部调用入口】用户点击发送按钮（AskButtonManager）调用这个函数，发送用户提问
    public void AskButtonCallback()
    {
        //检查 ActivityManager 是否存在
        //ActivityManager负责统计用户还剩多少条免费消息
        if (ActivityManager.instance == null)
        {
            Debug.LogError("DiscussionManager: ActivityManager 没有找到！");
            return;
        }

        //判断剩余消息次数不足，触发事件，弹出广告激励视频
        if (!ActivityManager.instance.CanSendMessage())
        {
            onTryingToSendMessagesButCant?.Invoke();
            return;
        }

        //先保存用户输入
        string userMessage =inputField.text;
        //如果什么都没输入，就不发送
        if (string.IsNullOrWhiteSpace(userMessage))
        {
            return;
        }
        
        CreateBubble(userMessage,true);//显示用户消息气泡

        //把用户消息保存到当前聊天记录,这样HistoryManager保存的时候，用户说的话也会被保存
        chatMessages.Add(new ChatGPTxUnity.Message(true,userMessage));

        //广播消息发送事件,告诉 ActivityManager：用户刚刚成功发送了一条消息
        onMessageSent?.Invoke();
        
        inputField.text = "";//清空输入字段并将文本设置为空字符串

        //调用OllamaManager里面的Chat函数，把用户问题传过去
        //Chat内部会自己启动网络协程,这里不能再使用StartCoroutine，因为Chat本身是void
        if (ollamaManager != null)
        {
            ollamaManager.Chat(userMessage);
        }
        else
        {
            Debug.LogError("DiscussionManager: OllamaManager 没有绑定！");
        }

        /*
        //专门装Message对象（一条条聊天消息），< >尖括号里面写类型
        List <Message> prompts = new List<Message>();
        prompts.Add(new Message(Role.User,userMessage));
        //ChatRequest是发给 OpenAI 服务器的完整数据包
        ChatRequest request = new ChatRequest(messages: prompts,model: OpenAI.Models.Model.GPT3_5_Turbo,
            //温度 0~2。越小AI回答越严谨、固定；越大AI越爱瞎编、脑洞大
            temperature: 0.2);
        try
        {
            //ChatEndpoint类似于电话上面的一个聊天功能按键模块
            //.ChatEndpoint → 专门管聊天功能
            //.ImageEndpoint → 专门管AI画图
            //GetCompletionAsync：等待OpenAI返回结果
            var result = await api.ChatEndpoint.GetCompletionAsync(request);
            CreateBubble(result.FirstChoice.ToString(), false);
        }
        catch (Exception e)
        {
            Debug.Log(e);
        }
        */
    }

    // 实例化一个聊天气泡预制体，放到Content下面，初始化UI，抛出onMessageReceived事件
    private void CreateBubble(string message,bool isUserMessage)
    {
        if (bubblePrefab == null ||bubblesParent == null)
        {
            Debug.LogError("DiscussionManager: bubblePrefab 或 bubblesParent 没有绑定！");
            return;
        }

        //在气泡父下实例气泡预制体
        DiscussionBubble discussionBubble =Instantiate(bubblePrefab,bubblesParent);
        
        //配置这条气泡
        discussionBubble.Configure(message,isUserMessage);

        //触发事件：新消息气泡生成完毕！
        //ContentAutoScroll订阅这个事件，收到就协程滚动聊天框到底部
        onMessageReceived?.Invoke();
    }

    /*
    private IEnumerator NotifyMessageReceivedNextFrame()
    {
        yield return null;
        Canvas.ForceUpdateCanvases();
        onMessageReceived?.Invoke();
    }
    private void OnReceiveMessage(string response)
    {
        Debug.Log(response);
    }
    */

    // 清空全部聊天气泡
    // 逻辑：把每一个子物体先脱离父物体，再Destroy销毁.直接Destroy子物体有时候Unity内部报错，所以先SetParent(null)
    private void ClearAllBubbles()
    {
        while (bubblesParent.childCount > 0)
        {
            Transform firstChild =bubblesParent.GetChild(0);
            //firstChild.SetParent(bubblesParent);把 firstChild 放进 bubblesParent 下面，变成它的子物体
            //firstChild.SetParent(null);脱离原来的父物体，变成没有父物体的独立物体（挂在场景根层级）
            firstChild.SetParent(null);
            Destroy(firstChild.gameObject);
        }
    }

    // 对外接口，把内存List<Message>转为数组，提供给HistoryManager保存会话到本地存档
    public ChatGPTxUnity.Message[] GetMessages()
    {
        //HistoryManager保存历史记录时使用的是Message[],只接收数组 Message[]，不接收 List
        //chatMessages.Count：List 里面一共有多少条聊天消息
        //new ChatGPTxUnity.Message[chatMessages.Count]：新建一个数组，数组长度 = List 里面消息的总条数
        ChatGPTxUnity.Message[] messages =new ChatGPTxUnity.Message[chatMessages.Count];
        for (int i = 0;i < chatMessages.Count;i++)
        {
            messages[i] =new ChatGPTxUnity.Message(chatMessages[i].isUser,chatMessages[i].content);
        }
        return messages;
    }
}
