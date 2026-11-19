# City Pack 河谷镇表现资源

用户在 2026-11-18 认可商住街段样片后，要求替换河谷镇中能使用 City 资源的其他部分。采用范围和验收见 Docs/CityPack河谷镇替换-20261118.md。

1. 从仓库根运行 `python Tools/CityTownArt/extract_selected.py`，在 Backup/Artifacts 下已有独立预览项目补入用户缓存的 40 个候选原件及依赖，不覆盖已有文件。
2. 将 Client/Assets/Game/Scripts/Gameplay/RiverValleyTown 下的 RiverValleyCityArtCatalog.cs、RiverValleyMeshLifetime.cs 复制到独立项目 Assets；把 CityTownArtBuilder.cs 放入 Assets/Editor。
3. 在该独立项目用 Unity 编辑器 `-batchmode -executeMethod CityTownArtBuilder.Run -quit` 烘焙；`-logFile` 指向本批 Backup/Artifacts。命令不构建 Player。
4. 运行 `python Tools/CityTownArt/sync_export.py`，只同步派生网格、材质、贴图及目录，修正 ScriptableObject 的主项目脚本 GUID；不向 Client 导入原包控制器或 Shader Graph。

当前 40 个候选原件另有一份侧向墙体网格，合计 41 个目录项。单位网格保留原 UV，材质派生为当前内置管线 Standard，仅复制需要的原贴图。运行时负责按已确认地图坐标拼装和合批，不从原包演示场景复制布局。没有兼容原件的内容保留原表现。

2026-11-18 环境细化：选件脚本可追加证据目录参数，如 `python Tools/CityTownArt/extract_selected.py Backup/Artifacts/CityTownDressing-20261118`。烘焙前将 `OUTPOST_ART_BATCH` 设为同一批次目录名，避免覆盖旧报告。停车线原件带整片路面，本轮仅核查参考；实际车位线由场景按地形生成。新陈设与证据见 Docs/河谷镇环境细化-20261118.md。
