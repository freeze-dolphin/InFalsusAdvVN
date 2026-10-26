# InFalsusAdvVN

**In Falsus** 剧情模式（Visual Novel）的两组增强，各自独立、各自可开关：

1. **LanguageSwitch** — 用快捷键实时切换**剧情文本**的语言（当前句、后续句、人名、历史记录）。
2. **Scroll** — 用鼠标滚轮做剧情导航（滚轮下 = 翻到下一句，滚轮上 = 打开历史记录）。

Two independent enhancements for the **In Falsus** visual novel mode:

1. **LanguageSwitch** — switch the *story* language in real time with a hotkey (current line,
   following lines, speaker name and history).
2. **Scroll** — drive the novel with the mouse wheel (wheel down = next line, wheel up = open history).

> 两者都只作用于剧情模式。菜单、设置、卡牌等 UI 完全不受影响。

---

## 两个功能对照 / At a glance

| | **LanguageSwitch** | **Scroll** |
|---|---|---|
| 触发 | 键盘快捷键（默认 `L`） | 鼠标滚轮（上 / 下） |
| 作用 | 切换剧情文本语言 | 推进剧情 / 打开历史记录 |
| 配置节 | `[LanguageSwitch]` | `[Scroll]` |
| 可关闭 | 通过 `LanguageOrder` 只留一种语言，或删掉该节配置 | `WheelDownAdvances` / `WheelUpOpensLog` 置 `false` |
| 生效范围 | 仅剧情（正文 / 人名 / LOG 历史） | 仅剧情（不影响列表界面自身的滚动） |

---

# 1. LanguageSwitch — 剧情文本实时切换

按一下快捷键，**当前正在读的这一句立刻变成目标语言**，之后每一句也都保持该语言；说话人名字牌与
已打开的历史记录也会同步切换。切换只作用于剧情文本，游戏的 UI 语言保持不变。

## 用法 / Usage

* 剧情中按 **`L`**：按配置的顺序循环切换语言（默认 `English → 简体中文 → 日本語 → 繁體中文`）。
* 只在存在 `StoryScene` 时响应；不在剧情里按 `L` 不会做任何事。
* 首次切换会从**游戏当前语言**开始往后循环，不会跳变。
* 打开 LOG 面板时会直接用当前选择语言构建；面板已经打开时按快捷键，历史条目会**就地重译**。

## 覆盖范围 / What it covers

| 目标 | 是否随快捷键切换 | 说明 |
|---|---|---|
| 对话正文 | ✅ 实时 | 当前句立即重排，之后每句由游戏本体重新解析 |
| 说话人名字牌 | ✅ 实时 | 走 `DynamicStringMapping` 的 `StoryName` 映射 |
| 历史记录（LOG） | ✅ 实时 | 打开时按目标语言构建；已打开时就地重译 |
| 菜单 / 设置 / 卡牌 等 UI | ❌ 不变（设计目标） | 这些走 `Str.Strings.Get(Key)`，模组完全不碰 |

## 游戏支持的语言 / Languages shipped by the game

游戏本体只带这四种剧情文本翻译（来自 `StoryTranslationDetails.Translations` 的 `StringMapping`
结构，字段为 `Id / English / Japanese / Korean / ChineseTC / ChineseSC`）：

| 名称 | 配置写法 | `Str.Strings.Localization` |
|---|---|---|
| English | `English` / `en` | 1 |
| 日本語 | `Japanese` / `jp` | 2 |
| 简体中文 | `ChineseSC` / `zh-hans` | 5 |
| 繁體中文 | `ChineseTC` / `zh-hant` | 4 |
| 한국어（数据回落英文） | `Korean` / `ko` | 3 |

`Korean` 在枚举与结构体里存在，但 `StoryTranslationDetails.Get()` 对韩语回落到英文字段，
所以默认配置没有放它。

## 配置 / Configuration

```ini
[LanguageSwitch]

## Key that cycles the story text language. Only acted on while a story (visual novel)
## scene is loaded. Unity Input System key name, e.g. L, D, F6, Tab.
# Default value: L
Hotkey = L

## Languages cycled by the hotkey.
# Default value: English,ChineseSC,Japanese,ChineseTC
LanguageOrder = English,ChineseSC,Japanese,ChineseTC

## Remember the story language selected with the hotkey and re-apply it on the next launch.
# Default value: true
RememberSelection = true

## Also hide the renderer sets of the other languages after switching. The set for the
## selected language is always activated; enable this only if leftover text remains visible.
# Default value: false
ManageRendererVisibility = false
```

常用改法：

