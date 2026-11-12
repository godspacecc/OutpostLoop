using System.Collections.Generic;
using UnityEngine;

namespace Game
{
    /// <summary>
    /// 定义可拖拽容器的子项数量和刷新回调入口，供自定义网格与拖拽逻辑协作。
    /// </summary>
    public interface IDraggableContainer
    {
        List<Transform> GetDraggableItems();
        Transform GetContentTransform();
    }
}
