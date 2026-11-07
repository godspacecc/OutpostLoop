namespace Game
{
    /// <summary>
    /// 包装底层资源加载 Handle，调用方通过它归还异步资源，空 Handle 表示无需释放。
    /// </summary>
    public readonly struct ResourceLoadHandle
    {
        public static readonly ResourceLoadHandle Empty = new ResourceLoadHandle(null);

        public ResourceLoadHandle(object rawHandle)
        {
            RawHandle = rawHandle;
        }

        public object RawHandle { get; }
        public bool IsValid => RawHandle != null;
    }
}