```ini
# 只在简中和英文之间切换
LanguageOrder = English,ChineseSC

# 改成 F6，顺序 日 → 简 → 繁 → 英
Hotkey = F6
LanguageOrder = Japanese,ChineseSC,ChineseTC,English
```

## 实现要点 / How it works

逆向结论（详见 `../infalsus_rea/ANALYSIS.md`）：

1. 每句台词由 `StoryCommandProcessor.ProcessSayTextCommand` 解析：
   `Strings.GetDynamic(DynamicStringTypeFlags.StoryText, StoryStringId)`，
   而 `GetDynamic` 内部读静态字段 `Str.Strings.targetLocalization`。
   **语言在这一刻被固定并存入命令**，之后可见文本都由这份数据切出。
2. 显示时 `StoryScene._jp(IStoryEvent)` 把事件自带的 `Str.DynamicString` 交给
   `FastText.SetText(...)`，`FastText` 再从 `Strings.GetCurrentLocalization()` 取字体族与换行规则。
3. 因此"事后改写文本框"会被下一帧的子串覆盖 —— 必须让**游戏自己**用目标语言解析与渲染。

模组在两个调用点各加一个 Harmony 钩子，仅在调用期间把 `targetLocalization` 切到目标语言，
返回前恢复：

| 钩子 | 作用 |
|---|---|
| `StoryCommandProcessor.ProcessSayTextCommand` | 这句在**解析时**就是目标语言 |
| `StoryScene._jp` | **渲染时**字体/换行规则一致，并把 `_xS` 切到对应语言的 FastText 组 |

人名与历史记录在此基础上单独处理：

* 人名：`DynamicStringMapping.Get(StoryName, loc, RawStoryName(原始键))` → `StoryNameContainer._yO`；
  会记住首次解析成功的原始 `RawStoryName` 键，并拒绝写入游戏的 `Missing String Mapping` 兜底串。
* 历史记录：LOG 面板是 `ChatLogContainer`，打开时由 `StoryScene._Ep()` 从
  `StoryScene._GS` 取命令列表调 `ChatLogContainer._aS` 重新解析（该调用已被钩子覆盖）；
  已打开时用"条目当前文本反查翻译表得到 id → 重新解析 → 就地写回"的方式刷新。

---

# 2. Scroll — 鼠标滚轮导航

用滚轮控制剧情：**向下 = 相当于鼠标左键单击（翻到下一句）**，**向上 = 相当于点击 LOG 按钮
（打开历史记录）**。

## 用法 / Usage

* 剧情中滚轮向下：推进一句（等同于左键点击）。
* 剧情中滚轮向上：打开历史记录；历史记录已打开时滚轮交还给面板（不会在背后推进剧情）。
* 其他界面不受影响，仍使用游戏原本的滚轮行为（列表/选歌等）。

## 配置 / Configuration

```ini
[Scroll]

## Mouse wheel down advances the story, like a left click.
# Default value: true
WheelDownAdvances = true

## Mouse wheel up opens the story history (LOG) panel, like clicking the LOG button.
# Default value: true
WheelUpOpensLog = true
```

两项都可以单独关闭。

## 实现要点 / How it works

* 推进走 `StoryScene._dp()`：这是游戏键盘/点击路径共用的输入触发（内部置 `StoryState` 的触发标志），
  而不是直接模拟一次点击事件。
* 打开历史记录走 `StoryScene._Ep()`：LOG 按钮处理器 `_wp` 就是调它，它再从 `StoryScene._GS`
  取命令列表调 `ChatLogContainer._aS` 填充（因此打开时也是当前选择语言）。
* 滚轮取值：这套构建由游戏自己驱动 Input System，`Mouse.current.scroll` 等设备读取在
  MonoBehaviour 阶段恒为 0，所以改为在 **`InputSystem.onEvent`** 上解析原始事件里的
  `MouseState.scroll`（事件头 24 字节，状态区 `position@+0 / delta@+8 / scroll@+16`）。
  监听器在**进入剧情后**才挂载，避免被游戏启动时设置的事件监听器覆盖。

---

## 安装 / Installation

**Thunderstore / r2modman**：直接安装，依赖 `BepInExPack_IL2CPP`。

**手动**：把 `BepInEx/plugins/InFalsusAdvVN/InFalsusAdvVN.dll` 放到
`<游戏目录>/BepInEx/plugins/InFalsusAdvVN/`。

需要 **BepInEx 6.0.0-be.788 (IL2CPP, x64)** 或兼容版本。
开发/验证环境：Unity 6000.3.9f1、.NET 6.0.7、Il2CppInterop 1.5.3、Harmony 2.10.2。

## 完整配置参考 / Full config reference

