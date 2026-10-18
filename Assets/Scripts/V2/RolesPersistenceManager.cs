using System.Collections.Generic;
using System.IO;
using UnityEngine;
using ChatGPTxUnity;

//Unity JsonUtility只序列化标记[System.Serializable]的类；普通class不加这个存不了JSON
public class RolesPersistenceManager : MonoBehaviour
{
    private static RolesPersistenceManager instance;
    private string dataPath;//JSON 存档完整磁盘路径
    //短时间连续多次调用 SaveRoles，防止上一次 File 写入还没完成又发起新写入，造成 JSON 文件损坏
    private bool _isSaving; //写入防重入锁

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
        DontDestroyOnLoad(gameObject);

        //Application.persistentDataPath:Unity提供跨平台持久化目录，Windows/Android/iOS 都能用，游戏可读写；拼接文件名得到完整存档路径
        //不要用 Application.dataPath，打包后手机没有写入权限，只能读不能写
        dataPath = Application.persistentDataPath +"/RolesData.json";
    }

    // 保存 Roles
    public void SaveRoles(List<RoleCategoryRuntimeData> categories)
    {
        if (_isSaving)
        {
            Debug.LogWarning("Roles 正在保存，跳过重复写入。");
            return;
        }

        _isSaving = true;

        try
        {
            if (categories == null)
            {
                categories = new List<RoleCategoryRuntimeData>();
            }

            string path = GetDataPath();

            string directory = Path.GetDirectoryName(path);

            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            EnsureLocalizationData(categories);

            RolesSaveData saveData = new RolesSaveData();
            saveData.categories = categories;

            string json = JsonUtility.ToJson(saveData, true);

            File.WriteAllText(path, json);

            Debug.Log("Roles 存档写入成功：" + path);
        }
        catch (System.Exception e)
        {
            Debug.LogError("Roles 存档写入失败：" + e);
        }
        finally
        {
            _isSaving = false;
        }
    }

    //读取 Roles
    public List<RoleCategoryRuntimeData> LoadRoles(RoleCategorySO[] defaultCategories)
    {
        //using System.IO; .NET自带的输入输出命名空间,里面提供文件读写类：File、StreamReader、Directory
        //File.Exists(dataPath) 判断磁盘上这个 JSON 文件是否存在
        //第一次启动，没有 RolesData.json
        if (!File.Exists(dataPath))
        {
            //CreateDefaultData(defaultCategories):把传入的 SO 数组，转换成运行时数据 List
            List<RoleCategoryRuntimeData> firstData =CreateDefaultData(defaultCategories);
            EnsureLocalizationData(firstData);//修复一遍
            SaveRoles(firstData);//直接把默认数据写一份 JSON 到本地
            return firstData;//返回这份默认数据给 RolesManager，赋值给 runtimeCategories
        }

        //读取已有 JSON
        string json =File.ReadAllText(dataPath);
        //FromJson<T>()把 JSON 字符串反向转成 C# 对象
        RolesSaveData saveData =JsonUtility.FromJson<RolesSaveData>(json);

        //JSON 无效
        //读档容错：JSON 损坏、文件乱码，FromJson返回 null。此时放弃损坏存档，回退 SO 默认数据，并且直接覆盖写一份新 JSON。防止游戏直接崩溃
        if (saveData == null ||saveData.categories == null)
        {
            List<RoleCategoryRuntimeData> defaultData =CreateDefaultData(defaultCategories);
            EnsureLocalizationData(defaultData);
            SaveRoles(defaultData);
            return defaultData;
        }

        EnsureLocalizationData(saveData.categories);//修复旧数据       
        SaveRoles(saveData.categories);//重新保存一次
        return saveData.categories;//返回修复好的 List 给到 RolesManager
    }

    //创建默认 SO 数据
    //SO 是打包资源，运行时不能修改；要把 SO 的数据复制一份，变成内存可修改的RoleCategoryRuntimeData
    private List<RoleCategoryRuntimeData> CreateDefaultData(RoleCategorySO[] defaultCategories)
    {
        //新建返回容器 result；循环遍历每一个 SO；SO 为 null 跳过
        List<RoleCategoryRuntimeData> result =new List<RoleCategoryRuntimeData>();
        if (defaultCategories == null)
        {
            return result;
        }
        for (int i = 0;i < defaultCategories.Length;i++)
        {
            RoleCategorySO category =defaultCategories[i];
            if (category == null)
            {
                continue;
            }

            RoleCategoryRuntimeData runtimeCategory =new RoleCategoryRuntimeData();
            runtimeCategory.title =category.title;//原始名字
            //创建 Category 本地化数据
            runtimeCategory.localization =RoleLocalizationData.Create(category.title);
            runtimeCategory.roles =new List<RoleRuntimeData>();

            //创建 Category 里面的 Roles
            if (category.roles != null)
            {
                for (int j = 0;j < category.roles.Length;j++)
                {
                    Role role =category.roles[j];
                    if (role == null)
                    {
                        continue;
                    }
                    RoleRuntimeData runtimeRole =new RoleRuntimeData();
                    runtimeRole.title =role.title;
                    runtimeRole.firstPrompt =role.firstPrompt;
                    //创建 Role 本地化数据
                    runtimeRole.localization =RoleLocalizationData.Create(role.title);
                    runtimeCategory.roles.Add(runtimeRole);
                }
            }
            result.Add(runtimeCategory);
        }
        return result;
    }

    //检查 / 修复本地化数据
    private void EnsureLocalizationData(List<RoleCategoryRuntimeData> categories)
    {
        if (categories == null)
        {
            return;
        }
        for (int i = 0;i < categories.Count;i++)
        {
            RoleCategoryRuntimeData category =categories[i];
            if (category == null)
            {
                continue;
            }
            //Category 本地化数据
            if (category.localization == null)
            {
                category.localization =RoleLocalizationData.Create(category.title);
            }
            else
            {
                //如果不为 null：交给 RepairLocalizationData 做进一步修复
                RepairLocalizationData(category.localization,category.title);
            }

            //title 字符串丢失为空；就拿翻译盒子里保存的 original 原始名字把 title 填回去
            if (string.IsNullOrWhiteSpace(category.title))
            {
                category.title =category.localization.original;
            }

            if (category.roles == null)
            {
                category.roles =new List<RoleRuntimeData>();
                continue;
            }
            for (int j = 0;j < category.roles.Count;j++)
            {
                RoleRuntimeData role =category.roles[j];
                if (role == null)
                {
                    continue;
                }
                //Role 本地化数据
                if (role.localization == null)
                {
                    role.localization =RoleLocalizationData.Create(role.title);
                }
                else
                {
                    RepairLocalizationData(role.localization,role.title);
                }
                //如果 title 空了,使用 original 恢复
                if (string.IsNullOrWhiteSpace(role.title))
                {
                    role.title =role.localization.original;
                }
            }
        }
    }

    //修复一个本地化数据
    private void RepairLocalizationData(RoleLocalizationData localization,string currentTitle)
    {
        if (localization == null)
        {
            return;
        }

        //第一个 if：如果original是空，就用currentTitle把它填上
        //填完之后，再做一次判断：如果现在依旧是空（说明 currentTitle 本身也是空字符串），直接 return 函数结束，不要再往下跑逻辑
        if (string.IsNullOrWhiteSpace(localization.original))
        {
            localization.original =currentTitle;
        }
        if (string.IsNullOrWhiteSpace(localization.original))
        {
            return;
        }

        string original =localization.original.Trim();
        localization.original =original;//把去空格后的字符串，写回对象的成员字段保存

        if (!localization.translationsGenerated)//还没有完整翻译
        {
            if (RoleLocalizationData.ContainsChinese(original))
            {
                //中文永远保留原文
                localization.chinese =original;
                //英文清空,交给AI重新翻译
                localization.english ="";
                //确保没有错误的旧翻译
                localization.translationsGenerated =false;
            }
            else
            {
                //英文永远保留原文
                localization.english =original;
                //中文清空,交给AI重新翻译
                localization.chinese ="";
                //确保没有错误的旧翻译
                localization.translationsGenerated =false;
            }
            return;
        }
       
        //已经完成翻译的数据
        if (string.IsNullOrWhiteSpace(localization.english) ||string.IsNullOrWhiteSpace(localization.chinese))
        {
            localization.translationsGenerated =false;
            if (RoleLocalizationData.ContainsChinese(original))
            {
                //中文原文
                localization.chinese =original;
                //英文重新翻译
                localization.english ="";
                //等游戏启动TranslateAllPendingNames协程调用 AI 补齐缺失翻译
            }
            else
            {
                //英文原文
                localization.english =original;
                //中文重新翻译
                localization.chinese ="";
            }
        }
    }

    //删除 Roles 保存数据
    [NaughtyAttributes.Button("Delete Roles Save Data")]
    public void DeleteRolesSaveData()
    {
        string path =GetDataPath();
        if (File.Exists(path))
        {
            File.Delete(path);//调用File.Delete()删除 JSON 文件
        }
        else
        {
            //Debug.Log("没有找到 RolesData.json：" +path);
        }
    }

    //获取数据路径
    private string GetDataPath()
    {
        if (string.IsNullOrWhiteSpace(dataPath))
        {
            dataPath =Application.persistentDataPath +"/RolesData.json";
        }
        return dataPath;
    }

    //输出数据路径
    [ContextMenu("Print Roles Data Path")]
    private void PrintRolesDataPath()
    {
        //Debug.Log("RolesData.json 路径：" +GetDataPath());
    }
}

