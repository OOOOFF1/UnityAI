using UnityEngine;

public class RolesTitleLongPress : MonoBehaviour
{
    //长按 Roles 标题以后调用
    public void LongPressCallback()
    {
        if (RolePopupManager.instance == null)
        {
            Debug.LogWarning("RolePopupManager 不存在。");

            return;
        }

        RolePopupManager.instance.ShowAddCategoryPopup();
    }
}