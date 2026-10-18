using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ChatGPTxUnity;

//给 Unity 菜单增加一个“创建这个数据文件”的按钮
[CreateAssetMenu(fileName = "Role Category", menuName = "Scriptable Objects/Role Category", order = 0)]

//ScriptableObject:Unity 专门用来创建、保存和编辑“数据”的一个“数据文件”
public class RoleCategorySO : ScriptableObject
{
    public string title;
    public Role[] roles;
}

