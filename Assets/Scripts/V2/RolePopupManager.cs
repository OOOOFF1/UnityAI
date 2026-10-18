using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;
using UnityEngine.Localization.Settings;

public class RolePopupManager : MonoBehaviour
{
    public static RolePopupManager instance;

    [Header("Elements")]
    [SerializeField] private GameObject popup;//弹窗根物体

    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI messageText;
    [SerializeField] private TMP_InputField inputField;
    [SerializeField] private TMP_InputField secondInputField;
    [SerializeField] private TextMeshProUGUI inputLabel1;
    [SerializeField] private TextMeshProUGUI inputLabel2;

    [SerializeField] private GameObject inputNameGroup;
    [SerializeField] private GameObject inputPromptGroup;
    //配合LayoutRebuilder.ForceRebuildLayoutImmediate(containerRect)，强制刷新 UGUI 布局
    //因为我们代码动态显示/隐藏 Group，UGUI 不会自动刷新，会出现 UI 挤压、错位，需要手动强制重建布局
    [SerializeField] private RectTransform containerRect;//弹窗内容区域的 RectTransform

    [SerializeField] private Button cancelButton;
    [SerializeField] private Button confirmButton;
    [SerializeField] private Button secondaryButton;
    [SerializeField] private TextMeshProUGUI confirmButtonText;
    [SerializeField] private TextMeshProUGUI secondaryButtonText;
    [SerializeField] private TextMeshProUGUI cancelButtonText;

    [Header("References")]
    [SerializeField] private RolesManager rolesManager;

    //Action：可以存一段要执行的代码逻辑（函数），暂时不运行，等用户点确认按钮才运行
    private System.Action confirmAction;
    private System.Action secondaryAction;

    //枚举类型，记录：现在这个弹窗是哪一种弹窗
    private PopupType currentPopupType =PopupType.None;

    //保存当前弹窗正在操作的那个分类的数据对象
    private RoleCategoryRuntimeData currentCategory;
    //保存：当前弹窗操作的角色对象
    private RoleRuntimeData currentRole;
    //保存：当前弹窗操作的历史会话对象
    private Conversation currentConversation;

    //枚举：标记现在打开的弹窗是哪一种
    private enum PopupType
    {
        None,//没有弹窗
        AddCategory,//新建分类
        Category,//编辑已有分类（删除分类、添加角色）
        Role,//删除角色
        AddRole,//新增角色
        HistoryDelete//删除历史会话
    }

    private void Awake()
    {
        if (instance != null && instance != this)//单例
        {
            Destroy(gameObject);
            return;
        }
        instance = this;

        popup.SetActive(false);//游戏启动，弹窗默认隐藏

        //给三个按钮绑定点击回调
        cancelButton.onClick.AddListener(HidePopup);
        confirmButton.onClick.AddListener(ConfirmCallback);
        secondaryButton.onClick.AddListener(SecondaryCallback);

        //监听语言切换，弹窗打开的时候切语言，弹窗文字会自动刷新
        LocalizationSettings.SelectedLocaleChanged -= OnLanguageChanged;
        LocalizationSettings.SelectedLocaleChanged += OnLanguageChanged;
    }

    private void OnDestroy()
    {
        //取消监听语言切换
        LocalizationSettings.SelectedLocaleChanged -=OnLanguageChanged;
    }

    //获取UITextTable里的文字,本地化获取文字
    private string GetLocalizedText(string key)
    {
        try
        {
            //StringDatabase：字符串数据库，存放所有多语言文本表格
            //从本地化表格 UITextTable 根据key拿对应语言文字
            string text =LocalizationSettings.StringDatabase.GetLocalizedString("UITextTable",key);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("获取本地化文字失败，Key：" +key +"，错误：" +e.Message);
        }
        //如果本地化系统没加载完成、报错，返回兜底英文
        return GetEnglishText(key);
    }

    //本地化文字获取失败时使用英文
    private string GetEnglishText(string key)
    {
        switch (key)
        {
            case "AddCategory":
                return "Add Category";
            case "PleaseEnterCategoryName":
                return "  Please enter the new role category name.  ";
            case "RoleCategoryName":
                return "Role Category Name:";
            case "Add":
                return "Add";
            case "PleaseSelectOperation":
                return "  Please select an operation.  ";
            case "Delete":
                return "Delete";
            case "AddRole":
                return "Add  Role";
            case "DeleteRole":
                return "Delete this role?";
            case "RoleName":
                return "Role Name:";
            case "RolePrompt":
                return "Role Prompt:";
            case "AddRoleMessage":
                return "  Please enter the role name and role prompt.  ";
            case "DeleteMessage":
                return "Delete Message";
            case "DeleteConversationConfirm":
                return " Are you sure you want to delete this conversation? ";
            case "Cancel":
                return "Cancel";
            default:
                return key;
        }
    }

