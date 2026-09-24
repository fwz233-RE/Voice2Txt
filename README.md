# Voice2Txt — Windows 语音转文字

> 🔗 [项目主页](https://fwz233-re.github.io/Voice2Txt/) · [最新版下载](https://github.com/fwz233-RE/Voice2Txt/releases/latest)

Windows 桌面语音输入小工具：**按住 `Ctrl+Alt` 说话**（热键可改），松开后文字自动插入光标处。识别走小米 **MiMo-V2.5-ASR**（Token Plan API），支持 Windows ARM64（原生单文件）。

> 前身为 macOS 版菜单栏语音输入应用，本仓库为 Windows 重制版（Win32/WinForms + Raw Input 实现）。

## 功能

- **按住说话**：默认 `Ctrl+Alt` 按住录音、松开识别（点按忽略，250ms 长按阈值防误触）；热键可改，支持「录制热键」实测绑定 `Fn` 等非常规键
- **准实时上屏**：说话停顿即切段流水识别，文字逐段跳进面板，不必等松开（API 为整段式，故为「停顿切段」的准流式）
- **面板自动呼出/消失**：按住热键时面板在光标旁置顶呼出（不抢焦点），松开/插入完成后自动隐藏
- **自动插入**：识别完成写入剪贴板并 `Ctrl+V` 到前台窗口，带「再次插入 / 复制」兜底
- **识别历史**：本地保存（`%APPDATA%\Voice2Txt\history.jsonl`），双击复制
- **开机自启**：设置内一键开关（HKCU Run 键）
- **API Key 只存本机**：环境变量或配置文件，不进源码、不进产物、不进仓库

## 快速开始

1. 从 [Releases](https://github.com/fwz233-RE/Voice2Txt/releases/latest) 下载对应架构的 zip（`win-arm64` 或 `win-x64`），解压
2. 运行 `Voice2Txt.exe`（单文件，图标已内置），首次启动填入 Token Plan API Key（`tp-` 开头）
3. 光标点进任意输入框 → **按住 `Ctrl+Alt`** 说话 → 松开，文字自动插入

## 使用

| 操作 | 效果 |
|---|---|
| **按住 `Ctrl+Alt`**（默认，可在设置里改） | 开始录音，面板在光标旁呼出 |
| 松开（任一键） | 面板消失，识别完成后自动粘贴插入 |
| 按住「按住说话」按钮 | 同上，但**面板保持打开**（适合在窗口里连续使用） |
| 设置 → 录制热键… | 按下任意键实测绑定（`Fn` 等非常规键用这个） |
| 设置 → 开机自启动 | 登录自动运行 |
| 再次插入 | 把窗口文字重新粘贴出去 |
| 复制 / 清空 / 历史 | 剪贴板、清框、历史记录（双击条目复制） |
| 关闭窗口 | 缩到托盘，热键继续可用 |

热键格式：`Ctrl+Alt` / `Ctrl+Shift+Space` / `Alt+F2` / `VK:500`（录制捕获的原始键 id），支持组合键与纯修饰键。

### 关于 Fn 键

`Fn` 多为键盘固件层按键，是否上报系统因键盘而异。本程序通过 Raw Input 捕获（含未映射键），部分键盘的 Fn 可直接绑定；若捕获不到，请用「录制热键」换绑其它键。

## 识别接口

小米 MiMo-V2.5-ASR（OpenAI 兼容格式），**双订阅自动识别**：

| Key 类型 | 端点 | 鉴权 |
|---|---|---|
| Token Plan（`tp-` 开头） | `https://token-plan-{cn,sgp,ams}.xiaomimimo.com/v1/chat/completions` | `api-key` |
| 官方开放平台订阅 | `https://api.xiaomimimo.com/v1/chat/completions` | `api-key` 或 `Bearer`（两种都发） |

- 程序按 Key 前缀自动选端点；`config.txt` 的 `BASE_URL` 可手动覆盖（如指定 sgp/ams 集群）
- 请求体：`model=mimo-v2.5-asr` + `input_audio` data URI + `asr_options.language`，结果取 `choices[0].message.content`
- 该接口为**整段式**（无流式），故客户端做「停顿切段 + 流水识别」实现实时感

## API Key 配置

优先级：环境变量 > `%APPDATA%\Voice2Txt\config.txt`（Key 类型自动识别，tp- 走 Token Plan，其它走官方订阅）：

```powershell
$env:MIMO_API_KEY = "tp-..."   # 或 DASHSCOPE_API_KEY / ALIYUN_API_KEY / VOICE_TO_TEXT_API_KEY
```

配置文件可参考 `config.example.txt`。**请勿把真实 Key 写进仓库、提交记录或截图**；泄露后立即到 Token Plan 控制台撤销重建。

## 从源码构建

```powershell
.\build.ps1            # 产出 bin\Voice2Txt.exe（win-arm64 单文件）
.\build.ps1 -Arch x64  # win-x64 版本（产出 bin-x64\）
.\build.ps1 -InstallSdk # 没装 .NET SDK 8 时先装（arm64）
.\run.ps1              # 不构建直接跑（PowerShell 7 内存编译）
```

| 命令 | 产物 | 说明 |
|---|---|---|
| `.\build.ps1` | win-arm64 单文件 exe | 需 .NET SDK 8；图标嵌入 exe |
| `.\build.ps1 -Arch x64` | win-x64 单文件 exe | 普通 64 位机器用 |
| `.\build.ps1 -SelfContained` | 较大单文件 | 目标机器无需装 .NET |
| `.\build.ps1`（无 SDK 时） | .NET Framework 独立 exe | 兜底路径，ARM64 上走模拟 |

## 目录结构

```
Voice2Txt.cs        全部源码（单文件）
run.ps1             开发运行（内存编译）
build.ps1           构建脚本（SDK 原生 / 5.1 兜底）
index.html          GitHub Pages 项目主页
config.example.txt  配置示例
assets/             Material 图标生成脚本与 icon.ico
build/              SDK 构建的工程文件
```

## 故障排查

- **麦克风错误 / 没录到声音**：系统设置 → 隐私 → 麦克风，允许桌面应用访问
- **识别报错 401 Invalid API Key**：确认订阅类型与 Key 匹配（tp- → Token Plan；普通 → 官方接口）；必要时在 config.txt 用 `BASE_URL` 指定集群
- **热键没反应**：设置 → 录制热键 实测绑定；某些键盘 Fn 不上报系统，需换键
- **粘贴没进目标窗口**：用「再次插入」按钮；或先点一下目标窗口再说话

## License

MIT
