# OutpostLoop（临时代号）

独立的限时单局探索、采集、升级与自动防守原型。Unity **2022.3.55f1c1**，工程目录 `Client`。

## 启动
1. Unity Hub 添加本仓库的 `Client` 目录。
2. 打开 `Assets/Game/Scenes/Boot.unity`。
3. 点击 Play，再点击游戏内“开始这一局”。推荐 Game 视图 16:9、1280×720 或更大。
4. Boot 负责资源、代码加载和启动错误恢复；Main 保存基础根与原型入口。开发时默认使用编辑器源资源模式，不生成资源包。

## 操作
- WASD/方向键移动，靠近资源按住 E 采集。
- 棕色堆为废料，青色晶体为能源。靠近中央基地自动卸货。
- 基地附近按 B 打开升级。第一档各消耗 8 废料，后续需要更多废料与能源。
- 炮塔自动攻击；采集运输、火力射程、基地耐久三条路线各三级。
- ESC 暂停/继续。升级面板暂停局时。
- 12 分钟一局，进攻时间为 2/4/6/8/10/11 分钟；到期基地存活且无残余敌人则获胜。
- 胜负界面可换种子重开。没有局外成长与旧玩家数据。

## 当前交付边界
这是**编辑器可玩原型**，不是发布版本。全部几何体、材质与调试 UI 独立创建，未使用 FirepowerLoop 的视觉资源。高位镜头和新概念方向已讨论；实际模型、动作和射击短线仍为临时可玩预览，不代表正式美术验收。

地图半径限制 46 米；36 个资源点；敌人固定池 48；不生成无限新区。边缘装饰不碰撞，首版没有自由建造、居民模拟、寻路障碍、音频、触屏输入或中途存档。手机性能与安装包尚未验证，未运行 Player Build。

## 实现与验证
已经补齐适合独立项目的通用开发基础，源码独立维护，不与 FirepowerLoop 自动同步：

- QFramework 的 Model/System、事件、统一生命周期、日志和计时器。原型由 Model 保存本局，System 推进规则，Controller 提交操作并显示画面。
- 独立 Luban 配置源 `Config/DataCsv/rules.csv`。运行 `Config/gen.bat` 会校验并生成客户端与服务端配置产物；`Server` 包含生成配置与通用资源服脚本模板，没有旧服务器业务或玩家数据。
- YooAsset 2.3.17、独立 OutpostLoop 收集设置；公共 IResourceLoader/ResourceLoader/ResourceLoadHandle/YooAssetResourceLoader 与 FirepowerLoop 对齐。当前编辑器通过同一公共接口的 EditorSourceResourceLoader 后端读取 `tbrule.bytes`，真实收集地址和 `startup` 标签已检查。尚无已构建清单，因此资源包运行加载未验证；不触发模拟构建或 Player Build。
- BaseUI、Box/Page 控制器、UIRoot 空层、列表与对象池、ReferenceCollector 和 UI 代码生成器。默认 UI 加载转发公共 ResourceLoader，控制器 Path 返回 UI 根下相对路径，统一地址解析后加载，并自动接入 SafeAreaFitter。未复制旧项目主题或 UI Prefab，游戏画面仍是原型 IMGUI。
- 独立 JSON 文件存储：区分缺失、损坏、未知版本，完整写入后替换旧文件。这里只提供通用能力，没有增加游戏中途存档或局外成长。
- UniTask、Input System、UGUI、TMP 等依赖由本工程自行安装。没有复制旧 Library、UserSettings、MCP 配置或玩家数据。

## 编辑器工具

| 入口 | 用途 |
|---|---|
| `Tools > 开发 > 初始化Boot与Main` | 初始化独立临时 Boot 界面、Main 基础根和资源收集配置 |
| `Tools > 配置 > 生成双端配置` | 从权威配置源双端生成 |
| `Tools > 诊断 > 基础诊断` | 检查配置、源收集和基础状态 |
| `Tools > 诊断 > 检查资源源收集` | 查询真实 YooAsset 源收集结果，不构建资源包 |
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
| `Tools > 打包 > 构建前检查` | 只读检查场景、收集配置、HybridCLR、平台模块与安装器依赖，并输出缺失项 |
| `Tools > 打包 > PC` | 完整包、复用全部/复用 AOT 快速包、代码与资源热更新、重建内置资源、安装器 |
| `Tools > 打包 > Android` | 完整包、宿主基线、首次下载资源启动包、两类快速包、代码与资源热更新 |
| `Tools > 服务器 > PC / Android` | 生成、更新、启动独立资源服 |
| `Tools > 服务器 > 资源更新公告` | 管理通用资源更新公告 |

构建沿用宿主兼容身份、不可变 AOT 基线、工作区互斥锁、持久版本预留与脚本重载恢复。首次完整包需要本项目自己的 HybridCLR 本地 IL2CPP 安装和生成产物；快速打包与热更新必须使用本项目已成功生成的兼容基线，不能使用参考项目的产物。Windows 安装器还需要 Inno Setup，Android 需要对应 SDK/NDK/JDK。资源地址初始为本机 `http://127.0.0.1:8000`，不继承旧项目服务地址。

本轮授权仅实现链路并验证编辑器流程，未实际生成 Player、资源包、安装器或发布资源服；不能据此宣称安装包、真机热更与安装流程已通过。验证证据位于 `Backup/Artifacts/BootMainBuild-20261107/`。
