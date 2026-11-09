# OutpostLoop（临时代号）

独立的限时单局轻量即时战略防守原型。Unity **2022.3.55f1c1**，工程目录 `Client`。

[游戏总纲](Docs/游戏总纲.md) · [首版玩法设计送审稿](Docs/首版玩法设计.md) · [开发规程](Docs/开发规程.md) · [执行总清单](Docs/执行总清单.md)

## 启动
1. Unity Hub 添加本仓库的 `Client` 目录。
2. 打开 `Assets/Game/Scenes/Boot.unity`。
3. 点击 Play，再点击游戏内“开始这一局”。推荐 Game 视图 16:9、1280×720 或更大。
4. Boot 负责资源、代码加载和启动错误恢复；Main 保存基础根与原型入口。开发时与参考项目相同，默认使用 YooAsset 编辑器模拟模式，生成模拟清单并校验真实收集地址。

## 操作
- 点击场景或小地图上的资源点派工。棕色堆为废料，青色晶体为能源。工人自动往返，装满或采尽后回基地卸货，改派时先送回手上的货。
- 默认镜头聚焦基地和附近防线。滚轮可拉近查看单位或拉远查看全图。
- HUD 显示本局剩余时间和已发生的波次。敌人入场后可从场景或小地图发现，用四向按钮调动机动小队。
- 按 B 打开基地升级。第一档各消耗 8 废料，后续需要更多废料与能源。采集运输、炮塔火力与射程、基地耐久三条路线各三级。
- 按 V 或点右下角「建造」打开实时侧栏，切换「外围扩建」和「炮位管理」。下单扣费并开始施工，工地上方显示进度条，侧栏显示剩余工期。
- 四个外围地块可建采集站、兵营或路障。采集站完工后自动开采。兵营完工后选中它，点「训练守军」付费下单，训练完成后守军自动在本侧作战。外围工地和建筑被摧毁后可以重建。
- 炮塔自动攻击。基地初始在东、西各有一座炮塔，空位可建造，也可免费迁塔。
- ESC 暂停/继续。升级面板暂停局时。
- 12 分钟一局，进攻时间为 2/4/6/8/10/11 分钟；到期基地存活且无残余敌人则获胜。
- 胜负界面可换种子重开。胜利发放可持久化的补给币，用于购买下一局的起始库存。

## 当前交付边界
这是**编辑器可玩原型**，不是发布版本。全部几何体、材质与调试 UI 独立创建，未使用 FirepowerLoop 的视觉资源。高位镜头和新概念方向已讨论；实际模型、动作和射击短线仍为临时可玩预览，不代表正式美术验收。

地图半径限制 46 米；36 个资源点；敌人固定池 48；不生成无限新区。边缘装饰不碰撞，首版没有自由建造、居民模拟、寻路障碍、正式音效、触屏输入或中途存档。Windows 本地 Player 仅验证过下文的 Boot 批次；未实际安装或进行 Android 真机验收。

## 实现与验证
已经补齐适合独立项目的通用开发基础，源码独立维护，不与 FirepowerLoop 自动同步：

- QFramework 的 Model/System、事件、统一生命周期、日志和计时器。原型由 Model 保存本局，System 推进规则，Controller 提交操作并显示画面。
- 独立 Luban 配置源 `Config/DataCsv/rules.csv`。运行 `Config/gen.bat` 会校验并生成客户端与服务端配置产物；`Server` 包含生成配置与通用资源服脚本模板，没有旧服务器业务或玩家数据。
- YooAsset 2.3.17、独立 OutpostLoop 收集设置；公共 IResourceLoader/ResourceLoader/ResourceLoadHandle/YooAssetResourceLoader 与 FirepowerLoop 对齐。Boot 默认经 YooAsset 模拟包初始化，主场景和配置均通过公共 YooAssetResourceLoader 加载；EditorSourceResourceLoader 仅供独立编辑器诊断/测试使用，不再是 Boot 启动分支。模拟模式仍检查 Game.dll.bytes 和 AOT 清单地址，需要本项目自身生成的启动依赖。
- BaseUI、Box/Page 控制器、UIRoot 分层、列表与对象池、ReferenceCollector 和 UI 代码生成器。默认 UI 加载转发公共 ResourceLoader，控制器 Path 返回完整逻辑地址（如 Prefabs/UI/Probe/Window），由 UIResourceAddress 校验后加载。开局、暂停、基地升级和结算使用 CreateView 生成的临时 UGUI 界面，接入 SafeAreaFitter；状态 HUD 与区域地图仍是原型 IMGUI。
- 同步加载以地址和类型在会话内复用 YooAsset 句柄，UI 和系统退出后统一释放。异步句柄由调用方持有并交回原加载器；切换后端不提前释放旧会话借用的同步资源。
- 运行时日志支持错误落盘和诊断导出；设置支持本地持久化及显示预览回滚；音频支持混音、池化、淡入淡出和会话暂停，尚未接入正式音效素材。
- 独立 JSON 文件存储：区分缺失、损坏、未知版本，完整写入后替换旧文件。胜利补给币跨局保存，单局进度随重开重置。
- UniTask、Input System、UGUI、TMP 等依赖由本工程自行安装。没有复制旧 Library、UserSettings、MCP 配置或玩家数据。

