# InFalsusAdvVN

In Falsus 剧情模式的两组增强：**实时切换剧情语言** 和 **鼠标滚轮导航**。

装好就能用，不需要改游戏设置；所有功能都只在剧情（Visual Novel）里生效，
菜单、设置、卡牌等界面完全不受影响。

---

## 功能

### 1. 一键切换剧情语言（默认 `L`）

剧情中按一下 `L`，**当前正在读的这一句会立刻换成目标语言**，之后的每一句也是该语言：

* 对话正文 ✅
* 说话人名字牌 ✅
* 历史记录（LOG）面板 ✅
* 游戏 UI（菜单 / 设置 / 卡牌）❌ 保持原样

支持游戏自带的四种剧情文本：**English / 日本語 / 简体中文 / 繁體中文**
（韩语在游戏数据里会回落到英文）。语言顺序可以自己配置。

### 2. 鼠标滚轮导航

* **滚轮向下**：推进一句，等同鼠标左键单击。
* **滚轮向上**：打开历史记录（等同点 LOG 按钮）。
* **右键单击**：在历史记录打开时关闭它（等同点关闭按钮）。

其他界面仍使用游戏原本的滚轮 / 右键行为。

---

## 安装

**推荐**：在 Thunderstore / r2modman 里搜索 `InFalsusAdvVN` 直接安装，会自动带上依赖
`BepInExPack_IL2CPP`。

**手动**：把压缩包里的 `BepInEx` 文件夹解压到游戏根目录（与 `infalsus.exe` 同级），
确认存在 `BepInEx/plugins/InFalsusAdvVN/InFalsusAdvVN.dll`。

---

## 配置

首次启动会生成 `BepInEx/config/aria.infalsus.advvn.cfg`，用记事本打开修改即可，改完重启游戏。

```ini
[LanguageSwitch]
# 切换语言的快捷键（Unity Input System 的键名，例如 L / F6 / Tab）
Hotkey = L

# 按快捷键循环的语言顺序
LanguageOrder = English,ChineseSC,Japanese,ChineseTC

# 记住上次选择的语言，下次启动自动套用
RememberSelection = true

[Scroll]
# 滚轮向下 = 下一句
WheelDownAdvances = true

# 滚轮向上 = 打开历史记录
WheelUpOpensLog = true

# 历史记录打开时，右键单击 = 关闭
RightClickClosesLog = true
```

常用改法：

```ini
# 只在英文和简中之间切换
LanguageOrder = English,ChineseSC

# 把快捷键换成 F6，顺序：日 -> 简 -> 繁 -> 英
Hotkey = F6
LanguageOrder = Japanese,ChineseSC,ChineseTC,English

# 不喜欢滚轮翻页，只保留快捷键切换
WheelDownAdvances = false
```

语言名可以写 `English` / `Japanese` / `ChineseSC` / `ChineseTC` / `Korean`，
也可以写简称 `en` / `jp` / `zh-hans` / `zh-hant` / `ko`。

---

## 说明

* 只改剧情文本，**不会**修改游戏设置里的语言，也不改存档。
* 如果切换时这一句正在逐字浮现，它可能用切换前的内容播完；等这句话显示完再按一次即可
  （把文本速度设为 Instant 就没有这个现象）。
* 语言切换只在剧情场景里响应，主菜单、设置界面按 `L` 不会有任何反应。

---

## English

Two enhancements for the **In Falsus** visual novel mode, both active only while reading the story:

### Switch the story language with one key (default `L`)

Press `L` while reading: the line on screen switches **immediately** and every following line stays
in the chosen language — dialogue, speaker name and the LOG history. Menus, settings and card UI are
never touched. The game ships English, Japanese, Simplified Chinese and Traditional Chinese story
text (Korean falls back to English).

### Mouse wheel navigation

* **Wheel down** — advance one line (same as a left click).
* **Wheel up** — open the history panel (same as the LOG button).
* **Right click** — close the history panel while it is open (same as its close button).

Other screens keep the game's normal wheel and right-click behaviour.

### Configuration

Edit `BepInEx/config/aria.infalsus.advvn.cfg` (created on first launch) and restart the game:

```ini
[LanguageSwitch]
Hotkey = L
LanguageOrder = English,ChineseSC,Japanese,ChineseTC
RememberSelection = true

[Scroll]
WheelDownAdvances = true
WheelUpOpensLog = true
RightClickClosesLog = true
```

`LanguageOrder` accepts `English / Japanese / ChineseSC / ChineseTC / Korean` or the short forms
`en / jp / zh-hans / zh-hant / ko`. Set any `[Scroll]` option to `false` to turn that gesture off.

---

GNU General Public License v3.0.
