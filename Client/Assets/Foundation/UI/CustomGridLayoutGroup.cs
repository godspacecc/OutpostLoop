using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Game
{
    /// <summary>
    /// 扩展网格布局以按序列化预制体填充子项，并通过刷新回调交给外部绑定数据。
    /// </summary>
    [AddComponentMenu("UI/Custom Grid Layout Group")]
    public class CustomGridLayoutGroup : GridLayoutGroup, IDraggableContainer
    {
        public GameObject gridPrefab;
        private readonly List<GameObject> _ownedItems = new List<GameObject>();
        private Action<Transform, int> _onItemRefresh;

        public int totalCount { get; set; }

        public void AddItemRefreshListener(Action<Transform, int> onItemRefresh)
        {
            _onItemRefresh = onItemRefresh;
        }

        public void LoadAndFillGrid(int itemCount = 10)
        {
            totalCount = Mathf.Max(0, itemCount);
            RefreshCells();
        }

        public void ClearGrid()
        {
            totalCount = 0;
            RemoveDestroyedItems();
            SetOwnedItemsActive(0);
        }

        public void RefreshCells()
        {
            RemoveDestroyedItems();
            var activeCount = Mathf.Max(0, totalCount);
            if (activeCount == 0)
            {
                SetOwnedItemsActive(0);
                return;
            }

            if (gridPrefab == null)
            {
                Log.Error("[CustomGridLayoutGroup] 网格预制体缺失。");
                return;
            }

            if (_onItemRefresh == null)
            {
                Log.Error("[CustomGridLayoutGroup] 刷新监听缺失。");
                return;
            }

            while (_ownedItems.Count < activeCount)
            {
                var item = Instantiate(gridPrefab, transform);
                item.SetActive(false);
                _ownedItems.Add(item);
            }

            // Owned items always follow authored/static content while retaining cache order.
            foreach (var item in _ownedItems)
            {
                item.transform.SetAsLastSibling();
            }

            SetOwnedItemsActive(activeCount);
            for (var index = 0; index < activeCount; index++)
            {
                _onItemRefresh(_ownedItems[index].transform, index);
            }
        }

        public List<Transform> GetDraggableItems()
        {
            RemoveDestroyedItems();
            return _ownedItems
                .Where(item => item.activeSelf)
                .Select(item => item.transform)
                .ToList();
        }

        public Transform GetContentTransform()
        {
            return transform;
        }

        protected override void OnDestroy()
        {
            RemoveDestroyedItems();
            foreach (var item in _ownedItems)
            {
                if (Application.isPlaying)
                {
                    Destroy(item);
                }
                else
                {
                    DestroyImmediate(item);
                }
            }

            _ownedItems.Clear();
            base.OnDestroy();
        }

        private void RemoveDestroyedItems()
        {
            _ownedItems.RemoveAll(item => item == null);
        }

        private void SetOwnedItemsActive(int activeCount)
        {
            for (var index = 0; index < _ownedItems.Count; index++)
            {
                var shouldBeActive = index < activeCount;
                if (_ownedItems[index].activeSelf != shouldBeActive)
                {
                    _ownedItems[index].SetActive(shouldBeActive);
                }
            }
        }
    }
}
