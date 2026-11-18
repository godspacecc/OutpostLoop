# POLYGON Prototype 首批接入

2026-11-18，用户在查看官方原图、包内容及用途分析后明确回复「那就用这个吧」，确认使用 POLYGON Prototype Pack。此前 City 优先候选不再作为本轮玩家选型决定，Sunbox 写实改造暂缓。

## 开工卡

| 项目 | 本批范围 |
| --- | --- |
| 目标 | 将现有玩家胶囊体替换为所选资源人形，建立可实际观察的待机、步行和冲刺表现 |
| 依据 | 用户本次资源选择；河谷镇整图 v0.5 及既有探索入口规则 |
| 玩家路径 | Boot 开始／继续 → 河谷镇 → WASD 行走、Shift 冲刺 → 停下及打开暂停／区域图 → 返回行动 |
| 规则与异常 | 位置、速度、碰撞与存档仍由既有领域层负责；关闭根运动；首次落地和瞬移不算行走；暂停窗口冻结动画 |
| 受影响范围 | 人物 prefab、材质、动画控制器、动画模块依赖和表现脚本；不改变地图、物资、战斗判定和保存格式 |
| 验收 | 骨骼与材质引用检查、三个动作采样、主项目编译及 Boot 可玩入口；截图和日志在 `Backup/Artifacts/PrototypePack-20261118/` |

## 资源来源与实现

- 用户指定来源为 Unity Package Manager → My Assets。本机已下载缓存通过内部 `Assets/PolygonPrototype/` 路径定位；并非把缓存当作全部资源库。
- 选用 `Character_Male_Face_01_FixedScale`，保留原网格、色板和黄色原型外观，派生材质使用内置 Standard 着色器。归一化静态高度 1.8 米，根节点在脚底。
- 动作来自用户已有 Human Basic Motions FREE，选择男性 Idle01、Walk01_Forward、Run01_Forward 原地片段；Prototype 自身不包含动画。三个片段保存为独立动画资产，不复制供应者控制器或脚本。
- 运行时只从位置差读取速度与朝向；既有领域移动仍决定位置，Animator 禁用根运动。使用原有四米步速和七米冲刺速度映射动画混合，不调整领域数值。
- 本批先接入玩家；感染者及河谷镇建筑、道具尚未整体替换。不把资源选定当作所有地图美术已完成。

## 验收状态

- 已验证：源 Avatar 为有效 Humanoid；三个片段均为 humanMotion；独立编辑器采样共 36 帧，非真实键鼠试玩录像。
- 主项目编译通过。手动从 Boot 点击开始，进入河谷镇核对人形显示和暂停／恢复；原生键盘短按未作为持续行走验收依据。
- 限定完整方法 `OutpostLoop.Tests.PrototypePlayerVisualTests.BootPlayerMovesAnimatesFreezesAndResumes`，最多 1 项，实际 1 项通过。通过 TestRunnerApi 执行 EditMode 协程并进入真实 Play，使用虚拟键盘持续注入行走、Shift 冲刺，断言领域位置与模型一致、动画进入相应区间、暂停冻结局时／位置／动画、恢复继续、区域图冻结及保存续玩重建。完整 XML 包含通过项，位于批次目录 `test-results.xml`。测试存档使用批次独立目录。
- `main-player.png` 为人工操作的编辑器 Game 视图；`main-verified.png` 为该集成测试期间的主游戏相机渲染，未包含 IMGUI HUD。`idle.gif`、`walk.gif`、`run.gif` 为独立近景蒙皮采样，不是真实键鼠录屏。
- 本批验收覆盖首批接入；步速与脚步完全同步、持物攻击／交互动作、感染者和整镇资源替换尚未完成，不标记完整美术交付。男性黄色基础人形是本轮已选包的首批使用，不延伸为剧情性别或全部角色外观定稿。
- 不执行 Player Build 或发布。
