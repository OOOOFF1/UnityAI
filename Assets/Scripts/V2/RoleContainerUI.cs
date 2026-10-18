using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using System;
using ChatGPTxUnity;
using UnityEngine.Localization.Settings;

public class RoleContainerUI : MonoBehaviour
{
    [Header("Elements")]
    [SerializeField] private TextMeshProUGUI roleText;//显示角色名字的文字组件

    private RoleRuntimeData role;//保存当前这个按钮绑定的角色运行时数据
    private RoleCategoryContainerUI parentCategoryContainer;//记录这个角色属于哪一个分类UI

    [Header("Action")]
    //静态事件：所有按钮共用这一个事件，点击就往外广播“哪个角色被点了”
    public static Action<ChatGPTxUnity.Role> onButtonClicked;

    private void OnEnable()
    {
        LocalizationSettings.SelectedLocaleChanged -= OnLanguageChanged;
        LocalizationSettings.SelectedLocaleChanged += OnLanguageChanged;
    }

    private void OnDisable()
    {
        LocalizationSettings.SelectedLocaleChanged -= OnLanguageChanged;
    }


    //初始化：给这个按钮赋值角色数据，显示角色名字，接收父分类容器
    public void Configure(RoleRuntimeData role,RoleCategoryContainerUI parentContainer)
    {
        this.role =role;//记住自己绑定的角色数据
        this.parentCategoryContainer =parentContainer;//记住自己属于哪个分类
        RefreshText();//立刻刷新界面上的角色名字
    }

    //刷新当前角色显示名字
    private void RefreshText()
    {
        if (role == null)
        {
            return;
        }

        if (roleText == null)
        {
            return;
        }
        //读取RoleRuntimeData的方法，根据当前选中语言返回对应的角色名字。中文返回中文，英文返回英文
        roleText.text =role.GetDisplayTitle();
    }

    //LocalizationSettings.SelectedLocaleChanged 是 Unity Localization 系统提供的全局事件（委托广播）
    //语言切换以后刷新角色名字
    private void OnLanguageChanged(UnityEngine.Localization.Locale locale)
    {
        RefreshText();
    }

    public void ButtonClickedCallback()
    {
        if (role == null)
        {
            return;
        }

        //把运行时数据转换回原来的 Role
        ChatGPTxUnity.Role originalRole =new ChatGPTxUnity.Role();

        //这里故意使用原来的 title,不能使用 GetDisplayTitle(),因为 title 是真正保存的原始角色名字
        originalRole.title =role.title;
        //Prompt 也继续使用原来的 Prompt
        originalRole.firstPrompt =role.firstPrompt;

        //触发广播，把刚刚组装好的originalRole往外发送出去
        onButtonClicked?.Invoke(originalRole);
    }

    //长按角色以后调用
    public void LongPressCallback()
    {
        if (role == null)
        {
            return;
        }

        if (RolePopupManager.instance == null)
        {
            Debug.LogWarning("RolePopupManager 不存在。");
            return;
        }
        //parentCategoryContainer.BindCategoryData：这个角色所属的分类数据。弹窗编辑角色的时候需要知道角色属于哪个分类
        RolePopupManager.instance.ShowRolePopup(role,parentCategoryContainer.BindCategoryData);
    }
}