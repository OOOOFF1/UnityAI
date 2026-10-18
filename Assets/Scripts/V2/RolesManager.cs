using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RolesManager : MonoBehaviour
{
    public static RolesManager instance;

    [Header("Elements")]
    [SerializeField] private RoleCategoryContainerUI roleCategoryContainerPrefab;
    [SerializeField] private Transform roleCategoryContainersParent;

    [Header("References")]
    //persistenceManager：持久化管理器，读写 JSON 文件，加载/保存角色数据
    [SerializeField] private RolesPersistenceManager persistenceManager;
    [SerializeField] private OllamaManager ollamaManager;

    [Header("Data")]
    //roleCategories：SO（ScriptableObject）数组，程序内置的默认角色分类
    //游戏第一次打开，本地没有存档JSON 的时候，就用这些 SO 当做初始数据。后续用户新增删除都存在runtimeCategories
    [SerializeField] private RoleCategorySO[] roleCategories;

    //runtimeCategories:内存里运行时的全部分类数据，核心数据
    //SO是硬盘上的静态资源；runtimeCategories是游戏运行在内存中的 List。之后交给persistenceManager存成JSON到本地。
    private List<RoleCategoryRuntimeData> runtimeCategories =new List<RoleCategoryRuntimeData>();

    //翻译锁。防止同时启动很多个翻译协程，避免并发请求炸掉。true = 正在翻译，不开启新翻译任务
    private bool isTranslating;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void Start()
    {
        Initialize();
    }

    private void Initialize()
    {
        if (persistenceManager == null)
        {
            Debug.LogError("RolesPersistenceManager 没有赋值！请在Inspector拖拽赋值。");
            return;
        }
        //读取本地 Roles 数据
        runtimeCategories =persistenceManager.LoadRoles(roleCategories);
        RefreshAllUI();//根据内存数据，刷新界面列表，生成所有分类 UI
        //游戏启动后自动检查所有翻译
        StartCoroutine(TranslateAllPendingNames());
    }

    //生成单条分类 UI
    private void CreateRoleCategoryContainer(RoleCategoryRuntimeData roleCategoryData)
    {
        if (roleCategoryData == null)
        {
            return;
        }
        if (roleCategoryContainerPrefab == null)
        {
            Debug.LogError("RoleCategoryContainerPrefab 没有设置！");
            return;
        }
        if (roleCategoryContainersParent == null)
        {
            Debug.LogError("RoleCategoryContainersParent 没有设置！");
            return;
        }

        //克隆预制体，父物体是ScrollView Content
        RoleCategoryContainerUI roleCategoryContainerInstance =Instantiate(roleCategoryContainerPrefab,roleCategoryContainersParent);
        //把数据传给UI脚本Configure，UI就会渲染显示名字
        roleCategoryContainerInstance.Configure(roleCategoryData);
    }

    //刷新全部分类 UI
    public void RefreshAllUI()
    {
        if (roleCategoryContainersParent == null)
        {
            return;
        }

        // 删除旧的角色分类 UI
        for (int i = roleCategoryContainersParent.childCount - 1; i >= 0; i--)
        {
            Transform childTrans = roleCategoryContainersParent.GetChild(i);

            if (childTrans.name.Contains("Roles Category Container"))
            {
                // 根据运行模式选择销毁方式
                if (Application.isPlaying)
                {
                    Destroy(childTrans.gameObject);
                }
                else
                {
                    DestroyImmediate(childTrans.gameObject);
                }
            }
        }

        // 根据运行时数据重新生成 UI
        if (runtimeCategories == null)
        {
            return;
        }

        for (int i = 0; i < runtimeCategories.Count; i++)
        {
            CreateRoleCategoryContainer(runtimeCategories[i]);
        }
    }


    //删除整个分类
    public void DeleteCategory(RoleCategoryRuntimeData category)
    {
        if (category == null)
        {
            return;
        }
        runtimeCategories.Remove(category);//内存List移除这个分类
        SaveData();//保存到本地JSON
        RefreshAllUI();//刷新界面，界面就看不到这条分类了
    }

    //新增分类
    public void AddCategory(string categoryName)
    {
        if (string.IsNullOrWhiteSpace(categoryName))
        {
            return;
        }
        string cleanName =categoryName.Trim();//去掉前后空格

        //新建内存数据对象
        RoleCategoryRuntimeData newCategory =new RoleCategoryRuntimeData();
        newCategory.title =cleanName;//保存原始输入名字
        newCategory.localization =RoleLocalizationData.Create(cleanName);//创建翻译容器，原始文本存进去
        newCategory.roles =new List<RoleRuntimeData>();//这个分类下面的角色空列表

        runtimeCategories.Add(newCategory);//加到内存总列表
        SaveData();//立刻存本地
        RefreshAllUI();//立刻刷新界面显示新增分类
        StartCoroutine(TranslateCategoryName(newCategory));//启动协程调用AI自动翻译名字
    }

    //翻译单个分类名字
    private IEnumerator TranslateCategoryName(RoleCategoryRuntimeData category)
    {
        if (category == null)
        {
            yield break;
        }

        OllamaManager aiManager = ollamaManager;
        if (aiManager == null)
        {
            Debug.LogWarning("OllamaManager 没有赋值，无法自动翻译 Category。请检查Inspector引用。");
            yield break;
        }

        if (category.localization == null)
        {
            category.localization =RoleLocalizationData.Create(category.title);
        }

        bool finished =false;//标记AI 网络回调有没有跑完
        bool success =false;//标记翻译结果是否完整拿到中英两套文本

        //调用 Ollama / 智谱翻译
        yield return StartCoroutine(aiManager.TranslateRoleName(category.localization.original,(english, chinese) =>
                {
                    //AI返回结果进入这个回调
                    if (category == null)
                    {
                        finished =true;
                        return;
                    }
                    if (category.localization == null)
                    {
                        category.localization =RoleLocalizationData.Create(category.title);
                    }
                    //把AI返回的英文、中文存进localization
                    if (!string.IsNullOrWhiteSpace(english))
                    {
                        category.localization.english =english;
                    }
                    if (!string.IsNullOrWhiteSpace(chinese))
                    {
                        category.localization.chinese =chinese;
                    }
                    //中英文都拿到，标记翻译完成
                    if (!string.IsNullOrWhiteSpace(category.localization.english) &&!string.IsNullOrWhiteSpace(category.localization.chinese))
                    {
                        category.localization.translationsGenerated =true;
                        success =true;
                    }
                    finished =true;//标记翻译协程结束
                }
            )
        );

        //等待回调完成，不停等finished变成true
        while (!finished)
        {
            //协程暂停当前这一帧，等到下一帧 Update 再回来判断 while 条件
            yield return null;
        }

        SaveData();//翻译完成，把翻译结果保存到JSON
        RefreshAllUI();
        if (success)
        {
            Debug.Log("Category 自动翻译完成：" +category.localization.original);
        }
        else
        {
            Debug.LogWarning("Category 自动翻译没有完整完成：" +category.title);
        }
    }

    //删除指定分类里面的角色
    public void DeleteRole(RoleCategoryRuntimeData category,RoleRuntimeData role)
    {
        if (category == null ||role == null)
        {
            return;
        }
        if (category.roles == null)
        {
            return;
        }

        category.roles.Remove(role);//在该分类的角色List移除
        SaveData();
        RefreshAllUI();
    }

    //新增角色
    public void AddRole(RoleCategoryRuntimeData category,string roleName,string firstPrompt)
    {
        if (category == null)
        {
            return;
        }
        if (string.IsNullOrWhiteSpace(roleName))
        {
            return;
        }
        if (category.roles == null)
        {
            category.roles =new List<RoleRuntimeData>();
        }

        string cleanName =roleName.Trim();
        RoleRuntimeData newRole =new RoleRuntimeData();
        newRole.title =cleanName;
        newRole.firstPrompt =firstPrompt;//角色提示词，不做翻译

        //创建中英文翻译数据
        newRole.localization =RoleLocalizationData.Create(cleanName);

        category.roles.Add(newRole);//加到这个分类内部的角色列表
        SaveData();
        RefreshAllUI();
        StartCoroutine(TranslateRoleName(newRole));//后台AI翻译角色名字
    }

    //自动翻译 Role
    private IEnumerator TranslateRoleName(RoleRuntimeData role)
    {
        if (role == null)
        {
            yield break;
        }

        OllamaManager aiManager = ollamaManager;
        if (aiManager == null)
        {
            Debug.LogWarning("OllamaManager 没有赋值，无法自动翻译 Role。请检查Inspector引用。");
            yield break;
        }

        if (role.localization == null)
        {
            role.localization =RoleLocalizationData.Create(role.title);
        }

        bool finished =false;
        bool success =false;

        //调用 Ollama / 智谱翻译
        yield return StartCoroutine(aiManager.TranslateRoleName(role.localization.original,(english, chinese) =>
                {
                    if (role == null)
                    {
                        finished =true;
                        return;
                    }
                    if (role.localization == null)
                    {
                        role.localization =RoleLocalizationData.Create(role.title);
                    }
                    //保存英文
                    if (!string.IsNullOrWhiteSpace(english))
                    {
                        role.localization.english =english;
                    }
                    //保存中文
                    if (!string.IsNullOrWhiteSpace(chinese))
                    {
                        role.localization.chinese =chinese;
                    }
                    //两个翻译都存在
                    if (!string.IsNullOrWhiteSpace(role.localization.english) &&!string.IsNullOrWhiteSpace(role.localization.chinese))
                    {
                        role.localization.translationsGenerated =true;
                        success =true;
                    }
                    finished =true;
                }
            )
        );

        while (!finished)
        {
            yield return null;
        }

        SaveData();
        RefreshAllUI();
        if (success)
        {
            Debug.Log("Role 自动翻译完成：" +role.localization.original);
        }
        else
        {
            Debug.LogWarning("Role 自动翻译没有完整完成：" +role.title);
        }
    }

    //游戏启动执行,遍历全部，补齐缺失翻译
    private IEnumerator TranslateAllPendingNames()
    {
        if (isTranslating)
        {
            yield break;
        }
        isTranslating =true;//上锁，禁止重复进入

        yield return null;//等一帧，保证OllamaManager初始化完成

        OllamaManager aiManager = ollamaManager;
        if (aiManager == null)
        {
            Debug.LogWarning("OllamaManager 没有赋值，无法检查 Roles 翻译。");
            isTranslating =false;
            yield break;
        }
        if (runtimeCategories == null)
        {
            isTranslating =false;
            yield break;
        }

        //遍历所有分类
        for (int i = 0;i < runtimeCategories.Count;i++)
        {
            RoleCategoryRuntimeData category =runtimeCategories[i];
            if (category == null)
            {
                continue;
            }

            //判断分类是否缺少翻译，需要翻译就启动协程，yield等待翻译做完再循环下一个
            if (NeedTranslate(category.localization))
            {
                yield return StartCoroutine(TranslateCategoryName(category));
            }

            //遍历这个分类下所有角色
            if (category.roles != null)
            {
                for (int j = 0;j < category.roles.Count;j++)
                {
                    RoleRuntimeData role =category.roles[j];
                    if (role == null)
                    {
                        continue;
                    }
                    if (NeedTranslate(role.localization))
                    {
                        //会等待一个翻译完整结束，才处理下一个，串行翻译，不会并发一堆网络请求
                        yield return StartCoroutine(TranslateRoleName(role));
                    }
                }
            }
        }

        SaveData();
        RefreshAllUI();
        isTranslating =false;//翻译全部完成，解锁
    }

    //判断是否需要翻译
    private bool NeedTranslate(RoleLocalizationData localization)
    {
        if (localization == null)
        {
            return true;
        }
        if (string.IsNullOrWhiteSpace(localization.original))
        {
            return false;
        }

        //缺少英文 / 缺少中文 / 没有标记生成完成 → 返回true需要翻译
        if (string.IsNullOrWhiteSpace(localization.english))
        {
            return true;
        }
        if (string.IsNullOrWhiteSpace(localization.chinese))
        {
            return true;
        }
        if (!localization.translationsGenerated)
        {
            return true;
        }

        return false;
    }

    //保存当前 Roles
    private void SaveData()
    {
        if (persistenceManager != null)
        {
            //把内存`runtimeCategories`交给持久化管理器，序列化为 JSON 写入本地磁盘。
            //只要内存数据发生改动（增、删、翻译完成），就调用 SaveData()，立刻落盘保存**
            persistenceManager.SaveRoles(runtimeCategories);
        }
    }

    /// <summary>
    /// 恢复默认角色分类和角色，清除自定义角色数据。
    /// </summary>
    public void ResetRolesToDefaults()
    {
        // 停止角色管理器自身的翻译协程
        StopAllCoroutines();
        isTranslating = false;

        try
        {
            // 确保持久化管理器存在
            if (persistenceManager == null)
            {
                persistenceManager =FindObjectOfType<RolesPersistenceManager>();
            }

            if (persistenceManager == null)
            {
                Debug.LogError("RolesManager：找不到 RolesPersistenceManager，" +"无法恢复默认角色。");
                return;
            }

            // 通过持久化管理器删除角色存档
            persistenceManager.DeleteRolesSaveData();

            // 重新从 Inspector 配置的默认 ScriptableObject 创建角色
            runtimeCategories =persistenceManager.LoadRoles(roleCategories);

            if (runtimeCategories == null)
            {
                Debug.LogError("RolesManager：加载默认角色失败，返回的数据为空。");
                return;
            }

            // 刷新角色界面
            RefreshAllUI();

            // 重新检查默认角色是否需要翻译
            StartCoroutine(TranslateAllPendingNames());

            Debug.Log("RolesManager：默认角色数据已重新加载。");
        }
        catch (System.Exception e)
        {
            Debug.LogError("RolesManager：恢复默认角色失败：" + e);
        }
    }
}
