# 独立配置
唯一生成入口 `gen.bat` 同时生成 Client 和 Server 配置，不启动服务器。需要 .NET 8 runtime。修改 Defines/DataCsv 后再生成，勿改生成代码或 bytes。

工具独立复制自参考工程的 Luban 发行文件；上游 https://github.com/focus-creative-games/luban ，运行时源保留 Code Philosophy MIT 许可头。工具包含第三方依赖，保留原分发依赖，不包含任何旧游戏配置或数据。

运行时程序集 OutpostLoop.Config；调用 PrototypeConfig.Load(name => bytes)；name 为 tbrule.bytes。加载异常应阻止开局，不回退旧常量。Server 目录仅供配置生成输出。