## 编辑器工具

| 入口 | 用途 |
|---|---|
| `Tools > 配置 > 生成双端配置` | 从权威配置源双端生成 |
| `Tools > 诊断 > 检查资源源收集` | 使用现有 YooAsset 设置检查源收集，不修改设置或构建资源包 |
| `Tools > 场景 > 场景切换` | 场景快速切换 |
| Prefab 右键 `智能更新UI/BoxUI/Page` | 按引用收集生成视图/控制器并绑定组件 |

UI 生成目录为 `Assets/Game/Scripts/Controller/`，Prefab 路径为 `Assets/Game/Prefabs/UI/`。正式 UI 必须先经过视觉预览确认；工具测试使用独立测试夹具。

限定验证共 39 项，分 10 个真实测试类执行，包含配置、资源源收集、UI、生成代码编译/引用绑定、存储、架构、原型规则和实际进入 Play 的帧推进/暂停/重开检查。每次只运行一个真实类，声明数量并保留全部通过项，禁止无效过滤回退完整套件。详情见 `Docs/基础迁入验收.md`。

预览、验证与日志在 `Backup/Artifacts/Prototype-20261106/` 和 `Backup/Artifacts/Foundation-20261107/`（本地资料，Git 忽略）。用户已授权独立 GitHub 仓库 `godspacecc/OutpostLoop`，不会推入 FirepowerLoop。

2026-11-07 资源/UI 基础对齐增量验证：21 项限定测试通过，含真实源收集、窗口安全区接入及实际 Play。详见 `Docs/执行总清单.md`；本批证据在 `Backup/Artifacts/FrameworkAlignment-20261107/`。没有验证资源包运行或真机安全区。

基础启动由 `FoundationLoop` / `FoundationRuntime` 负责，先准备资源和架构，再由原型接入玩法；退出统一清理 UI、容器、计时器、事件与资源入口。原型配置读取不再隐式初始化基础。启动清理增量验证 40 项通过，包含场景重载、关闭域重载后重进，以及真实 Play 的窗口卸载顺序；证据位于 `Backup/Artifacts/StartupAlignment-20261107/`。

## Boot/Main 与通用打包链路

稳定宿主程序集为 `Boot`，基础框架与原型编译到 `Game` 热更新程序集；原有 Foundation 与 Prototype 目录通过 asmref 保留代码组织。Build Settings 仅包含 Boot，Main 以 YooAsset 地址 `Scenes/Main` 收集。Main 的 FoundationRoot 跨场景保留，完成初始化后向 Boot 报告就绪，退出 Play 时统一清理。

通用工具基于参考项目迁入，已剔除具体玩法与渠道代码：

| 入口 | 功能 |
|---|---|
| `Tools > 打包 > PC` | 完整包、复用全部/复用 AOT 快速包、代码与资源热更新、重建内置资源、安装器 |
| `Tools > 打包 > Android` | 完整包、宿主基线、首次下载资源启动包、两类快速包、代码与资源热更新 |
| `Tools > 服务器 > PC / Android` | 生成、更新、启动独立资源服 |
| `Tools > 服务器 > 资源更新公告` | 管理通用资源更新公告 |

构建沿用宿主兼容身份、不可变 AOT 基线、工作区互斥锁、持久版本预留与脚本重载恢复。首次完整包需要本项目自己的 HybridCLR 本地 IL2CPP 安装和生成产物；快速打包与热更新必须使用本项目已成功生成的兼容基线，不能使用参考项目的产物。Windows 安装器还需要 Inno Setup，Android 需要对应 SDK/NDK/JDK。资源地址初始为本机 `http://127.0.0.1:8000`，不继承旧项目服务地址。

初次拆分的编辑器验证证据位于 `Backup/Artifacts/BootMainBuild-20261107/`。随后用户单独授权一次本地完整构建、不发布；已生成本项目 Windows 宿主、AOT/Game DLL、资源与安装器，并验证资源服不可达时回退内置资源、进入 Main。Boot 对齐的 35 项限定测试通过，证据位于 `Backup/Artifacts/BootAlignment-20261107/`。未执行安装、Android 真机或发布验证。

后续 UI 对齐以 3DBase 为准：安全区递归适配、Top/Popup 互斥、隐藏窗口置顶、完整 UI 地址/CreateView、列表及生成工具；26 项限定测试全部通过，含实际 Boot→Main 和三项生命周期 Play 检查。此后未再构建 Player，现有本地安装包仅对应前一轮 Boot 验证。证据位于 `Backup/Artifacts/UI3DBaseAlignment-20261107/`。


## Unity MCP

使用 `com.ivanmurzak.unity.mcp`（AI Game Developer），按用户最新要求锁定 OpenUPM 稳定版 `1.7.1`；配套本机服务端由插件管理。本机端口及项目路由由各工程独立生成，不复用其他项目配置。Codex 本地配置和自动生成技能不进入版本库；新设备需在插件中配置本机连接。

当前机器已通过真实 MCP 请求确认连接 OutpostLoop、读取 Boot 场景与 Console，并完成资源刷新。新增 MCP 配置后，当前聊天若仍没有原生工具，需要重启 Codex 后重新检查工具目录。
