using System;
using System.Collections.Generic;
using Game;

namespace UnityEngine.UI
{
    public interface LoopScrollPrefabSource
    {
        GameObject GetObject(int index);

        void ReturnObject(Transform trans, bool isDestroy = false);
    }

    [Serializable]
    public class LoopScrollPrefabSourceInstance : LoopScrollPrefabSource
    {
        public GameObject prefab;
        public int poolSize = 5;

        private readonly Stack<GameObject> _inactivePool = new Stack<GameObject>();
        private Transform _poolRoot;

        public virtual GameObject GetObject(int index)
        {
            try
            {
                if (prefab == null)
                {
                    Debug.LogError("LoopScroll prefab is not assigned.");
                    return null;
                }

                EnsurePoolWarm();

                GameObject instance = _inactivePool.Count > 0
                    ? _inactivePool.Pop()
                    : UnityEngine.Object.Instantiate(prefab);

                instance.SetActive(true);
                return instance;
            }
            catch (Exception e)
            {
                Debug.LogError(e);
                return null;
            }
        }

        public virtual void ReturnObject(Transform go, bool isDestroy = false)
        {
            try
            {
                if (go == null)
                {
                    return;
                }

                if (isDestroy)
                {
                    UnityEngine.Object.Destroy(go.gameObject);
                    return;
                }

                go.gameObject.SetActive(false);
                go.SetParent(GetPoolRoot(), false);
                _inactivePool.Push(go.gameObject);
            }
            catch (Exception e)
            {
                Debug.LogError(e);
            }
        }

        private void EnsurePoolWarm()
        {
            var root = GetPoolRoot();
            if (root == null)
            {
                return;
            }

            while (_inactivePool.Count < poolSize)
            {
                GameObject instance = UnityEngine.Object.Instantiate(prefab, root, false);
                instance.SetActive(false);
                _inactivePool.Push(instance);
            }
        }

        private Transform GetPoolRoot()
        {
            if (_poolRoot != null)
            {
                return _poolRoot;
            }

            var poolRoot = GameObjectPoolHelper.GetOrCreatePoolRoot();
            if (poolRoot == null)
            {
                return null;
            }

            var root = new GameObject("LoopScrollPool");
            root.transform.SetParent(poolRoot, false);
            _poolRoot = root.transform;
            return _poolRoot;
        }
    }
}
