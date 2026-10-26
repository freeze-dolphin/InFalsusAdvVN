# InFalsusAdvVN

In Falsus 剧情模式增强模组

BepinEx mod that enhances Visual Novel reading experience for InFalsus.

[中文](#中文) · [English](#english)

---

## 中文

此模组仅对剧情模式生效

### 功能

#### 实时切换剧情语言

剧情模式中按下快捷键（默认为`L`），实时修改文本语言（仅影响文本、人名、手机中的信息）

- 只改剧情文本，**不会**修改游戏设置里的语言和存档
- 语言切换快捷键只在剧情场景里响应

| 目标 | 说明 |
|---|---|
| 对话正文 | 实时切换 |
| 说话人名字牌 | 实时切换 |
| 历史记录（LOG） | 打开时按当前语言构建；已打开时就地重译 |
| 菜单 / 设置 / 卡牌等 UI | 不受影响 |

游戏自带四种剧情文本：`English` / `Japanese` / `ChineseSC` / `ChineseTC`
（`Korean` 在游戏数据里会回落到英文）。

#### 鼠标滚轮导航

- 鼠标上滚轮：打开历史记录（可通过鼠标右键关闭）
- 鼠标下滚轮：翻到下一句文本

其他界面仍使用游戏原本的滚轮 / 右键行为。

### 安装

**推荐**：在 Thunderstore / r2modman 中搜索 `InFalsusAdvVN` 直接安装，会自动带上依赖
`BepInExPack_IL2CPP`。

**手动**：把压缩包里的 `BepInEx` 文件夹解压到游戏根目录（与 `infalsus.exe` 同级），
确认存在 `BepInEx/plugins/InFalsusAdvVN/InFalsusAdvVN.dll`。

需要 **BepInEx 6.0.0-be.788 (IL2CPP, x64)** 或兼容版本。
开发环境：Unity 6000.3.9f1、.NET 6.0.7、Il2CppInterop 1.5.3、Harmony 2.10.2。

### 配置

首次启动会生成 `BepInEx/config/aria.infalsus.advvn.cfg`，每次修改需要重启游戏才会生效

```ini
[LanguageSwitch]
# 切换语言的快捷键（Unity Input System 的键名，例如 L / F6 / Tab）
Hotkey = L

# 按快捷键循环的语言顺序，英文逗号分隔
# 语言名可以写 `English` / `Japanese` / `ChineseSC` / `ChineseTC` / `Korean`
# 也可以写简称 `en` / `jp` / `zh-hans` / `zh-hant` / `ko`
LanguageOrder = English,ChineseSC,Japanese,ChineseTC

# 记住上次选择的语言，下次启动自动套用
RememberSelection = true

[Scroll]
# 是否启用：滚轮向下 = 下一句
WheelDownAdvances = true

# 是否启用：滚轮向上 = 打开历史记录
WheelUpOpensLog = true

# 是否启用：右键单击 = 关闭历史记录
RightClickClosesLog = true

[Advanced]
# 切换语言时重译已打开的 LOG 面板
RefreshOpenLog = true

# 切换语言时重译已显示的手机聊天气泡
RefreshPhoneChat = true

[Debug]
# 输出每条解析到的剧情 id 与每次文本更新
VerboseLogging = false
```

### 说明

- 如果切换时这一句正在逐字浮现，它可能用切换前的内容播完；等这句话显示完再按一次即可
  （把文本速度设为 Instant 就没有这个现象）。
- LOG 的历史条目没有保存剧情 id，模组用「当前文本反查翻译表」得到 id；若某条文本在翻译表中
  不唯一，会取到同文的第一条（显示文本一致，不影响观感）。
- 滚轮 / 右键监听在进入剧情后才挂载，主菜单不受影响。
- 游戏更新若改了 `ProcessSayTextCommand` / `_jp` / `_Ep` / `_dp` / `_bS` 等名字或签名，
  需要同步更新模组；日志里会打印各个钩子的安装结果。

### 实现要点

1. 每句台词由 `StoryCommandProcessor.ProcessSayTextCommand` 经
   `Strings.GetDynamic(DynamicStringTypeFlags.StoryText, StoryStringId)` 解析，而 `GetDynamic`
   读静态字段 `Str.Strings.targetLocalization` —— **语言在解析这一刻被固定**并存入命令。
2. 显示时 `StoryScene._jp(IStoryEvent)` 把事件自带的 `Str.DynamicString` 交给 `FastText.SetText`，
   `FastText` 再从 `Strings.GetCurrentLocalization()` 取字体与换行规则。
3. 所以模组只在两个调用点做括号（调用期间切 `targetLocalization`，返回前恢复）：

| 钩子 | 作用 |
|---|---|
| `ProcessSayTextCommand` | 这句在**解析时**就是目标语言 |
| `StoryScene._jp` | **渲染时**字体/换行一致，并驱动 `_xS` 选择对应语言的 FastText 组 |

另有三条补充路径：

- 人名：`DynamicStringMapping.Get(StoryName, loc, RawStoryName(原始键))` → `StoryNameContainer._yO`
- LOG：打开时走 `StoryScene._Ep()` → `ChatLogContainer._aS`（已被钩子覆盖）；已打开时按文本反查重译
- 手机聊天：`ProcessPhoneChatCommand` 加同样的括号；已显示的气泡同样按文本反查重译

**不要写 `StoryState.TextBoxState.CurrentText`**：把外来构造的 `DynamicString` 覆盖进去会破坏
游戏自身的文本状态，剧情节点结束时会让游戏空引用崩溃（模组已移除该路径）。

鼠标部分：滚轮下走 `StoryScene._dp()`（游戏键盘/点击共用的输入触发），滚轮上走
`StoryScene._Ep()`，右键走 `ChatLogContainer._bS()`（面板自己的淡出关闭）；滚轮与右键都从
`InputSystem.onEvent` 的原始事件读取（这套构建里设备级读取在 MonoBehaviour 阶段已被重置）。

### 打包

`build/package.ps1` 生成 Thunderstore 包，**只包含发布需要的文件**：

```
dist/InFalsusAdvVN-<version>.zip
├─ manifest.json
├─ icon.png
├─ README.md        （来自 README_thunderstore.md，仓库根目录的 README.md 不打包）
├─ LICENSE
└─ BepInEx/plugins/InFalsusAdvVN/InFalsusAdvVN.dll
```

```bat
:: 包装器以「进程级」-ExecutionPolicy Bypass 调用脚本，不修改系统策略
build\package.cmd                        :: 本机打包（检测到游戏 interop 时会先 dotnet build -c Release）
build\package.cmd -SkipBuild -ExpectedTag v1.0.4
build\package.cmd -DllPath bin\Release\InFalsusAdvVN.dll
```

脚本会校验 `manifest.json` 必填字段、`icon.png` 必须是 256×256，并在打包后重新打开 zip
确认必需文件齐全、且不含 `.cs` / `.csproj` / `.git` / `bin` / `obj` 等发布不需要的内容。

> CI 无法编译：构建依赖 Il2CppInterop 从游戏本体生成的 interop 程序集，它们不属于本仓库。
> 因此仓库内提交了 `prebuilt/InFalsusAdvVN.dll` 供 CI 打包，改动源码或版本后需同步刷新，
> 详见 [prebuilt/README.md](prebuilt/README.md)。

### 自动发布

`.github/workflows/release.yml`：推送 `v*` tag 时自动打包并创建 GitHub Release（附 zip 与
`SHA256SUMS.txt`），也可在 Actions 页面手动 dispatch。tag 与 `manifest.json` 的
`version_number` 不一致时工作流失败，避免发出错版本。

```powershell
# 发版：改 manifest.json / csproj / Plugin.cs 的版本，并刷新 prebuilt/，然后
git tag v1.0.5
git push origin v1.0.5
```

### 从源码构建

```powershell
dotnet build -c Release
```

默认从 `%APPDATA%\r2modmanPlus-local\InFalsus\profiles\Default\BepInEx` 读取 BepInEx / interop
程序集，可用 `-p:BepInExDir=<路径>` 覆盖；构建后会把 dll 放进本包的
`BepInEx/plugins/InFalsusAdvVN/`。

### 许可证

GNU General Public License v3.0，见 [LICENSE](LICENSE)。
逆向分析使用 REA（IL2CPP 元数据 + 原生代码）。

---

## English

This mod only affects the visual novel (story) mode.

### Features

#### Switch the story language in real time

While reading, press the hotkey (default `L`) to change the text language on the fly. Only the
dialogue text, the speaker names and the messages on the phone are affected.

- Only the story text is changed: the language in the game settings and your save file are untouched
- The hotkey only reacts inside the story

| Target | Behaviour |
|---|---|
| Dialogue text | switches immediately |
| Speaker name plate | switches immediately |
| History (LOG) | built in the current language, or re-translated in place while open |
| Menu / settings / card UI | never affected |

The game ships four story languages: `English` / `Japanese` / `ChineseSC` / `ChineseTC`
(`Korean` falls back to English in the game data).

#### Mouse wheel navigation

- Wheel up: open the history (close it with a right click)
- Wheel down: advance to the next line

Other screens keep the game's normal wheel and right-click behaviour.

### Installation

**Recommended**: install `InFalsusAdvVN` from Thunderstore / r2modman; the `BepInExPack_IL2CPP`
dependency comes with it.

**Manual**: extract the `BepInEx` folder from the zip into the game root (next to `infalsus.exe`) and
check that `BepInEx/plugins/InFalsusAdvVN/InFalsusAdvVN.dll` exists.

Requires **BepInEx 6.0.0-be.788 (IL2CPP, x64)** or a compatible build.
Developed with Unity 6000.3.9f1, .NET 6.0.7, Il2CppInterop 1.5.3 and Harmony 2.10.2.

### Configuration

`BepInEx/config/aria.infalsus.advvn.cfg` is created on first launch; restart the game after every
change.

```ini
[LanguageSwitch]
# Hotkey that switches the language (Unity Input System name, e.g. L / F6 / Tab)
Hotkey = L

# Order the hotkey cycles through, separated by commas
# Names: `English` / `Japanese` / `ChineseSC` / `ChineseTC` / `Korean`
# Short forms: `en` / `jp` / `zh-hans` / `zh-hant` / `ko`
LanguageOrder = English,ChineseSC,Japanese,ChineseTC

# Remember the last language and re-apply it on the next launch
RememberSelection = true

[Scroll]
# Enable: wheel down = next line
WheelDownAdvances = true

# Enable: wheel up = open the history
WheelUpOpensLog = true

# Enable: right click = close the history
RightClickClosesLog = true

[Advanced]
# Re-translate an already open LOG panel when the language changes
RefreshOpenLog = true

# Re-translate the phone chat bubbles already on screen
RefreshPhoneChat = true

[Debug]
# Log every resolved story id and every applied text update
VerboseLogging = false
```

### Notes

- If a line is still being revealed when you switch, it may finish with the previous text; press once
  more after it completes (with Instant text speed this never happens).
- LOG entries do not store their story id, so the mod recovers it by reverse-mapping the current text.
  If a string is not unique in the translation table the first match is used (the visible text is
  identical, so this is cosmetic).
- The wheel / right-click listener is attached once you are inside the story; the main menu is
  unaffected.
- If a game update renames or changes the signatures of `ProcessSayTextCommand`, `_jp`, `_Ep`, `_dp`
  or `_bS`, the mod has to be updated; the log reports the result of every hook installation.

### How it works

1. Every line is resolved by `StoryCommandProcessor.ProcessSayTextCommand` through
   `Strings.GetDynamic(DynamicStringTypeFlags.StoryText, StoryStringId)`, and `GetDynamic` reads the
   static field `Str.Strings.targetLocalization` — **the language is fixed at that moment** and stored
   in the command.
2. When rendering, `StoryScene._jp(IStoryEvent)` hands the event's `Str.DynamicString` to
   `FastText.SetText`, and `FastText` takes the font and line-breaking rules from
   `Strings.GetCurrentLocalization()`.
3. The mod therefore only brackets those two calls (switching `targetLocalization` for the duration of
   the call and restoring it afterwards):

| Hook | Purpose |
|---|---|
| `ProcessSayTextCommand` | the line is resolved in the target language |
| `StoryScene._jp` | rendering uses the matching font/line rules and `_xS` selects that language's FastText set |

Three additional paths:

- Speaker name: `DynamicStringMapping.Get(StoryName, loc, RawStoryName(original key))` →
  `StoryNameContainer._yO`
- LOG: opening goes through `StoryScene._Ep()` → `ChatLogContainer._aS` (covered by the hook); an
  already open panel is re-translated by reverse-mapping its texts
- Phone chat: the same bracket on `ProcessPhoneChatCommand`; visible bubbles are reverse-mapped too

**Never write `StoryState.TextBoxState.CurrentText`**: overwriting it with a `DynamicString` built
outside the game's text pipeline corrupts the game's own state and crashed the game when a story node
ended (that path was removed from the mod).

Mouse side: wheel down drives `StoryScene._dp()` (the input trigger shared by the game's keyboard and
click paths), wheel up drives `StoryScene._Ep()`, right click drives `ChatLogContainer._bS()` (the
panel's own fade-out close). Wheel and right click are read from the raw `InputSystem.onEvent` stream,
because device-level reads are already reset by the time a MonoBehaviour callback runs in this build.

### Packaging

`build/package.ps1` produces the Thunderstore package and only includes what a release needs:

```
dist/InFalsusAdvVN-<version>.zip
├─ manifest.json
├─ icon.png
├─ README.md        (from README_thunderstore.md; the repository README.md is not shipped)
├─ LICENSE
└─ BepInEx/plugins/InFalsusAdvVN/InFalsusAdvVN.dll
```

```bat
:: The wrapper applies -ExecutionPolicy Bypass to its own process, nothing global changes
build\package.cmd                        :: local build (runs dotnet build -c Release when interop exists)
build\package.cmd -SkipBuild -ExpectedTag v1.0.4
build\package.cmd -DllPath bin\Release\InFalsusAdvVN.dll
```

The script validates the required `manifest.json` fields, requires a 256x256 `icon.png`, and re-opens
the finished zip to verify that every required file is present and that no source or build output
leaked in.

> CI cannot compile: building needs the Il2CppInterop assemblies generated from the game, which are not
> part of this repository. The built assembly is therefore committed as `prebuilt/InFalsusAdvVN.dll`
> for the release workflow; refresh it whenever the sources or the version change (see
> [prebuilt/README.md](prebuilt/README.md)).

### Automatic releases

`.github/workflows/release.yml` runs on every `v*` tag (or manual dispatch), packages the mod and
publishes a GitHub release with the zip and `SHA256SUMS.txt`. It fails when the tag does not match
`version_number` in `manifest.json`, so a wrong version can never be released.

```powershell
# Release: bump manifest.json / csproj / Plugin.cs, refresh prebuilt/, then
git tag v1.0.5
git push origin v1.0.5
```

### Build from source

```powershell
dotnet build -c Release
```

BepInEx and the interop assemblies are read from
`%APPDATA%\r2modmanPlus-local\InFalsus\profiles\Default\BepInEx` by default; override it with
`-p:BepInExDir=<path>`. The build copies the dll into `BepInEx/plugins/InFalsusAdvVN/`.

### License

GNU General Public License v3.0 - see [LICENSE](LICENSE).
Reverse engineering was done with REA (IL2CPP metadata plus native code).
