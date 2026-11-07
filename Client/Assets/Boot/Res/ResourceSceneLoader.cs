using Cysharp.Threading.Tasks;
using Debug = UnityEngine.Debug;
using Scene = UnityEngine.SceneManagement.Scene;
using SceneManager = UnityEngine.SceneManagement.SceneManager;
using LoadSceneMode = UnityEngine.SceneManagement.LoadSceneMode;
using YooAsset;

namespace Boot
{
    /// <summary>
    /// 参与 Boot 启动链的 ResourceSceneLoader，在热更代码加载前完成资源、配置或界面衔接。
    /// </summary>
    public static class ResourceSceneLoader
    {
        private const string MAIN_SCENE_ADDRESS = "Scenes/Main";
        private static SceneHandle _sceneHandle;

        public static async UniTask<bool> LoadMainAsync()
        {
            var package = YooAssets.GetPackage("OutpostLoop");
            if (package == null)
            {
                Debug.LogError("[ResourceSceneLoader] 未找到资源包 OutpostLoop。");
                return false;
            }

            if (!package.CheckLocationValid(MAIN_SCENE_ADDRESS))
            {
                Debug.LogError($"[ResourceSceneLoader] 场景地址无效: {MAIN_SCENE_ADDRESS}");
                return false;
            }

            ReleaseCurrent();

            _sceneHandle = package.LoadSceneAsync(MAIN_SCENE_ADDRESS, LoadSceneMode.Single);
            await _sceneHandle.WaitAsync();

            if (_sceneHandle.Status != EOperationStatus.Succeed)
            {
                Debug.LogError($"[ResourceSceneLoader] 场景加载失败: {MAIN_SCENE_ADDRESS}");
                return false;
            }
            return true;
        }

        public static void ReleaseCurrent()
        {
            if (_sceneHandle != null)
            {
                _sceneHandle.Release();
                _sceneHandle = null;
            }
        }

        public static Scene GetLoadedScene()
        {
            if (_sceneHandle == null || _sceneHandle.Status != EOperationStatus.Succeed)
                return default;

            return _sceneHandle.SceneObject;
        }

        public static string GetLoadedSceneName()
        {
            var scene = GetLoadedScene();
            return scene.IsValid() ? scene.name : null;
        }
    }
}
