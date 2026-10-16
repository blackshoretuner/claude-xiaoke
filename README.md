# 小克 · Claude 的桌面看板娘

仿照「GPT 娘」做的 Claude 看板娘：一个能拖到桌面任何地方的小人，陪你用 Claude Code。
她会看额度、跟着 Claude 的状态换表情、说话、被摸头，还能直接问她问题。

> 粉丝作品，与 Anthropic 无关。Claude 是 Anthropic 的商标。

![小克](desktop/assets/happy.png)

## 她会做什么

- **桌面挂件**：透明置顶的小窗，可以拖到任意位置；贴到屏幕左右边会翻身，闲着时还会缩到屏幕外探个头。右键菜单、托盘图标、自由缩放（Ctrl + 滚轮）。
- **跟着 Claude 动**：Claude 思考时她托腮，跑工具时干活，答完开心，出错晕乎乎；同时开好几个对话也只出现一个小克，跟着正在忙的那个。
- **额度报告**：点她的身体看 5 小时 / 每周额度和当前对话的上下文；不说话时每 5 分钟自己同步一次用量；额度回满会报喜，用光会喊累。
- **等你批准时叫你**：Claude 停下来等你点「允许」，几秒内没点她就会提醒（自动模式下不会乱叫）。
- **问问小克**：点她手里的书打开对话框，背后是本机的 Claude Code 命令行，可以上网搜索、读网页（不碰你的文件、不跑命令）。
- **好感度**：摸头、陪 Claude 做完长任务、和她聊天都会涨，等级越高摸头台词越多；存在本地。
- **语音（可选）**：用你自己训练的 GPT-SoVITS 声线提前录好台词，平时不跑任何模型，点了就播。
- **Claude Code 里也有她**：`/xiaoke panel` 在应用里打开一个小面板。

## 目录

```
.claude-plugin/plugin.json   模组清单
hooks/                       模组本体（register.tsx）、台词、面板立绘
types/                       模组的状态类型
tests/                       模组测试（claude plugin test）
desktop/                     桌面挂件：XiaoKe.cs（WPF）、build.cmd、assets/（立绘、音效、图标）
art/                         应用内面板用的 WebP 立绘
sounds/                      音效
voice/                       录音脚本、台词表（script.json）、配置样例
data/                        运行时生成：设置、好感度、会话状态（不进仓库）
```

## 安装（Windows）

1. **模组**：在 `~/.claude/settings.json` 里让 Claude Code 每次都加载这个文件夹：

   ```json
   { "env": { "CLAUDE_CODE_PLUGIN_DIRS": "C:\\path\\to\\claude-xiaoke" } }
   ```

   重开 Claude Code（或 Claude 桌面端）后，有人在场的会话会自动把小克请出来。`/xiaoke` 叫她，`/xiaoke panel` 打开应用内面板，`/xiaoke add <语录>` 加自定义台词。

2. **桌面挂件**：双击 `desktop/build.cmd`，用 Windows 自带的 .NET Framework 4.x 编译器生成 `desktop/XiaoKe.exe`，不需要装 SDK。可以给它建个桌面快捷方式。

3. **问问小克**（可选）：对话框用的是 Claude 桌面端自带的命令行，第一次用要登录一次（窗口里有按钮，等同于 `claude auth login`）。

所有运行数据都在仓库里的 `data/`。故意不放 `%LOCALAPPDATA%`：Claude 桌面端是打包应用，从它里面写进 AppData 的文件会被转到它自己的私有目录，从快捷方式打开的挂件就看不到了。

## 语音（可选）

仓库不带任何声音模型和录音。想让她开口：

1. 用 [GPT-SoVITS](https://github.com/RVC-Boss/GPT-SoVITS) 训练或准备一个你有权使用的声线。
2. 复制 `voice/gsv.example.json` 为 `voice/gsv.json`，填上整合包路径、权重、参考音频。`synth` 指向你的合成脚本：它以 `python synth.py <整合包目录> <jobs.json> <results.json>` 调用，读入任务列表（每项有 `out`、`text`、`ref`、`ref_text`、`sovits`、`gpt` 等），合成后写回 `[{out, text, err, heard}]`。
3. 用整合包自带的 Python 录台词：`runtime\python.exe voice\record.py voice\script.json`。产物在 `voice/lines/`，挂件按台词文字自动匹配播放；之后新加的自定义语录会在后台自动补录。

请只使用你有权使用的声音，不要用它冒充真人。

## 开发

- 模组检查与测试：`claude plugin validate .`、`claude plugin test .`
- `.claude-plugin/types/` 是 Claude Code 自带的类型声明，不随仓库发布，改模组代码时再生成。
- 挂件调试：往 `data/snap.request` 写 `quota` / `chat` / `peek` / `love` / `pat`（或留空），挂件会把当前画面存成 `data/snap.png`。

## 版权

- **代码**：MIT，见 [LICENSE](LICENSE)。
- **立绘**（`art/`、`desktop/assets/` 里的 PNG / WebP、`xiaoke.ico`）：「Claude 娘」形象原作者为 ZipZipPipe，表情差分由 AI 编辑生成。立绘不在 MIT 协议范围内，版权归原作者，详见 [ART-NOTICE.md](ART-NOTICE.md)。