    //长按页面顶部标题，新增分类
    public void ShowAddCategoryPopup()
    {
        //设置弹窗类型，清空上一次弹窗残留的数据
        currentPopupType = PopupType.AddCategory;
        currentCategory =null;
        currentRole =null;
        currentConversation =null;

        popup.SetActive(true);//显示弹窗

        //设置弹窗文字，拿多语言
        titleText.text =GetLocalizedText("AddCategory");
        messageText.text =GetLocalizedText("PleaseEnterCategoryName");

        //控制输入框组显隐：只显示名称输入框，隐藏Prompt输入框
        inputNameGroup.SetActive(true);
        inputPromptGroup.SetActive(false);

        inputLabel1.text =GetLocalizedText("RoleCategoryName");
        inputField.text ="";//清空输入框旧文字
        inputField.ActivateInputField();//自动激活输入框，光标直接跳进去

        confirmButtonText.text =GetLocalizedText( "Add");
        cancelButtonText.text = GetLocalizedText("Cancel");
        secondaryButton.gameObject.SetActive(false);//不需要第二个按钮，隐藏

        //把确认要执行的函数存入confirmAction
        confirmAction = AddCategoryCallback;
        secondaryAction =null;

        //LayoutRebuilder：UGUI 布局重建工具
        //ForceRebuildLayoutImmediate(containerRect):立刻、马上强制手动重新计算这一块RectTransform下面所有Layout布局，同步刷新 UI，不等到下一帧
        //强制刷新UGUI布局，防止UI错乱挤压
        LayoutRebuilder.ForceRebuildLayoutImmediate(containerRect);
    }

    //长按某一个已经存在的分类
    public void ShowCategoryPopup(RoleCategoryRuntimeData category)
    {
        currentPopupType =PopupType.Category;
        currentCategory =category;//记住现在操作的是哪一个分类
        currentRole =null;
        currentConversation =null;

        popup.SetActive(true);

        //这里显示当前语言的 Category 名字
        //category.GetDisplayTitle():category是运行时用户自己创建的数据（用户新建的分类、角色）
        //这个文字是用户输入的！不是写死在本地化表格
        titleText.text =category != null? category.GetDisplayTitle(): "";
        messageText.text =GetLocalizedText("PleaseSelectOperation");

        inputNameGroup.SetActive(false);
        inputPromptGroup.SetActive(false);//全部输入框隐藏，这个弹窗不需要输入

        //GetLocalizedText(key)读取写死在本地化表格里固定文本
        confirmButtonText.text =GetLocalizedText("Delete");
        cancelButtonText.text =GetLocalizedText("Cancel");
        secondaryButtonText.text =GetLocalizedText("AddRole");
        secondaryButton.gameObject.SetActive(true);//打开第二个按钮

        //lambda表达式：直接写一段代码存进Action，不用额外写函数
        confirmAction = () =>
        {
            //点确认：删除当前这个分类
            if (rolesManager != null &&currentCategory != null)
            {
                rolesManager.DeleteCategory(currentCategory);
            }
            HidePopup();
        };
        secondaryAction = () =>
        {
            //点第二个按钮，直接在当前弹窗切换到新增角色弹窗
            if (currentCategory != null)
            {
                ShowAddRolePopup(currentCategory);
            }
        };
        LayoutRebuilder.ForceRebuildLayoutImmediate(containerRect);
    }

    //长按角色，删除角色
    public void ShowRolePopup(RoleRuntimeData role,RoleCategoryRuntimeData category)
    {
        currentPopupType =PopupType.Role;
        currentRole =role;//记住要删除的角色
        currentCategory =category;//记住角色属于哪个分类
        currentConversation =null;

        popup.SetActive(true);

        //这里显示当前语言的 Role 名字
        titleText.text =role != null? role.GetDisplayTitle(): "";
        messageText.text =GetLocalizedText("DeleteRole");

        //全部输入框隐藏，只做确认删除
        inputNameGroup.SetActive(false);
        inputPromptGroup.SetActive(false);

        confirmButtonText.text =GetLocalizedText("Delete");
        cancelButtonText.text =GetLocalizedText("Cancel");
        secondaryButton.gameObject.SetActive(false);

        confirmAction = () =>
        {
            //调用管理器删除角色
            if (rolesManager != null &&currentCategory != null &&currentRole != null)
            {
                rolesManager.DeleteRole(currentCategory,currentRole);
            }
            HidePopup();
        };
        secondaryAction =null;

        LayoutRebuilder.ForceRebuildLayoutImmediate(containerRect);
    }

    //新增角色
    private void ShowAddRolePopup(RoleCategoryRuntimeData category)
    {
        currentPopupType =PopupType.AddRole;
        currentCategory =category;//记住新增的角色放在哪个分类下面
        currentRole =null;
        currentConversation =null;

        popup.SetActive(true);

        //两个输入框全部打开：角色名字、角色提示词
        titleText.text =GetLocalizedText("AddRole");
        messageText.text =GetLocalizedText("AddRoleMessage");

        inputNameGroup.SetActive(true);
        inputPromptGroup.SetActive(true);
        inputLabel1.text =GetLocalizedText("RoleName");
        inputLabel2.text =GetLocalizedText("RolePrompt");
        inputField.text ="";
        secondInputField.text ="";
        inputField.ActivateInputField();

        confirmButtonText.text =GetLocalizedText("Add");
        cancelButtonText.text =GetLocalizedText("Cancel");
        secondaryButton.gameObject.SetActive(false);

        confirmAction = () =>
        {
            //把两个输入框文本传给RolesManager，新增角色
            if (rolesManager != null &&currentCategory != null)
            {
                rolesManager.AddRole(currentCategory,inputField.text,secondInputField.text);
            }
            HidePopup();
        };
        secondaryAction =null;

        LayoutRebuilder.ForceRebuildLayoutImmediate(containerRect);
    }

