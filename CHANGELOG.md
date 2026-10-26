# Changelog

## 1.0.0

两个独立功能：**LanguageSwitch**（快捷键实时切换剧情语言）与 **Scroll**（鼠标滚轮导航）。

### LanguageSwitch — 剧情文本实时切换

* 快捷键（默认 `L`）在配置的语言列表里循环：`English / 简体中文 / 日本語 / 繁體中文`。
* 当前正在显示的句子会立刻切换；之后每一句都由游戏本体用目标语言解析与渲染，
  字体、字号、换行规则与原版一致。
* 说话人名字牌与正文同步切换（`DynamicStringMapping` 的 `StoryName`，记忆原始 `RawStoryName` 键）。
* 历史记录（LOG）随快捷键实时切换：打开时按目标语言构建，已打开时就地重译。
* 只影响剧情文本：菜单、设置、卡牌等 UI 语言保持不变。
* 配置节 `[LanguageSwitch]`：`Hotkey` / `LanguageOrder` / `RememberSelection` /
  `ManageRendererVisibility`（原 `[General]`）。

### Scroll — 鼠标滚轮导航

* 滚轮向下 = 鼠标左键单击（推进一句），走游戏自身的输入触发 `StoryScene._dp()`。
* 滚轮向上 = 点击 LOG 按钮（打开历史记录），走 `StoryScene._Ep()` + `ChatLogContainer._aS`。
* 历史记录打开时滚轮交还给面板，不会在背后推进剧情。
* 配置节 `[Scroll]`：`WheelDownAdvances` / `WheelUpOpensLog`。

### 实现

* 语言：在 `StoryCommandProcessor.ProcessSayTextCommand` 与 `StoryScene._jp` 上挂钩子，
  仅在调用期间切换 `Str.Strings.targetLocalization`；`FastText` 的渲染路径保持游戏原生。
* 滚轮：从 `InputSystem.onEvent` 的原始事件中解析 `MouseState.scroll`
  （设备级读取在该构建下恒为 0），监听器进入剧情后才挂载。
* 所有类型/字段运行时按名字解析，无硬编码地址。
