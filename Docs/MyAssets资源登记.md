# My Assets 资源登记

2026-11-13 · 部分登记，未完成全量盘点。来源为用户指定的 Unity Package Manager → My Assets，不以本机下载缓存代替账号列表。

## 读取范围与当前阻塞

当前界面排序为 Name (asc)，搜索框为空，显示 18 of 164。将加载数量从 25 改为 All 后，界面出现 Error refreshing assets；点击 Load 重试一次后仍失败。Unity Console 显示 Package Manager 获取 access token 时 Unable to parse http response。

这证明当前无法继续加载，不证明资源已丢失，也不能认定 164 是实时刷新后的账号总量。没有读取或导出 token，没有自动操作登录界面。需要用户在 Unity Hub／编辑器中确认登录和网络，使 My Assets 能正常刷新，然后再接续全量登记。

截图位于 `Backup/Artifacts/MyAssets-20261113/refresh-error.png` 与 `starter-assets.png`。本批未下载、重新下载或导入资源，未修改项目渲染管线和控制器，未运行 Unity 测试。

## 当前可见 18 项

包名前缀均为界面实际显示的 CN Asset。带省略号的名称／版本保持截断，不补猜；发布者仅在实际打开详情后记录。列表图标不据此断言已导入本项目。

| 序号 | 界面显示名称 | 可读版本 | 核查范围 |
| --- | --- | --- | --- |
| 1 | CN Asset - 3D Scifi Kit Vol 3 | 1.0.3 | 详情已见：发布者 Creepy-Cat，按钮 Download |
| 2 | CN Asset - Bakery - GPU Lightmapper | 1.9.8 | 列表可见 |
| 3 | CN Asset - DOTween (HOTween v2) | 1.3.0.45 | 列表可见 |
| 4 | CN Asset - Dragon Crashers - UI Toolkit示… | 截断，待核对 | 列表可见 |
| 5 | CN Asset - Fantasy Inventory Icons [Free] - … | 1.2.0 | 列表可见 |
| 6 | CN Asset - For Unity Learn：3D Game Kit | 1.0.0 | 列表可见 |
| 7 | CN Asset - GUI Pro - Fantasy RPG | 1.0.0 | 列表可见 |
| 8 | CN Asset - GUI Pro - Survival Clean | 1.0.0 | 列表可见 |
| 9 | CN Asset - Love/Hate | 1.10.75 | 列表可见 |
| 10 | CN Asset - Odin Inspector and Serializer | 4.0.2 | 列表可见 |
| 11 | CN Asset - POLYGON - Icons Pack - Art by S… | 1.2.0 | 列表可见 |
| 12 | CN Asset - POLYGON - Prototype Pack - Art … | 1.9.0 | 列表可见 |
| 13 | CN Asset - POLYGON - 城市资源包(City Pack)… | 截断，待核对 | 列表可见，与历史 City 采用记录后续核对 |
| 14 | CN Asset - Robot Kyle \| URP | 5.0.0 | 列表可见 |
| 15 | CN Asset - Starter Assets - ThirdPerson \| URP 初始资产 - 第三人称控制器 | 1.0.0 | 提示与详情已见：发布者团结引擎，Import／Re-Download 按钮，70.02 MB、147 文件 |
| 16 | CN Asset - UI Samples | 1.2.3 | 列表可见 |
| 17 | CN Asset - 低多边形简约自然套装 | 1.0.0 | 列表可见 |
| 18 | CN Asset - 坦克对战 \| 完整项目 | 1.0.0 | 列表可见 |

## 玩家相关的初步筛选

- Starter Assets：优先核查能否提供独立运动样片的人体、骨骼和基础动作。详情提示含 Input System 与 Cinemachine 依赖，不能整包导入覆盖现有控制与相机；URP 材质是否适配现有管线须另验。Import 按钮不等于已经导入当前项目。
- City Pack：结合既有缓存索引继续查看人物和服装是否适合；只确认存在此条目，未据此确定角色外形。
- Robot Kyle：可评估骨骼技术样片用途，机器人外形不直接作为“普通人”主角造型。
- 3D Game Kit：先核对角色与动作内容、适用范围，再决定是否作为参考，尚未检查内部文件。
- 其余可见项暂未显示为玩家角色动作包。尚未加载的条目可能包含更合适的资源，当前不能得出“没有更多角色／动画资源”的结论。

Kevin Iglesias、Sunbox 与 Explosive 等已下载包仍保留在[缓存子清单](本机Unity缓存资源清单.md)；本次未在这 18 条中核对到它们，不将其冒充为本次 My Assets 已读取条目。

## 接续点

恢复 My Assets 刷新后，以 All 加载，确认实际已加载数量，再逐项补全名称、发布者、版本、下载状态；优先筛选玩家资源并与 PA-01 至 PA-14 动作需求映射。全量登记与动作适配目前均未完成。
