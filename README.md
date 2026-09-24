# Voice2Txt — Windows 语音转文字

> 🔗 [项目主页](https://fwz233-RE.github.io/Voice2Txt/) · [最新版下载](https://github.com/fwz233-RE/Voice2Txt/releases/latest)

[voicestick-mindex](https://github.com/fwz233-RE/voicestick-mindex)（macOS 版）的 Windows 移植版：**长按热键说话**，松开后调用小米 **MiMo-V2.5-ASR**（Token Plan API）识别，自动把文字粘贴到前台窗口。支持 Windows ARM64。

## 功能

- **长按说话**：默认 `Fn` 长按录音、松开识别（点按无效，长按阈值 250ms）；通过 Raw Input 捕获 `Fn` 这类常规接口看不见的键
- **自动粘贴**：识别完成后剪贴板写入并 `Ctrl+V` 粘贴到前台应用（可关闭）
- **再次插入 / 复制**：粘贴失败时一键重插，结果始终保留在窗口里
- **托盘常驻**：关闭窗口缩到托盘，热键继续可用
- **识别历史**：本地保存（`%APPDATA%\Voice2Txt\history.jsonl`），双击复制
- **API Key 只存本机**：环境变量或配置文件，不进源码、不进构建产物

## 快速开始

```powershell
# 构建 exe
.\build.ps1          # 得到 bin\Voice2Txt.exe

# 或者不构建直接跑（PowerShell 7 内存编译，原生 ARM64）
.\run.ps1
```

首次运行会弹出设置窗口，粘贴 Token Plan API Key（`tp-` 开头）即可。

## 识别接口

小米开放平台 Token Plan（OpenAI 兼容格式）：

- `POST https://token-plan-cn.xiaomimimo.com/v1/chat/completions`（集群 cn/sgp/ams 换 BASE_URL）
- 请求头 `api-key: tp-...`
- 请求体 `model=mimo-v2.5-asr` + `input_audio`（data URI）+ `asr_options.language`
- 结果取 `choices[0].message.content`

> 该接口是**一段式**（录完再传），不是流式，所以没有逐句上屏；按住说话期间窗口只显示录音状态，松开后 1~3 秒出全文。

## API Key 配置

优先级：环境变量 > `%APPDATA%\Voice2Txt\config.txt`。环境变量名（任选其一）：

```powershell
$env:MIMO_API_KEY = "tp-..."
# 或 DASHSCOPE_API_KEY / ALIYUN_API_KEY / VOICE_TO_TEXT_API_KEY
```

配置文件可参考 `config.example.txt`，在设置窗口里改会自动保存。**请勿把真实 Key 写进仓库、提交记录或截图**；泄露后立即到 Token Plan 控制台撤销重建。

## 使用

| 操作 | 效果 |
|---|---|
| **长按 `Fn`**（默认，可在设置里改） | 开始录音（点按忽略） |
| 松开 | 结束识别，自动粘贴到前台窗口 |
| 按住「按住说话」按钮 | 同上（鼠标操作） |
| 设置 → 录制热键… | 按下任意键实测绑定（Fn 等非常规键必须用这个） |
| 再次插入 | 把窗口文字重新粘贴出去 |
| 复制 / 清空 / 历史 | 剪贴板、清框、历史记录（双击条目复制） |
| 关闭窗口 | 缩到托盘，热键继续可用 |
| 托盘 → 退出 | 真正退出 |

热键格式：`Fn` / `Ctrl+Shift+Space` / `Alt+F2` / `VK:500`（录制捕获的原始键 id）。

### 关于 Fn 键

Fn 是键盘固件层的键，多数键盘不向系统上报——但相当一部分键盘（尤其笔记本、Mac 键盘）会在 Raw Input 里以「未映射键」出现，本程序正是靠 Raw Input 捕获它们。如果你的 Fn 按下后**完全无反应**，说明键盘根本没上报它（软件无法绑定），请到「设置 → 录制热键」换绑其它键。

## 构建选项（ARM64 相关）

| 命令 | 产物 | 架构 |
|---|---|---|
| `.\build.ps1` | `bin\Voice2Txt.exe` | 有 .NET SDK 时 = **原生 win-arm64** 单文件；无 SDK 时 = .NET Framework 独立 exe（ARM64 Windows 上走 x64 模拟，功能无差别） |
| `.\build.ps1 -InstallSdk` | — | winget 安装 .NET SDK 8（arm64），装完重开终端再构建即得原生 exe |
| `.\build.ps1 -SelfContained` | 较大单文件 | SDK 路径下打包运行时，目标机器无需装 .NET |
| `.\run.ps1` | 无产物 | PowerShell 7 内存编译直接跑（进程即本机架构） |

## 目录结构

```
Voice2Txt.cs        全部源码（C# 5 语法，单文件）
run.ps1             开发运行（内存编译）
build.ps1           构建 exe（SDK 原生 / 5.1 兜底两条路）
config.example.txt  配置文件示例
bin\Voice2Txt.exe   构建产物
build\              SDK 构建的临时工程
```

## 实现对照（与 voicestick-mindex 的关系）

| 模块 | macOS 版（DashScope） | Windows 版（MiMo Token Plan） |
|---|---|---|
| 识别 | WS 流式、逐句上屏 | HTTP 一段式（接口所限），全文返回 |
| 采集 | AVAudioEngine + 重采样到 16k | winmm `waveIn`，16k 直采，设备不支持时 44.1k/48k 线性重采样，WAV 封装上传 |
| 热键 | `⌘⇧空格` 按住 | `Fn` 长按（Raw Input 捕获 + 250ms 长按阈值），可录制绑定 |
| 自动插入 | CGEvent `⌘V` | `keybd_event` `Ctrl+V`，记录录音时的前台窗口用于「再次插入」 |
| 配置 | 环境变量 / config.plist | 环境变量 / config.txt |
| 历史 | 本地保存 | `%APPDATA%\Voice2Txt\history.jsonl` |

## 故障排查

- **「麦克风错误」**：系统设置 → 隐私 → 麦克风，允许应用访问
- **识别报错 401 Invalid API Key**：确认 Key 是 Token Plan `tp-` 开头、集群（BASE_URL）选对（cn/sgp/ams）
- **Fn 没反应**：见上文「关于 Fn 键」；用设置里的「录制热键」实测绑定，捕获不到就换键
- **粘贴没进目标窗口**：用「再次插入」按钮；或先点一下目标窗口再按住说话