//保存数据
//Unity自带JsonUtility有硬性限制：不能直接把List<T>直接作为最顶层对象转json.外壳作为顶层对象，就正常工作
[System.Serializable]
public class RolesSaveData
{
    public List<RoleCategoryRuntimeData> categories =new List<RoleCategoryRuntimeData>();
}

[System.Serializable]
public class RoleCategoryRuntimeData
{
    public string title;//原始名字
    public RoleLocalizationData localization;//中英文本地化数据
    //Category 里面的 Roles
    public List<RoleRuntimeData> roles =new List<RoleRuntimeData>();

    //获取当前语言应该显示的名字
    //GetDisplayTitle()：对外接口，调用 localization 的方法返回适配当前游戏语言的显示文本；兜底返回 title，防止 localization 为 null 报错
    public string GetDisplayTitle()
    {
        if (localization == null)
        {
            return title;
        }
        return localization.GetCurrentLanguageName();
    }
}

[System.Serializable]
public class RoleRuntimeData
{
    public string title;//原始名字
    public string firstPrompt;//角色提示词
    public RoleLocalizationData localization;//中英文本地化数据

    //获取当前语言应该显示的名字
    public string GetDisplayTitle()
    {
        if (localization == null)
        {
            return title;
        }
        return localization.GetCurrentLanguageName();
    }
}

