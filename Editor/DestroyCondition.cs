using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Neigerium
{
    [Serializable]
    public class DestroyCondition
    {
        public string ConditionName;
        public DestroyNameType NameType;
        public DestroyConditionType ConditionType;

    }

    public enum DestroyNameType
    {
        Object = 0,
        Component = 1,
    }

    public enum DestroyConditionType
    {
        Contain = 0,
        Match = 1,
    }
}
