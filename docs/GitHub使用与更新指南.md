# 词间的 GitHub 使用与更新指南

## 只想用软件

打开仓库，点击右侧 **Releases**，下载 `cijian-0.5.0-win-x64.zip`，完整解压后运行。不要把绿色 Code 按钮下的源码 ZIP 当成安装包。运行环境和数据备份方法见 [使用说明](使用说明.md)。

## 修改一段说明

在 GitHub 打开 README 或 docs 中的文档，点击铅笔编辑，预览无误后点 **Commit changes**，填写例如“补充启动说明”的提交信息即可。提交会保留修改历史。

## 更新电脑上的源码

在自己的源码目录中打开终端。首次上传已经完成，以后修改后可按以下步骤同步：

```powershell
git pull --ff-only
# 在这里修改文件，并完成相应验证
git status
git add README.md docs src tests tools .gitignore .gitattributes NuGet.Config THIRD_PARTY_NOTICES.md licenses
git diff --cached --stat
git commit -m "说明这次具体改了什么"
git push
```

如果 `git pull` 提示冲突或本地修改，不要强制覆盖；先保存自己的改动再处理。提交前查看待提交文件，排除个人 Data、Profiles、导出 JSON、密钥和大型数据库。

## 发布新的可下载版本

1. 修改版本号并运行构建与相关检查。
2. 将发布目录、第三方许可证、使用说明和离线词典打包，检查没有个人档案。
3. 推送源码，打开 **Releases → Draft a new release**。
4. 创建新标签（例如后续的 `v0.5.1`），填写更新内容与运行环境，上传程序 ZIP 和 SHA-256 校验文件。
5. 检查文件和说明后点 **Publish release**。

源码提交和程序包是两个步骤：只修改源码，不会自动替换已发布的 ZIP。

## 本次参考的 GitHub 官方教程

- [将本地代码添加到 GitHub](https://docs.github.com/en/migrations/importing-source-code/using-the-command-line-to-import-source-code/adding-locally-hosted-code-to-github)：初始化、提交、设置远程仓库和推送。
- [README 应包含什么](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/customizing-your-repository/about-readmes)：说明用途、如何开始使用以及如何获得帮助。
- [管理仓库中的发布版本](https://docs.github.com/en/repositories/releasing-projects-on-github/managing-releases-in-a-repository)：发布版本、附件与说明。

GitHub 网页按钮可能随版本变化，以实际页面文字为准。
