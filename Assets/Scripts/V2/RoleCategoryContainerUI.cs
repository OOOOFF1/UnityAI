using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.Localization.Settings;

public class RoleCategoryContainerUI : MonoBehaviour
{
    [Header("Elements")]
    [SerializeField] private TextMeshProUGUI titleText;//显示分类的标题文字
    [SerializeField] private RoleContainerUI roleContainerPrefab;//角色按钮预制体

    //Instantiate(预制体, 父物体Transform)第二个参数，要求传入 Transform 类型，用来把新生成的 UI 物体挂到这个父物体下面
    [SerializeField] private Transform roleContainersParent;//子角色按钮挂载父物体（Content）

    //保存当前分类的数据；外部可以读BindCategoryData，但是外部不能随便赋值，只能本脚本内部设置
    public RoleCategoryRuntimeData BindCategoryData { get; private set; }

    private void OnEnable()
    {
        //LocalizationSettings.SelectedLocaleChanged 是 Unity 本地化全局广播事件
        //订阅前先取消，防止重复订阅：多次OnEnable会多次+=，切换一次语言执行好几次函数
        LocalizationSettings.SelectedLocaleChanged -= OnLanguageChanged;
        //订阅系统全局事件：当用户切换游戏语言（中文↔英文），就执行 OnLanguageChanged 函数
        LocalizationSettings.SelectedLocaleChanged += OnLanguageChanged;
    }

    private void OnDisable()
    {
        LocalizationSettings.SelectedLocaleChanged -= OnLanguageChanged;
    }


    //传入一本小册子（RoleCategoryRuntimeData），刷新这个分类UI
    public void Configure(RoleCategoryRuntimeData roleCategoryData)
    {
        BindCategoryData =roleCategoryData;//把传进来的分类数据存到本脚本变量
        RefreshTitle();//刷新标题文字
        CreateRoleContainers(roleCategoryData);//循环生成该分类下全部角色按钮
    }

    //刷新分类标题
    private void RefreshTitle()
    {
        if (BindCategoryData == null)
        {
            return;
        }

        if (titleText == null)
        {
            return;
        }
        //调用数据对象的 GetDisplayTitle()，这个内部会做本地化，根据当前语言返回对应语言的标题文本
        titleText.text =BindCategoryData.GetDisplayTitle();
    }

    //语言切换以后刷新当前分类标题和角色按钮
    private void OnLanguageChanged(UnityEngine.Localization.Locale locale)
    {
        if (BindCategoryData == null)
        {
            return;
        }
        RefreshTitle();//更新标题多语言文字
        RefreshUI();//销毁全部旧角色按钮，重新生成一遍角色按钮（角色名字也要多语言刷新）
    }

    //循环这个分类里面所有角色，生成一个个角色按钮
    private void CreateRoleContainers(RoleCategoryRuntimeData roleCategoryData)
    {
        if (roleCategoryData == null)
        {
            return;
        }

        if (roleCategoryData.roles == null)
        {
            return;
        }
        //遍历这个分类下全部角色数据
        for (int i = 0;i < roleCategoryData.roles.Count;i++)
        {
            RoleRuntimeData role =roleCategoryData.roles[i];
            if (role == null)
            {
                continue;//数据为空就跳过，防止空报错
            }

            //实例化角色按钮，塞到roleContainersParent下面
            RoleContainerUI roleContainerInstance =Instantiate(roleContainerPrefab,roleContainersParent);
            //给新生成的角色按钮调用Configure，把角色数据 + 当前分类脚本实例传过去
            //this：把当前 RoleCategoryContainerUI 自己传给子角色按钮；子按钮可以反向拿到所属的分类
            roleContainerInstance.Configure(role,this);
        }
    }

    //刷新当前分类里面的角色按钮
    public void RefreshUI()
    {
        if (BindCategoryData == null)
        {
            return;
        }

        //清空旧角色按钮：从后往前删子物体,不能正序i=0开始删
        //如果正序 for (i=0;i<childCount;i++)，删除第 0 个，列表数组会前移，会漏掉一部分物体删不干净，会残留旧 UI
        for (int i =roleContainersParent.childCount - 1;i >= 0;i--)
        {
            Destroy(roleContainersParent.GetChild(i).gameObject);
        }

        //重新根据BindCategoryData数据生成全部角色按钮
        CreateRoleContainers(BindCategoryData);
        RefreshTitle();
    }

    //长按分类标题以后调用
    public void LongPressCallback()
    {
        if (RolePopupManager.instance == null)
        {
            Debug.LogWarning("RolePopupManager 不存在。");
            return;
        }
        //把当前绑定的分类数据传给弹窗，弹出分类编辑/删除弹窗
        RolePopupManager.instance.ShowCategoryPopup(BindCategoryData);
    }
}
