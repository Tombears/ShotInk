# 提交与发布

- 用户当前约定：每次完成修改并通过必要检查后，提交并推送 Git；无需等待“更新 git”。
- GitHub 的 Git 拉取和推送统一使用 SSH（git@github.com:...），不改回 HTTPS。
- 同步维护并发布 `ShotInk-portable.zip`（完整 OCR 版）与 `ShotInk-portable-lite.zip`（不含 OCR 的轻量版），不生成或提供 EXE 安装包、独立 EXE 下载。通用功能共用源码，两版同一次构建、测试、发布；轻量版编译移除 OCR 逻辑和按钮，不打包 OCR 文件。两版均不打包 docs，历史附件保留。
- 小修复、样式微调保持当前版本号，更新该版本的中文说明并替换最新 Release 的 ZIP 包。
- 新增较多功能或较大改动时才增加版本号、创建新版本；不要为每次提交创建版本。
- 版本号统一修改 `src/AssemblyInfo.cs`，发布说明写入对应的 `releases/v版本号.md`。
- `main` 分支通过 Windows 构建和测试后自动发布：已有版本替换附件，新版本创建 Release。
- 替换附件时保留原有标签和历史 Release，不移动已发布的标签。README 和发布说明仅面向用户介绍功能与使用方式，不添加内部提交、发布约定或源码提交链接。
- GitHub 将版本设为不可变发布时，仍更新可编辑的发布说明，并明确报告附件无法替换；不要擅自删除版本、改变仓库安全设置或增加版本号绕过限制。
- 构建产物、OCR 模型和用户数据不提交到 Git。不要推送或发布未通过必要检查的 ZIP 包。
- 程序的版本更新检查和下载统一使用 Gitee。GitHub 发布成功后，以该 Release 已验证的两份 ZIP 执行 `scripts/publish-gitee-update.py --archive <完整版ZIP> --lite-archive <轻量版ZIP>`，使用本机保存的 Gitee 令牌发布；验证匿名下载成功后，再提交 `updates/latest.json` 和 `updates/latest-lite.json` 并同步 GitHub、Gitee。两版只能使用自己的更新包，不要提前发布索引，不把令牌写入程序、源码或 Git。
