using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game
{
    /// <summary>
    /// 按对象池名维护全局池实例，提供创建、取出、回收和清理入口。
    /// </summary>
    public static class GameObjectPoolHelper
    {
        private static readonly Dictionary<string, GameObjectPool> Pools = new Dictionary<string, GameObjectPool>();
        private static Transform _poolRoot;
        private static int _totalInflateCount;

        public static int TotalInflateCount => _totalInflateCount;

        internal static void NotifyInflate(int count)
        {
            _totalInflateCount += count;
        }

        public static void InitPool(
            GameObject prefab,
            string poolName,
            int size,
            PoolInflationType type = PoolInflationType.Double)
        {
            if (prefab == null || string.IsNullOrWhiteSpace(poolName) || Pools.ContainsKey(poolName))
            {
                return;
            }

            var root = GetOrCreatePoolRoot();
            if (root == null)
            {
                return;
            }

            Pools[poolName] = new GameObjectPool(poolName, prefab, root, size, type);
        }

        public static GameObject GetObjectFromPool(
            GameObject prefab,
            string poolName,
            bool autoActive = true,
            int autoCreate = 0)
        {
            if (!Pools.ContainsKey(poolName) && autoCreate > 0)
            {
                InitPool(prefab, poolName, autoCreate, PoolInflationType.Increment);
            }

            return Pools.TryGetValue(poolName, out var pool) ? pool.NextAvailableObject(autoActive) : null;
        }

        public static void ReturnObjectToPool(GameObject go)
        {
            if (go == null)
            {
                return;
            }

            var poolObject = go.GetComponent<PoolObject>();
            if (poolObject == null || !Pools.TryGetValue(poolObject.PoolName, out var pool))
            {
                return;
            }

            pool.ReturnObjectToPool(poolObject);
        }

        /// <summary>
        /// 移除指定名称的对象池。已借出的对象由调用方拥有，此处只销毁池根及其可用对象。
        /// </summary>
        public static void RemovePool(string poolName)
        {
            if (string.IsNullOrWhiteSpace(poolName) || !Pools.TryGetValue(poolName, out var pool))
            {
                return;
            }

            Pools.Remove(poolName);
            pool.Destroy();
        }

        public static void ClearForTests()
        {
            foreach (var pool in Pools.Values)
            {
                pool.Destroy();
            }

            Pools.Clear();
            _totalInflateCount = 0;

            if (_poolRoot != null)
            {
                if (Application.isPlaying)
                {
                    Object.Destroy(_poolRoot.gameObject);
                }
                else
                {
                    Object.DestroyImmediate(_poolRoot.gameObject);
                }

                _poolRoot = null;
            }
        }

        public static Transform GetOrCreatePoolRoot()
        {
            if (_poolRoot != null)
            {
                return _poolRoot;
            }

            // 池根只属于本工程通用 UI 基础，不依赖旧项目场景根。
            var root = new GameObject("OutpostLoopUIPool");
            if (Application.isPlaying) Object.DontDestroyOnLoad(root);
            _poolRoot = root.transform;
            return _poolRoot;
        }
    }
}
