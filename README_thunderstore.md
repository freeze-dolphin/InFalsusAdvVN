# InFalsusAdvVN 

In Falsus 剧情模式增强模组

BepinEx mod that enhances Visual Novel reading experience for InFalsus.

[简体中文](#简体中文) · [English](#english)

---

## 简体中文

此模组仅对剧情模式生效

### 功能

#### 实时切换剧情语言

剧情模式中按下快捷键（默认为`L`），实时修改文本语言（仅影响文本、人名、手机中的信息）

- 只改剧情文本，**不会**修改游戏设置里的语言和存档
- 语言切换快捷键只在剧情场景里响应

#### 鼠标滚轮导航

- 鼠标上滚轮：打开历史记录（可通过鼠标右键关闭）
- 鼠标下滚轮：翻到下一句文本

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
```

---

## English

This mod only affects the visual novel (story) mode.

### Features

#### Switch the story language in real time

While reading, press the hotkey (default `L`) to change the text language on the fly. Only the
dialogue text, the speaker names and the messages on the phone are affected.

- Only the story text is changed: the language in the game settings and your save file are untouched
- The hotkey only reacts inside the story

#### Mouse wheel navigation

- Wheel up: open the history (close it with a right click)
- Wheel down: advance to the next line

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
```
