using UnityEngine;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization;
using UnityEngine.UI;

public class LanguageSelector : MonoBehaviour
{
    [Header("语言开关按钮")]
    [SerializeField] private Button chineseToggleBtn;
    [SerializeField] private Button englishToggleBtn;

    [Header("开关Sprite资源")]
    [SerializeField] private Sprite toggleOffSprite;
    [SerializeField] private Sprite toggleOnSprite;

    [Header("开关颜色")]
    [SerializeField] private Color toggleOnColor;
    [SerializeField] private Color toggleOffColor;

    void Start()
    {
        //读取本地存档：取出上次保存的语言序号，默认是0
        int savedLocaleIndex = PlayerPrefs.GetInt("SelectedLocale", 0);

        //拿到本地化系统配置里全部可用语言列表（中文、英文…）
        var locales = LocalizationSettings.AvailableLocales.Locales;

        //判断读取到的序号是合法有效的
        if (savedLocaleIndex >= 0 && savedLocaleIndex < locales.Count)
        {
            //把全局语言设置成上次保存的语言
            LocalizationSettings.SelectedLocale = locales[savedLocaleIndex];
        }

        //刷新按钮UI，哪个语言选中就显示选中样式
        RefreshToggleUI();

        //订阅语言切换事件：当全局语言被别的地方修改时，执行OnLocaleChanged
        LocalizationSettings.SelectedLocaleChanged -= OnLocaleChanged;
        LocalizationSettings.SelectedLocaleChanged += OnLocaleChanged;
    }

    // 只要全局语言发生变化，就会触发这个回调
    private void OnLocaleChanged(Locale newLocale)
    {
        RefreshToggleUI();
    }

    // 切换简体中文
    public void SetChinese()
    {
        //Locale=语言对象，Unity Localization 系统里，每一门语言就是一个 Locale 对象
        //从本地化库拿到简体中文语言对象 zh‑Hans
        Locale zhLocale = LocalizationSettings.AvailableLocales.GetLocale("zh-Hans");
        if (zhLocale != null)
        {
            //LocalizationSettings.SelectedLocale是全局变量，改它，整个游戏所有本地化文字立刻切换语言
            //设置全局当前语言为中文，整个游戏文字全部变成中文
            LocalizationSettings.SelectedLocale = zhLocale;

            //获取这个语言在语言列表里面的序号
            int index = LocalizationSettings.AvailableLocales.Locales.IndexOf(zhLocale);

            //把语言序号存入本地 PlayerPrefs
            PlayerPrefs.SetInt("SelectedLocale", index);
            PlayerPrefs.Save();//立刻写入磁盘保存
            
            RefreshToggleUI();//刷新按钮显示状态
        }
    }

    // 切换英文
    public void SetEnglish()
    {
        Locale enLocale = LocalizationSettings.AvailableLocales.GetLocale("en");
        if (enLocale != null)
        {
            LocalizationSettings.SelectedLocale = enLocale;
            int index = LocalizationSettings.AvailableLocales.Locales.IndexOf(enLocale);
            PlayerPrefs.SetInt("SelectedLocale", index);
            PlayerPrefs.Save();
            RefreshToggleUI();
        }
    }

    // 更新两个按钮的图片、颜色：选中的显示on，没选中显示off
    private void RefreshToggleUI()
    {
        //读取本地保存的语言索引
        int savedIndex = PlayerPrefs.GetInt("SelectedLocale", 0);

        //如果savedIndex等于0 → chineseActive = true（中文处于选中状态）
        //如果savedIndex不等于 0 → chineseActive = false（中文没有被选中）
        bool chineseActive = savedIndex == 0;//索引0=中文是否选中
        bool englishActive = savedIndex == 1;//索引1=英文是否选中

        //中文按钮
        if (chineseToggleBtn != null)
        {
            Image img = chineseToggleBtn.GetComponent<Image>();
            if (img != null)
            {
                //三元运算符：条件 ? 满足返回A :不满足返回B
                img.sprite = chineseActive ? toggleOnSprite : toggleOffSprite;
                img.color = chineseActive ? toggleOnColor : toggleOffColor;
            }
        }
        //英文按钮
        if (englishToggleBtn != null)
        {
            Image img = englishToggleBtn.GetComponent<Image>();
            if (img != null)
            {
                img.sprite = englishActive ? toggleOnSprite : toggleOffSprite;
                img.color = englishActive ? toggleOnColor : toggleOffColor;
            }
        }
    }

    private void OnDestroy()
    {
        LocalizationSettings.SelectedLocaleChanged -= OnLocaleChanged;
    }
}
