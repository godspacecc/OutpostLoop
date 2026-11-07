using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Game
{
    /// <summary>
    /// 为小规模固定数量的 UI 列表复用由预制体创建的列表项。
    /// </summary>
    [AddComponentMenu("UI/Custom List View")]
    public sealed class CustomListView : MonoBehaviour, IDraggableContainer
    {
        public GameObject itemPrefab;
        private readonly List<GameObject> cells = new List<GameObject>();
        private Action<Transform, int> onItemRefresh;

        public int totalCount { get; set; }

        public void AddItemRefreshListener(Action<Transform, int> refresh)
        {
            onItemRefresh = refresh;
        }

        public void RefreshCells()
        {
            if (itemPrefab == null)
            {
                Log.Error("[CustomListView] 列表元素预制体缺失。");
                return;
            }

            cells.RemoveAll(cell => cell == null);
            var activeCount = Mathf.Max(0, totalCount);
            while (cells.Count < activeCount)
            {
                var item = Instantiate(itemPrefab, transform);
                item.SetActive(false);
                cells.Add(item);
            }

            for (var index = 0; index < cells.Count; index++)
            {
                var active = index < activeCount;
                if (cells[index].activeSelf != active)
                {
                    cells[index].SetActive(active);
                }

                if (active)
                {
                    onItemRefresh?.Invoke(cells[index].transform, index);
                }
            }
        }

        public void ClearCells()
        {
            totalCount = 0;
            cells.RemoveAll(cell => cell == null);
            foreach (var cell in cells)
            {
                cell.SetActive(false);
            }
        }

        public List<Transform> GetDraggableItems()
        {
            cells.RemoveAll(cell => cell == null);
            return cells
                .Where(cell => cell.activeSelf)
                .Select(cell => cell.transform)
                .ToList();
        }

        public Transform GetContentTransform()
        {
            return transform;
        }
    }
}