//Role / Category 本地化数据
[System.Serializable]
public class RoleLocalizationData
{
    public string original;//用户最开始输入的名字,永远保留，不会被修改
    public string english;//英文显示名字
    public string chinese;//中文显示名字
    public bool translationsGenerated;//是否已经完成中英文翻译

    //创建本地化数据
    public static RoleLocalizationData Create(string source)
    {
        RoleLocalizationData data =new RoleLocalizationData();
        data.Initialize(source);
        return data;
    }

    //初始化
    public void Initialize(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return;
        }
        source =source.Trim();
        //保存原始名字
        if (string.IsNullOrWhiteSpace(original))
        {
            original =source;
        }

        //如果原文包含中文
        if (ContainsChinese(source))
        {
            //中文原文直接保存
            chinese =source;
            //英文留空,等待 AI 翻译
            english ="";
            translationsGenerated =false;
        }

        //如果原文不是中文
        else
        {
            //英文/非中文原文直接保存
            english =source;
            //中文留空,等待 AI 翻译
            chinese ="";
            translationsGenerated =false;
        }
    }

    //获取当前语言名字
    public string GetCurrentLanguageName()
    {
        string languageCode =GetCurrentLanguageCode();

        //中文
        //判断这个字符串是不是以 zh 开头
        if (languageCode.StartsWith("zh"))
        {
            if (!string.IsNullOrWhiteSpace(chinese))
            {
                return chinese;
            }
        }

        //英文
        if (languageCode.StartsWith("en"))
        {
            if (!string.IsNullOrWhiteSpace(english))
            {
                return english;
            }
        }

        //当前语言没有翻译,使用原始名字
        if (!string.IsNullOrWhiteSpace(original))
        {
            return original;
        }
        if (!string.IsNullOrWhiteSpace(english))
        {
            return english;
        }
        if (!string.IsNullOrWhiteSpace(chinese))
        {
            return chinese;
        }
        return "";
    }

    //获取当前 Unity Localization 语言代码
    private string GetCurrentLanguageCode()
    {
        try
        {
            if (UnityEngine.Localization.Settings.LocalizationSettings.SelectedLocale != null)
            {
                string code =UnityEngine.Localization.Settings.LocalizationSettings.SelectedLocale.Identifier.Code;
                if (!string.IsNullOrWhiteSpace(code))
                {
                    //防止大小写不一样判断出错，统一转小写之后再做StartsWith对比
                    return code.ToLower();//把字符串全部转为小写字母
                }
            }
        }
        catch
        {
            //Localization还没有初始化完成
        }
        //默认中文
        return "zh-cn";
    }

    public static bool ContainsChinese(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }
        for (int i = 0;i < text.Length;i++)
        {
            char c =text[i];
            //CJK统一汉字
            if ((c >= '\u4E00' &&c <= '\u9FFF') ||(c >= '\u3400' &&c <= '\u4DBF'))
            {
                return true;
            }
        }
        return false;
    }
}
