using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityEngine.UI
{
    [AddComponentMenu("UI/Loop Grid Layout Group", 52)]
    [DisallowMultipleComponent]
    public class LoopGridLayoutGroup : LoopVerticalScrollRect
    {
        public Vector2 cellSize = new Vector2(88f, 72f);
        public Vector2 spacing = new Vector2(8f, 8f);
        public RectOffset padding;
        public int constraintCount = 5;

        protected override void Awake()
        {
            if (!HasValidGridWiring())
            {
                enabled = false;
                return;
            }

            InitializeGridLayout();
            base.Awake();
        }

        public void EnsureGridContent()
        {
            HasValidGridWiring();
        }

        public new void AddItemRefreshListener(Action<Transform, int> scrollMoveEvent)
        {
            if (scrollMoveEvent == null)
            {
                Debug.LogError("scrollMoveEvent is error!");
                return;
            }

            if (dataSource == null)
            {
                dataSource = new LoopScrollDataSourceInstance();
            }

            dataSource.scrollMoveEvent = scrollMoveEvent;
        }

        public void RefreshCells(bool refillFromTop)
        {
            if (!HasValidGridWiring())
            {
                return;
            }

            if (Application.isPlaying && isActiveAndEnabled)
            {
                if (refillFromTop)
                {
                    RefillCells(0);
                }
                else
                {
                    base.RefreshCells();
                }

                return;
            }
        }

        public new void RefreshCells()
        {
            RefreshCells(true);
        }

        public new void ClearCells()
        {
            if (!HasValidGridWiring())
            {
                return;
            }

            if (Application.isPlaying)
            {
                base.ClearCells();
            }
            else
            {
                Debug.LogWarning($"[{nameof(LoopGridLayoutGroup)}] 编辑态不刷新或清理列表项，避免修改宿主 Prefab 的静态子物体。");
            }
        }

        public new Transform GetContentTransform()
        {
            return HasValidGridWiring() ? content : null;
        }

        public new List<Transform> GetDraggableItems()
        {
            if (!HasValidGridWiring())
            {
                return new List<Transform>();
            }

            var items = new List<Transform>();
            foreach (Transform child in content)
            {
                items.Add(child);
            }

            return items;
        }

        private bool HasValidGridWiring()
        {
            if (prefabSource == null || prefabSource.prefab == null)
            {
                Debug.LogError($"[{nameof(LoopGridLayoutGroup)}] prefabSource.prefab 为空，无法刷新网格。");
                return false;
            }

            if (viewport == null)
            {
                Debug.LogError($"[{nameof(LoopGridLayoutGroup)}] 缺少 Viewport，宿主 Prefab 必须预先创建。");
                return false;
            }

            if (content == null)
            {
                Debug.LogError($"[{nameof(LoopGridLayoutGroup)}] 缺少 Content，宿主 Prefab 必须预先创建。");
                return false;
            }

            if (content.parent != viewport)
            {
                Debug.LogError($"[{nameof(LoopGridLayoutGroup)}] Content 必须挂在 Viewport 下。");
                return false;
            }

            if (viewport.GetComponent<RectMask2D>() == null)
            {
                Debug.LogError($"[{nameof(LoopGridLayoutGroup)}] Viewport 缺少 RectMask2D。");
                return false;
            }

            if (content.GetComponent<GridLayoutGroup>() == null)
            {
                Debug.LogError($"[{nameof(LoopGridLayoutGroup)}] Content 缺少 GridLayoutGroup。");
                return false;
            }

            return true;
        }

        private void InitializeGridLayout()
        {
            horizontal = false;
            vertical = true;
            EnsurePadding();
            ConfigureContentRect(content);
            ConfigureGrid(content.gameObject);
        }

        private void ConfigureGrid(GameObject contentObject)
        {
            var grid = contentObject.GetComponent<GridLayoutGroup>();
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = constraintCount;
            grid.cellSize = cellSize;
            grid.spacing = spacing;
            grid.padding = padding;
        }

        private void EnsurePadding()
        {
            if (padding == null)
            {
                padding = new RectOffset(8, 8, 8, 8);
            }
        }

        private static void ConfigureContentRect(RectTransform contentTransform)
        {
            contentTransform.anchorMin = new Vector2(0f, 1f);
            contentTransform.anchorMax = new Vector2(1f, 1f);
            contentTransform.pivot = new Vector2(0.5f, 1f);
            contentTransform.anchoredPosition = Vector2.zero;
            contentTransform.sizeDelta = new Vector2(0f, contentTransform.sizeDelta.y);
        }
    }
}
