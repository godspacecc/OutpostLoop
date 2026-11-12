using UnityEngine;
using UnityEngine.UI;
using UnityEditor;

[CustomEditor(typeof(LoopScrollRect), true)]
public class LoopScrollRectInspector : UnityEditor.Editor
{
    int index = 0;
    float speed = 1000, time = 1;
    private int refreshCount = 10;
    
    public override void OnInspectorGUI ()
    {
        base.OnInspectorGUI();
        EditorGUILayout.Space();

        LoopScrollRect scroll = (LoopScrollRect)target;
        
        // 编辑器模式下的操作（始终可用）
        GUILayout.Space(10);
        EditorGUILayout.LabelField("网格操作", EditorStyles.boldLabel);
        
        // 刷新数量设置
        refreshCount = EditorGUILayout.IntField("刷新数量", refreshCount);
        refreshCount = Mathf.Max(0, refreshCount); // 确保非负数
        
        GUILayout.Space(5);
        
        // 编辑器模式下的按钮（始终可用）
        EditorGUILayout.BeginHorizontal();
        if(GUILayout.Button("刷新网格"))
        {
            RefreshGridInEditor(scroll);
        }
        if(GUILayout.Button("清空网格"))
        {
            ClearGridInEditor(scroll);
        }
        EditorGUILayout.EndHorizontal();
        
        GUILayout.Space(10);
        EditorGUILayout.LabelField("调试信息", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("子对象数量: ", scroll.content.childCount.ToString());
        
        GUILayout.Space(10);
        EditorGUILayout.LabelField("运行时操作", EditorStyles.boldLabel);
        
        // 运行时操作（仅在运行时可用）
        GUI.enabled = Application.isPlaying;
        
        EditorGUILayout.BeginHorizontal();
        if(GUILayout.Button("Clear"))
        {
            scroll.ClearCells();
        }
        if (GUILayout.Button("Refresh"))
        {
            scroll.RefreshCells();
        }
        if(GUILayout.Button("Refill"))
        {
            scroll.RefillCells();
        }
        if(GUILayout.Button("RefillFromEnd"))
        {
            scroll.RefillCellsFromEnd();
        }
        EditorGUILayout.EndHorizontal();

        EditorGUIUtility.labelWidth = 45;
        float w = (EditorGUIUtility.currentViewWidth - 100) / 2;
        index = EditorGUILayout.IntField("Index", index, GUILayout.Width(w));
        EditorGUILayout.BeginHorizontal();
        EditorGUIUtility.labelWidth = 60;
        speed = EditorGUILayout.FloatField("    Speed", speed, GUILayout.Width(w+15));
        if(GUILayout.Button("Scroll With Speed", GUILayout.Width(130)))
        {
            scroll.SrollToCell(index, speed);
        }
        EditorGUILayout.EndHorizontal();
        
        EditorGUILayout.BeginHorizontal();
        EditorGUIUtility.labelWidth = 60;
        time = EditorGUILayout.FloatField("    Time", time, GUILayout.Width(w+15));
        if(GUILayout.Button("Scroll Within Time", GUILayout.Width(130)))
        {
            scroll.SrollToCellWithinTime(index, time);
        }
        EditorGUILayout.EndHorizontal();
        
        // 恢复GUI启用状态
        GUI.enabled = true;
    }
    
    // 编辑器模式下刷新网格
    private void RefreshGridInEditor(LoopScrollRect scroll)
    {
        if (Application.isPlaying)
        {
            Debug.LogWarning("请在编辑器模式下使用刷新网格功能");
            return;
        }
        
        // 检查PrefabSource下面的prefab字段是否为空
        if (scroll.prefabSource == null || scroll.prefabSource.prefab == null)
        {
            Debug.LogError("PrefabSource或PrefabSource.prefab为空，无法生成网格数据。请先设置PrefabSource字段下的prefab字段。");
            return;
        }
        
        // 清空现有内容
        ClearGridInEditor(scroll);
        
        // 使用PrefabSource.prefab创建网格项
        for (int i = 0; i < refreshCount; i++)
        {
            // 使用PrefabSource.prefab获取预设对象
            var prefab = scroll.prefabSource.prefab;
            if (prefab == null)
            {
                Debug.LogError($"PrefabSource.prefab为空，无法生成网格项");
                continue;
            }
            
            // 实例化预设
            GameObject item = UnityEditor.PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            if (item == null)
            {
                Debug.LogError($"无法实例化预设对象，索引: {i}");
                continue;
            }
            
            // 设置父对象和位置
            item.transform.SetParent(scroll.content);
            item.transform.localPosition = Vector3.zero;
            item.transform.localRotation = Quaternion.identity;
            item.transform.localScale = Vector3.one;
            
            // 激活对象
            item.SetActive(true);
            
            // 设置名称以便识别（与CustomGridLayoutGroupEditor保持一致）
            item.name = $"EditorItem_{i}";
        }
        
        Debug.Log($"在编辑器模式下使用PrefabSource刷新了 {refreshCount} 个网格项");
    }
    
    // 编辑器模式下清空网格
    private void ClearGridInEditor(LoopScrollRect scroll)
    {
        if (Application.isPlaying)
        {
            Debug.LogWarning("请在编辑器模式下使用清空网格功能");
            return;
        }
        
        // 删除所有子对象
        int childCount = scroll.content.childCount;
        for (int i = childCount - 1; i >= 0; i--)
        {
            GameObject.DestroyImmediate(scroll.content.GetChild(i).gameObject);
        }
        
        Debug.Log($"在编辑器模式下清空了网格，删除了 {childCount} 个子对象");
    }
}