首次运行生成 `BepInEx/config/aria.infalsus.advvn.cfg`：

```ini
[LanguageSwitch]
Hotkey = L
LanguageOrder = English,ChineseSC,Japanese,ChineseTC
RememberSelection = true
ManageRendererVisibility = false

[Scroll]
WheelDownAdvances = true
WheelUpOpensLog = true

[Advanced]

## Also replace the text stored in StoryState.TextBoxState.CurrentText.
# Default value: false
RewriteEngineText = false

[Debug]

## Log every resolved story string id and every applied text update.
# Default value: false
VerboseLogging = false
```

> 旧版本的配置在 `[General]` 节下，本版本已改名为 `[LanguageSwitch]`。
> 升级后如需保留原有设置，把旧 `[General]` 里的键值手工搬到 `[LanguageSwitch]` 即可。

## 打包 / Packaging

`build/package.ps1` 生成 Thunderstore 包，**只包含发布需要的文件**（源码、`.csproj`、`bin/obj`、
`.git` 一律不进包）：

```
dist/InFalsusAdvVN-<version>.zip
├─ manifest.json
├─ icon.png
├─ README.md
├─ CHANGELOG.md
├─ LICENSE
└─ BepInEx/plugins/InFalsusAdvVN/InFalsusAdvVN.dll
```

```bat
:: 推荐：包装器。它以「进程级」-ExecutionPolicy Bypass 调用脚本，
:: 因此 AllSigned / Restricted 策略的机器也能直接运行，且不修改系统策略。
build\package.cmd                        :: 本机打包（检测到游戏 interop 时会先 dotnet build -c Release）
build\package.cmd -Version 1.1.0         :: 指定版本
build\package.cmd -SkipBuild -ExpectedTag v1.0.0
build\package.cmd -DllPath bin\Release\InFalsusAdvVN.dll
```

如果直接调用 `.ps1`，请按进程指定策略（同样不改全局设置）：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build\package.ps1 -SkipBuild
```

脚本会校验 `manifest.json` 的必填字段、`icon.png` 必须是 256×256，并在打包后重新打开 zip
确认必需文件齐全、且不含 `.cs` / `.csproj` / `.git` / `bin` / `obj` 等发布不需要的内容。

> **CI 无法编译**：构建依赖 Il2CppInterop 从游戏本体生成的 interop 程序集（`Game.dll`、`Str.dll` …），
> 它们不属于本仓库。因此仓库内提交了 `prebuilt/InFalsusAdvVN.dll` 供 CI 打包，
> 详见 [prebuilt/README.md](prebuilt/README.md)。改动源码 / 版本后请同步刷新该文件。

## 自动发布 / Automatic releases

`.github/workflows/release.yml`：推送 `v*` tag 时自动调用打包脚本并创建 GitHub Release
（附带 zip 与 `SHA256SUMS.txt`）；也可在 Actions 页面手动 dispatch 并填写 tag。
tag 与 `manifest.json` 的 `version_number` 不一致时工作流会失败，避免发出错版本的包。

```powershell
# 发版：改 manifest.json 的 version_number（并刷新 prebuilt/），然后
git tag v1.0.0
git push origin v1.0.0
```

## 已知限制 / Known limitations

* 剧情文本之外的 UI 文本不随快捷键变化（这是设计目标）。
* 若在"逐字浮现"动画进行中切换，该句会继续用切换前解析的文本跑完；等它显示完再按一次即可
  （Instant 文本速度没有这个问题）。
* LOG 的历史条目没有保存 `StoryStringId`，模组是用"当前文本反查翻译表"得到 id 的；
  若某条文本在翻译表中不唯一，可能取到同文的第一条（显示文本一致，不影响观感）。
* 滚轮监听在进入剧情后才挂载；刚进游戏的主菜单不受影响。
* 游戏更新若改了 `ProcessSayTextCommand` / `_jp` / `_Ep` / `_dp` 等名字或签名，需要同步更新模组；
  日志里会打印各个钩子的安装结果。

## 从源码构建 / Build from source

```powershell
dotnet build -c Release
```

默认从 `%APPDATA%\r2modmanPlus-local\InFalsus\profiles\Default\BepInEx` 读取
BepInEx / interop 程序集，可用 `-p:BepInExDir=<路径>` 覆盖；构建后会把 dll 放进本包的
`BepInEx/plugins/InFalsusAdvVN/`。

## 许可证 / License

GNU General Public License v3.0 — 见 [LICENSE](LICENSE)。

## 致谢 / Credits

* BepInEx 团队（IL2CPP 运行时与 Il2CppInterop）。
* 逆向分析使用 REA（IL2CPP 元数据 + 原生代码）。
