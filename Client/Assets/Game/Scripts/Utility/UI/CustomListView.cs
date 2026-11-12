using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game
{
    /// <summary>
    /// 用于少量固定列表项的简单列表视图，按 totalCount 复用预制体并回调刷新项内容。
    /// </summary>
    [AddComponentMenu("UI/Custom List View")]
    public sealed class CustomListView : MonoBehaviour, IDraggableContainer
    {
        public GameObject itemPrefab;
        public int totalCount;

        private readonly List<Transform> _cachedItems = new List<Transform>();
        private Action<Transform, int> _onItemRefresh;

        public void AddItemRefreshListener(Action<Transform, int> onItemRefresh)
        {
            _onItemRefresh = onItemRefresh;
        }

        public void RefreshCells()
        {
            RemoveDestroyedItems();
            var count = Mathf.Max(0, totalCount);
            if (itemPrefab == null)
            {
                Log.Error("[CustomListView] 列表项预制体缺失。");
                return;
            }

            while (_cachedItems.Count < count)
            {
                var item = Instantiate(itemPrefab, transform);
                _cachedItems.Add(item.transform);
            }

            for (var i = 0; i < _cachedItems.Count; i++)
            {
                var active = i < count;
                _cachedItems[i].gameObject.SetActive(active);
                if (active)
                {
                    _onItemRefresh?.Invoke(_cachedItems[i], i);
                }
            }
        }

        public void ClearCells()
        {
            totalCount = 0;
            RemoveDestroyedItems();
            foreach (var item in _cachedItems)
            {
                item.gameObject.SetActive(false);
            }
        }

        public List<Transform> GetDraggableItems()
        {
            RemoveDestroyedItems();
            var items = new List<Transform>();
            foreach (var item in _cachedItems)
            {
                if (item.gameObject.activeSelf)
                {
                    items.Add(item);
                }
            }

            return items;
        }

        public Transform GetContentTransform()
        {
            return transform;
        }

        private void RemoveDestroyedItems()
        {
            for (var i = _cachedItems.Count - 1; i >= 0; i--)
            {
                if (_cachedItems[i] == null)
                {
                    _cachedItems.RemoveAt(i);
                }
            }
        }
    }
}
