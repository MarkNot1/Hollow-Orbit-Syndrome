using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu]

public class componentDatabaseSO : ScriptableObject
{
 
        public List<componentData> componentDataList;
}
[Serializable]
public class componentData
{
    [field: SerializeField]
    public string componentName { get; private set; }
    [field: SerializeField] 
    public int ID { get; private set; }
    [field: SerializeField] 
    public Vector2Int Size { get; private set; } = Vector2Int.one;
    [field: SerializeField] 
    public GameObject Prefab { get; private set; }

}