    //历史消息长按删除弹窗
    public void ShowHistoryDeletePopup(Conversation targetConversation)
    {
        currentPopupType =PopupType.HistoryDelete;
        currentConversation =targetConversation;//记住要删除的历史会话
        currentCategory =null;
        currentRole =null;

        popup.SetActive(true);

        titleText.text =GetLocalizedText("DeleteMessage");
        messageText.text =GetLocalizedText("DeleteConversationConfirm");

        inputNameGroup.SetActive(false);
        inputPromptGroup.SetActive(false);

        confirmButtonText.text =GetLocalizedText("Delete");
        cancelButtonText.text =GetLocalizedText("Cancel");
        secondaryButton.gameObject.SetActive(false);

        confirmAction = () =>
        {
            if (HistoryManager.instance != null)
            {
                HistoryManager.instance.RemoveConversation(currentConversation);
            }
            HidePopup();
        };
        secondaryAction =null;

        LayoutRebuilder.ForceRebuildLayoutImmediate(containerRect);
    }

    private void AddCategoryCallback()
    {
        //调用RolesManager，传入输入框填写的文本，真正新增分类
        if (rolesManager != null)
        {
            rolesManager.AddCategory(inputField.text);
        }
        HidePopup();//新增完成关闭弹窗
    }

    //按钮点击的回调函数
    private void ConfirmCallback()
    {
        confirmAction?.Invoke();
    }
    private void SecondaryCallback()
    {
        secondaryAction?.Invoke();
    }

    //Unity检测到用户切换语言，触发这个回调
    private void OnLanguageChanged(UnityEngine.Localization.Locale locale)
    {
        //popup.SetActive(true) → 设置激活（写）
        //popup.activeSelf → 查询当前状态（读，不能赋值）
        if (!popup.activeSelf)//.activeSelf：只读 bool，查询这个物体当前是否是激活显示状态
        {
            return;//弹窗没有打开，不用刷新，直接返回
        }
        RefreshPopup();//弹窗正在显示，重新刷新全部文字
    }

    //刷新当前Popup文字
    private void RefreshPopup()
    {
        //根据当前弹窗类型currentPopupType，重新赋值一遍全部多语言文字
        switch (currentPopupType)
        {
            case PopupType.AddCategory:
                titleText.text =GetLocalizedText("AddCategory");
                messageText.text =GetLocalizedText("PleaseEnterCategoryName");
                inputLabel1.text =GetLocalizedText("RoleCategoryName");
                confirmButtonText.text =GetLocalizedText("Add");
                cancelButtonText.text =GetLocalizedText("Cancel");
                break;
            case PopupType.Category:
                if (currentCategory != null)
                {
                    titleText.text =currentCategory.GetDisplayTitle();
                }
                messageText.text =GetLocalizedText("PleaseSelectOperation");
                confirmButtonText.text =GetLocalizedText("Delete");
                secondaryButtonText.text =GetLocalizedText("AddRole");
                cancelButtonText.text =GetLocalizedText("Cancel");
                break;
            case PopupType.Role:
                if (currentRole != null)
                {
                    titleText.text =currentRole.GetDisplayTitle();
                }
                messageText.text =GetLocalizedText("DeleteRole");
                confirmButtonText.text =GetLocalizedText("Delete");
                cancelButtonText.text =GetLocalizedText("Cancel");
                break;
            case PopupType.AddRole:
                titleText.text =GetLocalizedText("AddRole");
                messageText.text =GetLocalizedText("AddRoleMessage");
                inputLabel1.text =GetLocalizedText("RoleName");
                inputLabel2.text =GetLocalizedText("RolePrompt");
                confirmButtonText.text =GetLocalizedText("Add");
                cancelButtonText.text =GetLocalizedText("Cancel");
                break;
            case PopupType.HistoryDelete:
                titleText.text =GetLocalizedText("DeleteMessage");
                messageText.text =GetLocalizedText("DeleteConversationConfirm");
                confirmButtonText.text =GetLocalizedText("Delete");
                cancelButtonText.text =GetLocalizedText("Cancel");
                break;
        }

        LayoutRebuilder.ForceRebuildLayoutImmediate(containerRect);
    }

    public void HidePopup()
    {
        popup.SetActive(false);//隐藏弹窗物体

        confirmAction =null;
        secondaryAction =null;
        currentPopupType =PopupType.None;
        currentCategory =null;
        currentRole =null;
        currentConversation =null;
    }
}
