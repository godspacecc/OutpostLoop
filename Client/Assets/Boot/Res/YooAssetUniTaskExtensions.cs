using System;
using Cysharp.Threading.Tasks;
using YooAsset;

namespace Boot
{
    /// <summary>
    /// YooAsset 操作的 UniTask 扩展
    /// </summary>
    public static class YooAssetUniTaskExtensions
    {
        /// <summary>
        /// 等待 AsyncOperationBase 完成
        /// </summary>
        public static async UniTask WaitAsync(this AsyncOperationBase operation)
        {
            if (operation == null)
            {
                return;
            }

            while (!operation.IsDone)
            {
                await UniTask.Yield();
            }
        }

        /// <summary>
        /// 等待 HandleBase 完成
        /// </summary>
        public static async UniTask WaitAsync(this HandleBase handle)
        {
            if (handle == null)
            {
                return;
            }

            while (!handle.IsDone)
            {
                await UniTask.Yield();
            }
        }

        /// <summary>
        /// 获取下载字节进度（0.0 - 1.0）
        /// </summary>
        public static float GetByteProgress(this ResourceDownloaderOperation downloader)
        {
            if (downloader == null || downloader.TotalDownloadBytes == 0)
            {
                return 0f;
            }
            return (float)downloader.CurrentDownloadBytes / downloader.TotalDownloadBytes;
        }
    }
}