# Codex Game Studios 接入

OutpostLoop 在仓库内使用 Codex Game Studios 的完整 Codex 技能目录和岗位参考。主代理负责从确认版本拆出可验收的开发批次，选择技能、协调实现、修复与复验，按执行总清单接续工作。

## 来源与接入内容

- 固定上游版本为 [eiichimo/Codex-Game-Studios @ e9ea497](https://github.com/eiichimo/Codex-Game-Studios/tree/e9ea4975d7b95de8aa27eaa5385b0c5d45640622)。上游 `SKILL.md` 原文位于 `.agents/skills/cgs-*`，岗位资料位于 `.agents/roles/`。许可证和署名位于 `.agents/Codex-Game-Studios-LICENSE` 和 `.agents/Codex-Game-Studios-NOTICE.md`。
- `outpostloop-development` 是项目的执行入口。上游技能说明方法，OutpostLoop 的 `AGENTS.md`、开发规程和用户授权决定当前可以做什么。原文中 `.claude/skills/` 指向上游历史参考，需查阅时用上面的固定版本。
- 上游 `tools/codex-validate.sh` 的钩子、角色映射和模板检查依赖 `.claude` 源文件及上游目录。当前接入用仓库内技能文件检查和实际任务试跑验证。Unity 测试沿用项目的限定验证流程。

## 对应到 OutpostLoop

| 上游工作流 | OutpostLoop 输入与交付 |
| --- | --- |
| `cgs-start`、`cgs-project-stage-detect`、`cgs-help` | 从 `Client/ProjectSettings/ProjectVersion.txt` 读取 Unity 版本，从 `Docs/游戏总纲.md` 和 `Docs/执行总清单.md` 判断进度。源码、场景和测试都从 `Client/` 查找。 |
| `cgs-create-epics`、`cgs-create-stories`、`cgs-sprint-plan`、`cgs-milestone-review` | 首版方案提供交付范围，执行总清单记录里程碑、依赖、当前批次和接续点。按开发规程填必要的开工卡。 |
| `cgs-quick-design`、`cgs-team-level`、`cgs-ux-design` | 查相关确认版本与项目文档。按请求交付设计讨论、布局、界面方案或送审稿，按项目规则记录玩法确认。 |
| `cgs-art-bible`、`cgs-asset-spec`、`cgs-team-audio` | 查已有美术方向与正式确认记录。视觉评审使用项目实机或明确标识的预览，按批次归档。 |
| `cgs-prototype` | 先明确一个要验证的问题。使用独立临时可玩预览，记录体验结论和适用范围。 |
| `cgs-story-readiness`、`cgs-dev-story`、`cgs-code-review`、`cgs-story-done` | 开工卡对应任务，确认方案对应规则，项目现有技术约定对应实现约束。既有记录足够时直接使用。遇到影响多个系统的技术决定，再记录理由。 |
| `cgs-qa-plan`、`cgs-smoke-check`、`cgs-test-evidence-review` | 从确认方案提取验收项，从 Boot 实际进入。按指定非空测试类或完整方法执行，保存通过明细。证据放在 `Backup/Artifacts/`。 |
| `cgs-release-checklist` | 用于已授权目标平台的安装包交付。Windows 首版送审范围对应干净安装、离线启动、两趟行动与重启续档。 |
| `cgs-team-release`、`cgs-launch-checklist`、`cgs-team-live-ops` | 用于实际指定的公开发行、上线准备或运营任务。交付本地安装包按安装验收流程执行。 |

按用户明确指定的交付物选流程。确认范围内的实施、技术选型、测试和修复由 Codex 推进。新增玩法、玩法变更、正式视觉采用和超出既有授权的发布操作，按仓库规则提交相应内容供用户确认。

## 接续与交付

每批以玩家能走通的路径为单位，沿确认范围安排后续任务。按 `Docs/开发规程.md` 完成实现、限定验证与交付。执行总清单记录完成状态、证据和下一项可执行任务，换聊天时从该处继续。

接入试跑用第一趟行动 v0.5 的现有证据核对验收条件。原始 XML 有 10 项通过，脚本在两个种子下以两种走法共完成四趟加速模拟。下一项验收是正常局时的完整键鼠试玩，记录搜索节奏、夜间可读性和撤离压力。详细试跑记录位于 `Backup/Artifacts/CodexGameStudios-20261112/试跑.md`。
