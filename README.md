# Snipaste Lite

一个可直接运行的 Windows 截图工具，参考 Snipaste 的截图、标注和桌面贴图交互，另提供自动滚动和手动采集的长截图功能。

## 功能

- **区域截图**：`F1` 拖选区域，跨显示器选区，显示像素尺寸，`Esc` 或右键取消。
- **标注编辑**：矩形、椭圆、箭头、画笔、文字、马赛克，颜色、线宽、撤销、缩放，导出原始分辨率 PNG。
- **桌面贴图**：`F3` 将剪贴板图片置顶。拖动位置，滚轮缩放，右键切换半透明，双击或 `Esc` 关闭。
- **滚动长截图**：`Ctrl+Alt+L` 选择内容区域，支持自动滚动和手动滚动后逐屏采集，匹配重叠区域后拼接。
- **本地历史**：默认保存最近 100 张原始截图，可关闭自动保存；双击历史图片继续编辑。标注导出由编辑器单独保存。
- **托盘常驻**：关闭工作台后仍可使用全局快捷键，从托盘菜单退出。
- **GitHub 上传**：提供源码上传脚本，GitHub Actions 自动构建测试、生成便携包，推送 `v*` 标签可生成 Release。

## 运行

Windows 10 / 11，安装系统自带或微软提供的 **.NET Framework 4.8**。无需 Node.js、Python、.NET SDK、管理员权限或额外 NuGet 依赖。

双击 `dist\SnipasteLite.exe`。也可以在项目目录运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\run.ps1
```

首次运行脚本会自动编译。源码构建：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Test
```

产物为 `dist\SnipasteLite.exe` 和 `SnipasteLite-portable.zip`。Visual Studio 也可打开 `SnipasteLite.csproj`（需要 .NET Framework 4.8 targeting pack）。

## 长截图操作

1. 打开目标网页或文档，等待图片、字体加载完成，滚动到想截图的起点。
2. 按 `Ctrl+Alt+L`，拖选**可以滚动的内容区域**，避开浏览器标签栏、固定页头、固定底栏和侧边栏。
3. 选择自动滚动，点击「开始」。工具以选区中心为滚动目标，每次滚动后等待指定毫秒数再采集；连续三次画面未变化时自动完成。
4. 动画、懒加载或特殊滚动控件可使用手动模式：点击「开始」采集首屏，自己向下滚动少量，再点击「采集一屏」。相邻画面保留至少约 20% 的重叠；推荐每次滚动不超过半屏。
5. 点击「完成」进入编辑器，复制或保存长图。取消则丢弃本次采集。

匹配不可靠时会暂停并保留已拼接部分；返回上一位置后可在手动模式重试。自动模式暂停后可直接完成，重新发起截图可更换模式。超过 30,000 像素高或 8,000 万总像素时停止追加。只支持向下的垂直滚动；整屏无纹理、视频、动态布局、受保护窗口和固定覆盖内容可能无法正确拼接。不同滚动控件对系统滚轮响应不同，可改用手动模式。

## 快捷键

| 操作 | 快捷键 |
| --- | --- |
| 区域截图 | `F1` |
| 剪贴板贴图 | `F3` |
| 长截图 | `Ctrl+Alt+L` |
| 保存编辑结果 | `Ctrl+S` |
| 复制编辑结果 | `Ctrl+C` |
| 撤销最近标注 | `Ctrl+Z` |

如果已经运行官方 Snipaste 等使用相同快捷键的程序，工作台会提示快捷键冲突，可使用按钮或托盘菜单。

截图和设置目录：`%LOCALAPPDATA%\SnipasteLite`。应用本身不会向网络上传截图；GitHub 上传脚本只上传本项目源码，构建产物和运行数据均不纳入 Git。

## 上传源码到 GitHub

安装 Git、GitHub CLI 并执行一次 `gh auth login`。默认新建当前用户的 **私有** `snipaste` 仓库：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\publish-github.ps1
```

也可指定归属、名称、可见性：

```powershell
.\scripts\publish-github.ps1 -Owner your-account -Repository snipaste -Visibility private
```

脚本初始化本目录 Git，提交源码，创建仓库并推送。已有 origin 指向其他仓库时停止；远端存在冲突时不会强制覆盖。已存在仓库的可见性不会被更改。没有保存访问令牌到项目中。

每次推送会运行 Windows 构建和测试，可从 Actions 下载便携程序。发布版本：

```powershell
git tag v0.1.0
git push origin v0.1.0
```

## 验证与结构

`build.ps1 -Test` 验证连续滚动、任意像素位移、重复帧、无关图像拒绝、尺寸变化和安全上限，以及标注输出。`build.ps1 -Test -Preview` 额外生成工作台和编辑器布局预览，并在交互桌面上验证真实屏幕区域采集、系统滚轮驱动的自动长截图与拼接结果。

```text
src/                    Windows Forms 界面与截图、编辑、拼接实现
tests/                  无第三方依赖的回归测试
scripts/publish-github.ps1
.github/workflows/      Windows 构建与标签发布
build.ps1               本机构建、测试和便携包
run.ps1                 构建后启动
```

MIT License。
