# InFalsusAdvVN

In Falsus 剧情模式（Visual Novel）的两组增强：**LanguageSwitch**（快捷键实时切换剧情文本语言）与
**Scroll**（鼠标滚轮做剧情导航）。两者互相独立，可分别开关。

Two independent enhancements for the In Falsus visual novel mode: **LanguageSwitch** (hotkey-driven
real-time story language switching) and **Scroll** (mouse wheel navigation).

[中文](#中文) · [English](#english)

---

# 中文

## 功能一览

| | LanguageSwitch | Scroll |
|---|---|---|
| 触发 | 键盘快捷键（默认 `L`） | 鼠标滚轮（上 / 下） |
| 作用 | 切换剧情文本语言 | 推进剧情 / 打开历史记录 |
| 配置节 | `[LanguageSwitch]` | `[Scroll]` |
| 可关闭 | `LanguageOrder` 只留一种语言 | `WheelDownAdvances` / `WheelUpOpensLog` 置 `false` |
| 生效范围 | 仅剧情（正文 / 人名 / LOG 历史） | 仅剧情（不影响列表界面的滚动） |

两者都只作用于剧情模式：菜单、设置、卡牌等 UI 完全不受影响。

## LanguageSwitch：剧情文本实时切换

按一下快捷键，**当前正在读的这一句立刻变成目标语言**，之后每一句都保持该语言；说话人名字牌与
已打开的历史记录也同步切换。只改剧情文本，不改游戏的 UI 语言，也不写存档里的语言项。

### 用法

* 剧情中按 **`L`**：在配置的语言列表里循环（默认 `English → 简体中文 → 日本語 → 繁體中文`）。
* 只在存在 `StoryScene` 时响应，其他界面按 `L` 不做任何事。
* 首次切换从**游戏当前语言**开始往后循环，不会跳变。
* 打开 LOG 面板时直接用当前选择语言构建；面板已打开时按快捷键，历史条目会**就地重译**。

### 覆盖范围

| 目标 | 是否切换 | 说明 |
|---|---|---|
| 对话正文 | 实时 | 当前句立即重排，之后每句由游戏本体重新解析 |
| 说话人名字牌 | 实时 | 走 `DynamicStringMapping` 的 `StoryName` 映射 |
| 历史记录（LOG） | 实时 | 打开时按目标语言构建；已打开时就地重译 |
| 菜单 / 设置 / 卡牌等 UI | 不变 | 这些走 `Str.Strings.Get(Key)`，模组完全不碰 |

### 支持的语言

游戏本体只带这四种剧情文本翻译（`StoryTranslationDetails.Translations` 的 `StringMapping`，
字段为 `Id / English / Japanese / Korean / ChineseTC / ChineseSC`）：

| 名称 | 配置写法 | `Str.Strings.Localization` |
|---|---|---|
| English | `English` / `en` | 1 |
| 日本語 | `Japanese` / `jp` | 2 |
| 简体中文 | `ChineseSC` / `zh-hans` | 5 |
| 繁體中文 | `ChineseTC` / `zh-hant` | 4 |
| 한국어（数据回落英文） | `Korean` / `ko` | 3 |

`Korean` 在枚举与结构体里存在，但 `StoryTranslationDetails.Get()` 对韩语回落到英文字段，
因此默认配置没有包含它。

### 配置

```ini
[LanguageSwitch]
Hotkey = L
LanguageOrder = English,ChineseSC,Japanese,ChineseTC
RememberSelection = true
ManageRendererVisibility = false
```

* `Hotkey`：`UnityEngine.InputSystem.Key` 名称（`L`、`D`、`F6`、`Tab` …）。
* `LanguageOrder`：循环顺序，支持 `English / Japanese / ChineseSC / ChineseTC / Korean`
  以及别名 `en / jp / zh-hans / zh-hant / ko`。
* `RememberSelection`：记住上次选择，下次启动自动套用。
* `ManageRendererVisibility`：切换后额外隐藏其他语言的渲染组（一般不需要开）。

常用改法：

```ini
# 只在简中和英文之间切换
LanguageOrder = English,ChineseSC

# 改成 F6，顺序 日 → 简 → 繁 → 英
Hotkey = F6
LanguageOrder = Japanese,ChineseSC,ChineseTC,English
```

### 实现要点

逆向结论（详见 `../infalsus_rea/ANALYSIS.md`）：

1. 每句台词由 `StoryCommandProcessor.ProcessSayTextCommand` 经
   `Strings.GetDynamic(DynamicStringTypeFlags.StoryText, StoryStringId)` 解析，而 `GetDynamic`
   内部读静态字段 `Str.Strings.targetLocalization` —— **语言在解析这一刻被固定**并存入命令。
2. 显示时 `StoryScene._jp(IStoryEvent)` 把事件自带的 `Str.DynamicString` 交给
   `FastText.SetText(...)`，`FastText` 再从 `Strings.GetCurrentLocalization()` 取字体族与换行规则。
3. 因此事后改写文本框会被下一帧的子串覆盖，必须让**游戏自己**用目标语言解析与渲染。

模组只在两个调用点各加一个 Harmony 钩子，调用期间把 `targetLocalization` 切到目标语言，
返回前恢复：

| 钩子 | 作用 |
|---|---|
| `StoryCommandProcessor.ProcessSayTextCommand` | 这句在**解析时**就是目标语言 |
| `StoryScene._jp` | **渲染时**字体/换行一致，并把 `_xS` 切到对应语言的 FastText 组 |

人名与历史记录在此基础上单独处理：

* 人名：`DynamicStringMapping.Get(StoryName, loc, RawStoryName(原始键))` 写入 `StoryNameContainer._yO`；
  记忆首次成功的原始 `RawStoryName` 键，并拒绝写入游戏的 `Missing String Mapping` 兜底串。
* 历史记录：LOG 面板是 `ChatLogContainer`，打开时由 `StoryScene._Ep()` 从 `StoryScene._GS`
  取命令列表调 `ChatLogContainer._aS` 重新解析（该调用已被钩子覆盖）；已打开时用
  「条目当前文本反查翻译表得到 id → 重新解析 → 就地写回」刷新。

## Scroll：鼠标滚轮导航

滚轮向下 = 鼠标左键单击（翻到下一句）；滚轮向上 = 点击 LOG 按钮（打开历史记录）。

### 用法

* 剧情中滚轮向下：推进一句（等同左键点击）。
* 剧情中滚轮向上：打开历史记录；历史记录已打开时滚轮交还给面板，不会在背后推进剧情。
* 其他界面不受影响，仍是游戏原本的滚轮行为（列表 / 选歌等）。

### 配置

```ini
[Scroll]
WheelDownAdvances = true
WheelUpOpensLog = true
```

两项可单独关闭。

### 实现要点

* 推进走 `StoryScene._dp()`：游戏键盘/点击路径共用的输入触发（内部置 `StoryState` 的触发标志），
  不是模拟一次点击事件。
* 打开历史记录走 `StoryScene._Ep()`：LOG 按钮处理器 `_wp` 就是调它，它再从 `StoryScene._GS`
  取命令列表调 `ChatLogContainer._aS` 填充。
* 滚轮取值：这套构建由游戏自己驱动 Input System，`Mouse.current.scroll` 等设备读取在
  MonoBehaviour 阶段恒为 0，因此在 **`InputSystem.onEvent`** 上解析原始事件里的 `MouseState.scroll`
  （事件头 24 字节，状态区 `position@+0 / delta@+8 / scroll@+16`）。监听器在**进入剧情后**才挂载，
  避免被游戏启动时设置的事件监听器覆盖。

## 安装

**Thunderstore / r2modman**：直接安装，依赖 `BepInExPack_IL2CPP`。

**手动**：把 `BepInEx/plugins/InFalsusAdvVN/InFalsusAdvVN.dll` 放到
`<游戏目录>/BepInEx/plugins/InFalsusAdvVN/`。

需要 **BepInEx 6.0.0-be.788 (IL2CPP, x64)** 或兼容版本。开发/验证环境：
Unity 6000.3.9f1、.NET 6.0.7、Il2CppInterop 1.5.3、Harmony 2.10.2。

## 完整配置参考

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
RewriteEngineText = false

[Debug]
VerboseLogging = false
```

* `Advanced/RewriteEngineText`：额外替换 `StoryState.TextBoxState.CurrentText` 里的文本（默认关闭）。
* `Debug/VerboseLogging`：输出每条解析到的剧情 id 与每次文本更新（默认关闭）。

> 旧版本配置在 `[General]` 节下，现已改名 `[LanguageSwitch]`；如需保留旧设置，把键值手工搬过去。

## 打包

`build/package.ps1` 生成 Thunderstore 包，**只包含发布需要的文件**（源码、`.csproj`、`bin/obj`、
`.git` 一律不进包）：

```
dist/InFalsusAdvVN-<version>.zip
├─ manifest.json
├─ icon.png
├─ README.md
├─ LICENSE
└─ BepInEx/plugins/InFalsusAdvVN/InFalsusAdvVN.dll
```

```bat
:: 推荐：包装器，以「进程级」-ExecutionPolicy Bypass 调用脚本，
:: 因此 AllSigned / Restricted 策略的机器也能直接运行，且不修改系统策略。
build\package.cmd                        :: 本机打包（检测到游戏 interop 时会先 dotnet build -c Release）
build\package.cmd -Version 1.0.1         :: 指定版本
build\package.cmd -SkipBuild -ExpectedTag v1.0.1
build\package.cmd -DllPath bin\Release\InFalsusAdvVN.dll
```

直接调用 `.ps1` 时请按进程指定策略（同样不改全局设置）：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build\package.ps1 -SkipBuild
```

脚本会校验 `manifest.json` 必填字段、`icon.png` 必须是 256×256，并在打包后重新打开 zip
确认必需文件齐全、且不含 `.cs` / `.csproj` / `.git` / `bin` / `obj` 等发布不需要的内容。

> **CI 无法编译**：构建依赖 Il2CppInterop 从游戏本体生成的 interop 程序集（`Game.dll`、`Str.dll` …），
> 它们不属于本仓库。因此仓库内提交了 `prebuilt/InFalsusAdvVN.dll` 供 CI 打包，
> 详见 [prebuilt/README.md](prebuilt/README.md)。改动源码或版本后请同步刷新该文件。

## 自动发布

`.github/workflows/release.yml`：推送 `v*` tag 时自动调用打包脚本并创建 GitHub Release
（附带 zip 与 `SHA256SUMS.txt`）；也可在 Actions 页面手动 dispatch 并填写 tag。
tag 与 `manifest.json` 的 `version_number` 不一致时工作流失败，避免发出错版本的包。

```powershell
# 发版：改 manifest.json / csproj / Plugin.cs 的版本，并刷新 prebuilt/，然后
git tag v1.0.1
git push origin v1.0.1
```

## 已知限制

* 剧情文本之外的 UI 文本不随快捷键变化（设计目标）。
* 若在「逐字浮现」动画进行中切换，该句会继续用切换前解析的文本跑完；等它显示完再按一次即可
  （Instant 文本速度没有这个问题）。
* LOG 条目没有保存 `StoryStringId`，模组用「当前文本反查翻译表」得到 id；若某条文本在翻译表中
  不唯一，可能取到同文的第一条（显示文本一致，不影响观感）。
* 滚轮监听在进入剧情后才挂载，刚进游戏的主菜单不受影响。
* 游戏更新若改了 `ProcessSayTextCommand` / `_jp` / `_Ep` / `_dp` 等名字或签名，需要同步更新模组；
  日志会打印各个钩子的安装结果。

## 从源码构建

```powershell
dotnet build -c Release
```

默认从 `%APPDATA%\r2modmanPlus-local\InFalsus\profiles\Default\BepInEx` 读取 BepInEx / interop
程序集，可用 `-p:BepInExDir=<路径>` 覆盖；构建后会把 dll 放进本包的
`BepInEx/plugins/InFalsusAdvVN/`。

## 许可证与致谢

GNU General Public License v3.0，见 [LICENSE](LICENSE)。

* BepInEx 团队（IL2CPP 运行时与 Il2CppInterop）。
* 逆向分析使用 REA（IL2CPP 元数据 + 原生代码）。

---

# English

## Features at a glance

| | LanguageSwitch | Scroll |
|---|---|---|
| Trigger | Keyboard hotkey (default `L`) | Mouse wheel (up / down) |
| Effect | Switches the story language | Advances the story / opens history |
| Config section | `[LanguageSwitch]` | `[Scroll]` |
| Can be disabled | Leave a single language in `LanguageOrder` | Set `WheelDownAdvances` / `WheelUpOpensLog` to `false` |
| Scope | Story only (text / speaker name / LOG history) | Story only (list scrolling is untouched) |

Both features act on the visual novel mode only: menus, settings and card UI are never affected.

## LanguageSwitch: real-time story text switching

Pressing the hotkey turns **the line you are reading** into the target language immediately, and every
following line keeps that language. The speaker name plate and an already-open history panel switch with
it. Only the story text changes - the game's UI language and the saved language setting are untouched.

### Usage

* In the story, press **`L`** to cycle through the configured list (default
  `English → Simplified Chinese → Japanese → Traditional Chinese`).
* It only reacts while a `StoryScene` exists; pressing `L` anywhere else does nothing.
* The first press continues from the language the game is currently using, it does not jump.
* Opening the LOG panel builds it in the selected language; pressing the hotkey while it is open
  re-translates the existing entries in place.

### Coverage

| Target | Switched | Notes |
|---|---|---|
| Dialogue text | Live | Current line is re-laid out; later lines are re-resolved by the game |
| Speaker name plate | Live | Uses the `StoryName` mapping of `DynamicStringMapping` |
| History (LOG) | Live | Built in the target language, or re-translated in place |
| Menu / settings / card UI | No | These use `Str.Strings.Get(Key)`; the mod never touches them |

### Languages shipped by the game

The game only ships these four story translations (`StringMapping` inside
`StoryTranslationDetails.Translations`, fields `Id / English / Japanese / Korean / ChineseTC / ChineseSC`):

| Name | Config value | `Str.Strings.Localization` |
|---|---|---|
| English | `English` / `en` | 1 |
| Japanese | `Japanese` / `jp` | 2 |
| Simplified Chinese | `ChineseSC` / `zh-hans` | 5 |
| Traditional Chinese | `ChineseTC` / `zh-hant` | 4 |
| Korean (data falls back to English) | `Korean` / `ko` | 3 |

`Korean` exists in the enum and struct, but `StoryTranslationDetails.Get()` falls back to the English
field for it, so it is not part of the default configuration.

### Configuration

```ini
[LanguageSwitch]
Hotkey = L
LanguageOrder = English,ChineseSC,Japanese,ChineseTC
RememberSelection = true
ManageRendererVisibility = false
```

* `Hotkey`: a `UnityEngine.InputSystem.Key` name (`L`, `D`, `F6`, `Tab`, ...).
* `LanguageOrder`: cycle order; accepts `English / Japanese / ChineseSC / ChineseTC / Korean` and the
  aliases `en / jp / zh-hans / zh-hant / ko`.
* `RememberSelection`: remember the last choice and re-apply it on the next launch.
* `ManageRendererVisibility`: additionally hide the renderer sets of the other languages after
  switching (normally unnecessary).

Examples:

```ini
# Only English and Simplified Chinese
LanguageOrder = English,ChineseSC

# Hotkey F6, order Japanese -> SC -> TC -> English
Hotkey = F6
LanguageOrder = Japanese,ChineseSC,ChineseTC,English
```

### How it works

Findings from reverse engineering (details in `../infalsus_rea/ANALYSIS.md`):

1. Every line is resolved by `StoryCommandProcessor.ProcessSayTextCommand` through
   `Strings.GetDynamic(DynamicStringTypeFlags.StoryText, StoryStringId)`, and `GetDynamic` reads the
   static field `Str.Strings.targetLocalization` - **the language is fixed at that moment** and stored in
   the command.
2. When rendering, `StoryScene._jp(IStoryEvent)` hands the event's `Str.DynamicString` to
   `FastText.SetText(...)`, and `FastText` takes the font family and line-breaking rules from
   `Strings.GetCurrentLocalization()`.
3. Rewriting the text box afterwards is therefore overwritten by the next frame's substring - the game
   itself has to resolve and render in the target language.

The mod adds one Harmony hook at each of those two call sites and only switches `targetLocalization`
for the duration of the call, restoring it before returning:

| Hook | Purpose |
|---|---|
| `StoryCommandProcessor.ProcessSayTextCommand` | The line is resolved in the target language |
| `StoryScene._jp` | Rendering uses the matching font/line rules and `_xS` selects the right FastText set |

Speaker name and history are handled on top of that:

* Speaker name: `DynamicStringMapping.Get(StoryName, loc, RawStoryName(original key))` into
  `StoryNameContainer._yO`; the original `RawStoryName` key is memoised and the game's
  `Missing String Mapping` fallback string is never written back.
* History: the LOG panel is `ChatLogContainer`. It is filled by `StoryScene._Ep()`, which takes the
  command list from `StoryScene._GS` and calls `ChatLogContainer._aS` (that call is covered by the
  hook). For an already-open panel each entry's current text is reverse-mapped to its id in the
  translation table and rewritten in place.

## Scroll: mouse wheel navigation

Wheel down acts like a left click (next line); wheel up acts like the LOG button (open history).

### Usage

* Wheel down in the story: advance one line (identical to a left click).
* Wheel up in the story: open the history; while it is open the wheel belongs to the panel and never
  advances the story behind it.
* Other screens are unaffected and keep the game's own wheel behaviour (lists, song select, ...).

### Configuration

```ini
[Scroll]
WheelDownAdvances = true
WheelUpOpensLog = true
```

Both can be disabled independently.

### How it works

* Advancing uses `StoryScene._dp()`, the input trigger shared by the game's keyboard and click paths
  (it raises the trigger flag in `StoryState`); no synthetic click event is injected.
* Opening the history uses `StoryScene._Ep()`, which is exactly what the LOG button handler `_wp`
  calls; it takes the command list from `StoryScene._GS` and fills `ChatLogContainer._aS`.
* Reading the wheel: this build drives the Input System itself, so device reads such as
  `Mouse.current.scroll` are always 0 by the time a MonoBehaviour callback runs. The wheel is therefore
  parsed from the raw event stream in **`InputSystem.onEvent`** (`MouseState.scroll`; event header is
  24 bytes, state layout `position@+0 / delta@+8 / scroll@+16`). The listener is attached **after the
  story scene exists**, so the game's own listener installed during startup is not overwritten.

## Installation

**Thunderstore / r2modman**: install normally; it depends on `BepInExPack_IL2CPP`.

**Manual**: copy `BepInEx/plugins/InFalsusAdvVN/InFalsusAdvVN.dll` into
`<game folder>/BepInEx/plugins/InFalsusAdvVN/`.

Requires **BepInEx 6.0.0-be.788 (IL2CPP, x64)** or a compatible build. Developed and verified with
Unity 6000.3.9f1, .NET 6.0.7, Il2CppInterop 1.5.3 and Harmony 2.10.2.

## Full configuration reference

Generated on first launch as `BepInEx/config/aria.infalsus.advvn.cfg`:

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
RewriteEngineText = false

[Debug]
VerboseLogging = false
```

* `Advanced/RewriteEngineText`: also replace the text stored in `StoryState.TextBoxState.CurrentText`
  (off by default).
* `Debug/VerboseLogging`: log every resolved story id and every applied text update (off by default).

> Older versions used the `[General]` section; it is now `[LanguageSwitch]`. Copy the old keys over if
> you want to keep your settings.

## Packaging

`build/package.ps1` produces the Thunderstore package and **only includes what a release needs**
(sources, `.csproj`, `bin/obj` and `.git` never end up in the zip):

```
dist/InFalsusAdvVN-<version>.zip
├─ manifest.json
├─ icon.png
├─ README.md
├─ LICENSE
└─ BepInEx/plugins/InFalsusAdvVN/InFalsusAdvVN.dll
```

```bat
:: Recommended wrapper: it calls the script with a process-level -ExecutionPolicy Bypass,
:: so machines using AllSigned/Restricted can run it without changing any global setting.
build\package.cmd                        :: local build (dotnet build -c Release when interop is present)
build\package.cmd -Version 1.0.1         :: override the version
build\package.cmd -SkipBuild -ExpectedTag v1.0.1
build\package.cmd -DllPath bin\Release\InFalsusAdvVN.dll
```

Calling the `.ps1` directly also works when the policy is set per process (again, nothing global):

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build\package.ps1 -SkipBuild
```

The script validates the required `manifest.json` fields, requires a 256x256 `icon.png`, and re-opens
the finished zip to verify that every required file is present and that no `.cs`, `.csproj`, `.git`,
`bin` or `obj` content leaked in.

> **CI cannot compile**: building needs the game's Il2CppInterop assemblies (`Game.dll`, `Str.dll`, ...),
> which are not part of this repository. The built assembly is therefore committed as
> `prebuilt/InFalsusAdvVN.dll` for the release workflow - see [prebuilt/README.md](prebuilt/README.md).
> Refresh it in the same commit that changes the sources or the version.

## Automatic releases

`.github/workflows/release.yml` runs on every `v*` tag (and via manual dispatch with a tag input),
packages the mod and publishes a GitHub release with the zip and `SHA256SUMS.txt`. The workflow fails
when the tag does not match `version_number` in `manifest.json`, so a wrong version can never be
released.

```powershell
# Release: bump manifest.json / csproj / Plugin.cs, refresh prebuilt/, then
git tag v1.0.1
git push origin v1.0.1
```

## Known limitations

* UI text outside the story does not follow the hotkey (that is the design goal).
* Switching during the typewriter animation lets that line finish with the previously resolved text;
  press once more after it completes (Instant text speed is unaffected).
* LOG entries do not store their `StoryStringId`; the mod recovers it by reverse-mapping the current
  text. If a string is not unique in the translation table the first match is used (the visible text is
  identical, so this is cosmetic).
* The wheel listener is attached once you are in the story; the main menu is unaffected.
* If a game update renames or changes the signatures of `ProcessSayTextCommand`, `_jp`, `_Ep` or `_dp`,
  the mod has to be updated; the log reports the result of every hook installation.

## Build from source

```powershell
dotnet build -c Release
```

BepInEx and the interop assemblies are read from
`%APPDATA%\r2modmanPlus-local\InFalsus\profiles\Default\BepInEx` by default; override it with
`-p:BepInExDir=<path>`. The build copies the dll into `BepInEx/plugins/InFalsusAdvVN/` of this package.

## License and credits

GNU General Public License v3.0 - see [LICENSE](LICENSE).

* The BepInEx team (IL2CPP runtime and Il2CppInterop).
* Reverse engineering was done with REA (IL2CPP metadata plus native code).
