# OutpostLoop（临时代号）

独立的限时单局探索、采集、升级与自动防守原型。Unity **2022.3.55f1c1**，工程目录 `Client`。

## 启动
1. Unity Hub 添加本仓库的 `Client` 目录。
2. 打开 `Assets/Prototype/Scenes/Boot.unity`。
3. 点击 Play，再点击游戏内“开始这一局”。推荐 Game 视图 16:9、1280×720 或更大。
4. 若未找到场景，用菜单 `OutpostLoop > Create Preview Scene` 创建。

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
- 独立 Luban 配置源 `Config/DataCsv/rules.csv`。运行 `Config/gen.bat` 会校验并生成客户端与服务端配置产物；`Server` 当前只有生成配置，没有服务器业务或数据。
- YooAsset 2.3.17、独立 OutpostLoop 收集设置；公共 IResourceLoader/ResourceLoader/ResourceLoadHandle/YooAssetResourceLoader 与 FirepowerLoop 对齐。当前编辑器通过同一公共接口的 EditorSourceResourceLoader 后端读取 `tbrule.bytes`，真实收集地址和 `startup` 标签已检查。尚无已构建清单，因此资源包运行加载未验证；不触发模拟构建或 Player Build。
- BaseUI、Box/Page 控制器、UIRoot 空层、列表与对象池、ReferenceCollector 和 UI 代码生成器。默认 UI 加载转发公共 ResourceLoader，控制器 Path 返回 UI 根下相对路径，统一地址解析后加载，并自动接入 SafeAreaFitter。未复制旧项目主题或 UI Prefab，游戏画面仍是原型 IMGUI。
- 独立 JSON 文件存储：区分缺失、损坏、未知版本，完整写入后替换旧文件。这里只提供通用能力，没有增加游戏中途存档或局外成长。
- UniTask、Input System、UGUI、TMP 等依赖由本工程自行安装。没有复制旧 Library、UserSettings、MCP 配置或玩家数据。

## 编辑器工具

| 入口 | 用途 |
|---|---|
| `OutpostLoop > Foundation > Prepare Prototype Foundation` | 给 Boot 场景补齐统一生命周期与空 UI 层 |
| `OutpostLoop > Foundation > Generate Config (Client + Server)` | 从权威配置源双端生成 |
| `OutpostLoop > Foundation > Diagnostics` | 检查配置、源收集和基础状态 |
| `OutpostLoop > Resources > Audit Source Collection` | 查询真实 YooAsset 源收集结果，不构建资源包 |
| `Tools > 场景 > 场景切换` | 场景快速切换 |
| Prefab 右键 `智能更新UI/BoxUI/Page` | 按引用收集生成视图/控制器并绑定组件 |

UI 生成目录为 `Assets/Game/Scripts/Controller/`，Prefab 路径为 `Assets/Game/Prefabs/UI/`。正式 UI 必须先经过视觉预览确认；工具测试使用独立测试夹具。

限定验证共 39 项，分 10 个真实测试类执行，包含配置、资源源收集、UI、生成代码编译/引用绑定、存储、架构、原型规则和实际进入 Play 的帧推进/暂停/重开检查。每次只运行一个真实类，声明数量并保留全部通过项，禁止无效过滤回退完整套件。详情见 `Docs/基础迁入验收.md`。

预览、验证与日志在 `Backup/Artifacts/Prototype-20261106/` 和 `Backup/Artifacts/Foundation-20261107/`（本地资料，Git 忽略）。用户已授权独立 GitHub 仓库 `godspacecc/OutpostLoop`，不会推入 FirepowerLoop。

2026-11-07 资源/UI 基础对齐增量验证：21 项限定测试通过，含真实源收集、窗口安全区接入及实际 Play。详见 `Docs/执行总清单.md`；本批证据在 `Backup/Artifacts/FrameworkAlignment-20261107/`。没有验证资源包运行或真机安全区。
