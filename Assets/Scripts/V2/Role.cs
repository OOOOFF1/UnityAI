using System.Collections;
using System.Collections.Generic;
using UnityEngine;

//给一堆代码划一个“文件夹/区域”，防止不同代码重名
namespace ChatGPTxUnity
{
    //告诉 Unity，这个普通 C# 类里的数据可以被 Unity 序列化、保存，并在 Inspector 中显示
    [System.Serializable]
    public class Role
    {
        public string title;
        public string firstPrompt;
    }
}

