# superpowers-zh（项目内置副本）

| 项 | 值 |
| --- | --- |
| 上游 | https://github.com/jnMetaCode/superpowers-zh |
| 导入版本 | commit `78cb4f68d691d516eb7216897053ea574212cdf6`（`package.json` version 1.7.13） |
| 导入日期 | 2026-09-22 |
| 导入方式 | 复制上游 `skills/` 到本项目 `.opencode/skills/` |

## 为什么用「复制」而不是官方插件

OpenCode 的技能发现优先级里，**项目级 `.opencode/skills/` 高于全局兼容目录**（`~/.claude/skills`、`~/.agents/skills`）。
复制进项目后：

- 不依赖运行期网络与插件加载，团队成员拉下仓库即可用；
- 技能与项目版本绑定，改了什么可复核、可回退；
- 本项目 20 个技能定义以本目录为准（全局同名技能被覆盖）。

技能 ID 由目录名决定（`<id>/SKILL.md`），全部 20 个都带 `description`，因此都会出现在模型的可用技能列表里。

## 更新方式

```powershell
$tmp = "$env:TEMP\superpowers-zh"
if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force }
git clone --depth 1 https://github.com/jnMetaCode/superpowers-zh.git $tmp
Copy-Item "$tmp\skills\*" "<本项目>\.opencode\skills" -Recurse -Force
```

更新后请同步修改上表的 commit 与版本号。

## 备选：官方插件方式（重启自动更新）

在 `opencode.json` 中加入：

```json
{
  "plugin": ["superpowers@git+https://github.com/jnMetaCode/superpowers-zh.git"]
}
```

重启 OpenCode 后插件会自动安装并注册全部 skills（可用 `#vX.Y.Z` 固定版本）。
注意：它与本目录的 ID 完全相同，两者同时存在时以插件注册的来源为准，**建议二选一**——
选了插件就把本目录删掉，避免出现「改这里不生效」的困惑。

## 备注

- 上游仓库的 git tag 已经到 `v5.1.0`，而 `package.json` 的 `version` 仍是 `1.7.13`，两者不是一套编号；
  若要固定版本，建议用 commit hash（如上表）而不是 tag。
- 全局 `~/.claude/skills`、`~/.agents/skills` 里另有 2 个不属于本仓库的技能：
  `developing-with-streamlit`、`find-skills`。它们不受本次导入影响。
