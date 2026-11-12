using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityEngine.UI
{
    [AddComponentMenu("UI/Loop Grid Layout Group", 52)]
    [DisallowMultipleComponent]
    public class LoopGridLayoutGroup : LoopVerticalScrollRect
    {
        public GameObject gridPrefab;
        public Vector2 cellSize = new Vector2(88f, 72f);
        public Vector2 spacing = new Vector2(8f, 8f);
        public RectOffset padding;
        public int constraintCount = 5;

        protected override void Awake()
        {
            EnsureGridContent();
            base.Awake();
        }

        public void EnsureGridContent()
        {
            horizontal = false;
            vertical = true;
            EnsureViewport();

            if (content == null)
            {
                var contentTransform = viewport.transform.Find("Content") as RectTransform;
                if (contentTransform == null)
                {
                    contentTransform = transform.Find("Content") as RectTransform;
                }

                if (contentTransform == null)
                {
                    var contentObject = new GameObject("Content", typeof(RectTransform));
                    contentObject.transform.SetParent(viewport, false);
                    contentTransform = contentObject.GetComponent<RectTransform>();
                }

                content = contentTransform;
            }

            if (content.parent != viewport)
            {
                content.SetParent(viewport, false);
            }

            ConfigureContentRect(content);
            ConfigureGrid(content.gameObject);
            SyncPrefabSource();
        }

        private void EnsureViewport()
        {
            if (viewport == null)
            {
                var viewportTransform = transform.Find("Viewport") as RectTransform;
                if (viewportTransform == null)
                {
                    var viewportObject = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
                    viewportObject.transform.SetParent(transform, false);
                    viewportTransform = viewportObject.GetComponent<RectTransform>();
                }

                viewport = viewportTransform;
            }

            ConfigureViewportRect(viewport);

            if (viewport.GetComponent<RectMask2D>() == null)
            {
                viewport.gameObject.AddComponent<RectMask2D>();
            }
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

        public new void RefreshCells()
        {
            EnsureGridContent();

            if (Application.isPlaying && isActiveAndEnabled)
            {
                RefillCells(0);
                return;
            }

            FillEditorCells();
        }

        public new void ClearCells()
        {
            if (Application.isPlaying)
            {
                base.ClearCells();
                return;
            }

            ClearEditorCells();
            totalCount = 0;
        }

        public new Transform GetContentTransform()
        {
            EnsureGridContent();
            return content;
        }

        public new List<Transform> GetDraggableItems()
        {
            EnsureGridContent();
            var items = new List<Transform>();
            foreach (Transform child in content)
            {
                items.Add(child);
            }

            return items;
        }

        private void ConfigureGrid(GameObject contentObject)
        {
            EnsurePadding();

            var grid = contentObject.GetComponent<GridLayoutGroup>();
            if (grid == null)
            {
                grid = contentObject.AddComponent<GridLayoutGroup>();
            }

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

        private static void ConfigureViewportRect(RectTransform viewportTransform)
        {
            viewportTransform.anchorMin = Vector2.zero;
            viewportTransform.anchorMax = Vector2.one;
            viewportTransform.pivot = new Vector2(0.5f, 0.5f);
            viewportTransform.anchoredPosition = Vector2.zero;
            viewportTransform.sizeDelta = Vector2.zero;
        }

        private void SyncPrefabSource()
        {
            if (gridPrefab == null && prefabSource.prefab != null)
            {
                gridPrefab = prefabSource.prefab;
            }

            if (gridPrefab != null)
            {
                prefabSource.prefab = gridPrefab;
            }
        }

        private void FillEditorCells()
        {
            ClearEditorCells();

            if (gridPrefab == null || content == null)
            {
                return;
            }

            for (var index = 0; index < totalCount; index++)
            {
                var item = Instantiate(gridPrefab, content, false);
                item.SetActive(true);
                dataSource.ProvideData(item.transform, index);
            }
        }

        private void ClearEditorCells()
        {
            if (content == null)
            {
                return;
            }

            var children = new List<Transform>();
            foreach (Transform child in content)
            {
                children.Add(child);
            }

            foreach (var child in children)
            {
                DestroyImmediate(child.gameObject);
            }
        }
    }
}
