using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game
{
    /// <summary>
    /// 定义对象池容量不足时的膨胀策略，控制是拒绝、单个扩容还是批量扩容。
    /// </summary>
    public enum PoolInflationType
    {
        Increment,
        Double
    }

    /// <summary>
    /// 管理 GameObject 实例池的创建、取出、回收和销毁，池对象生命周期由该池统一持有。
    /// </summary>
    public sealed class GameObjectPool
    {
        private readonly Stack<PoolObject> _availableObjects = new Stack<PoolObject>();
        private readonly GameObject _prefab;
        private readonly PoolInflationType _inflationType;
        private readonly string _poolName;
        private readonly Transform _root;
        private int _objectsInUse;

        public GameObjectPool(
            string poolName,
            GameObject prefab,
            Transform root,
            int initialCount,
            PoolInflationType inflationType)
        {
            _poolName = poolName;
            _prefab = prefab;
            _inflationType = inflationType;

            var rootObject = new GameObject(poolName + "Pool");
            rootObject.transform.SetParent(root, false);
            _root = rootObject.transform;

            Populate(Mathf.Max(1, initialCount));
        }

        public GameObject NextAvailableObject(bool autoActive)
        {
            while (_availableObjects.Count > 0 && _availableObjects.Peek() == null)
            {
                _availableObjects.Pop();
            }

            if (_availableObjects.Count == 0)
            {
                Inflate();
            }

            if (_availableObjects.Count == 0)
            {
                return null;
            }

            var poolObject = _availableObjects.Pop();
            poolObject.IsPooled = false;
            _objectsInUse++;

            var go = poolObject.gameObject;
            if (autoActive)
            {
                go.SetActive(true);
            }

            return go;
        }

        public void ReturnObjectToPool(PoolObject poolObject)
        {
            if (poolObject == null || poolObject.PoolName != _poolName || poolObject.IsPooled)
            {
                return;
            }

            _objectsInUse = Mathf.Max(0, _objectsInUse - 1);
            AddObjectToPool(poolObject);
        }

        public void Destroy()
        {
            if (_root == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Object.Destroy(_root.gameObject);
            }
            else
            {
                Object.DestroyImmediate(_root.gameObject);
            }
        }

        private void Inflate()
        {
            var count = _inflationType == PoolInflationType.Increment ? 1 : Mathf.Max(1, _objectsInUse);
            Populate(count);
            GameObjectPoolHelper.NotifyInflate(count);
        }

        private void Populate(int count)
        {
            if (_prefab == null || _root == null)
            {
                return;
            }

            for (var i = 0; i < count; i++)
            {
                var go = Object.Instantiate(_prefab);
                var poolObject = go.GetComponent<PoolObject>() ?? go.AddComponent<PoolObject>();
                poolObject.PoolName = _poolName;
                AddObjectToPool(poolObject);
            }
        }

        private void AddObjectToPool(PoolObject poolObject)
        {
            var go = poolObject.gameObject;
            go.name = _poolName;
            go.SetActive(false);
            go.transform.SetParent(_root, false);
            poolObject.IsPooled = true;
            _availableObjects.Push(poolObject);
        }
    }
}
