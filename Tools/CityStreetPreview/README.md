# 独立街段样片生成工具

仅用于临时视觉评审，不属于运行时。资源取自用户已有 City Pack，来源与适配界限见 `Docs/CityPack商住街段样片-20261118.md`。

从仓库根运行 `python Tools/CityStreetPreview/extract_selected.py`，将选件与依赖补入本仓库 Backup/Artifacts 下已有独立 PreviewProject；不覆盖已存在资源。把 CityStreetPreview.cs 复制到该项目 Assets/Editor，使用 Unity 编辑器的 `-batchmode -projectPath ... -executeMethod CityStreetPreview.Run -quit -logFile ...` 生成。不能传 `-nographics`，渲染需要 GPU。此命令不构建 Player。

输出为 Backup/Artifacts/CityStreetPreview-20261118 下三张 PNG、来源清单、验证日志；场景位于独立项目 Assets/CityStreetPreview/CityStreetPreview.unity。需已有 City 夹克资源，工具缺项会失败，不自动下载或替换来源。
