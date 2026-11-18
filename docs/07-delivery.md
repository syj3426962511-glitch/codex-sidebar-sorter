# Codex Sidebar Sorter 1.4.2

独立运行的 Codex 项目与聊天排序工具，提供七种排序、十二种界面语言及独立后台。

## 选择安装包

| 系统 | 文件 | 验收范围 |
| --- | --- | --- |
| Windows x64 | Windows/CodexSidebarSorter-1.4.2-Windows-Setup.exe | 安装、运行中覆盖、设置保留、关窗后台、重开及卸载通过。官方客户端生命周期使用未登录配置 |
| Ubuntu 26.04.1 x64 | Linux/CodexSidebarSorter-1.4.2-Linux-x64-Setup.run | 官方客户端 26.1111.21308 的登录后七种排序、后台自动变更、客户端退出重开恢复、工具卸载后客户端继续运行通过 |

安装包已包含运行环境、图标、多语言资源和源码。与已登录、兼容的 Codex Desktop 安装在同一台电脑上。

Intel Mac 另有安装候选，工具安装和生命周期检查通过。Mac 原侧栏排序、生产辅助功能接入、Apple 芯片安装验收及发行签名继续待完成。

[Mac 安装候选](https://github.com/syj3426962511-glitch/codex-sidebar-sorter/releases/download/v1.4.2-preview/CodexSidebarSorter-1.4.2-macOS-x64-Candidate.dmg)

## 安装

Windows 双击 EXE，选择安装位置，再从快捷方式打开工具。

Linux 为安装包增加执行权限并指定安装位置。

```sh
chmod +x CodexSidebarSorter-1.4.2-Linux-x64-Setup.run
./CodexSidebarSorter-1.4.2-Linux-x64-Setup.run --prefix "$HOME/Applications/CodexSidebarSorter"
```

系统具有 Zenity 时可通过文件夹选择窗口指定位置。安装器会建立应用入口。再次使用相同位置安装，可覆盖工具并保留偏好。

## 排序

1. 打开已登录、兼容的 Codex Desktop。
2. 打开 Codex Sidebar Sorter，点击“刷新”。
3. 检查项目和聊天列表。
4. 默认启用“自动保持名称升序”，新增或改名后由后台再次同步。
5. 使用其他规则时，选择范围、排序方式，核对预览并点击“应用到侧栏”。

七种方式包括名称升序、名称降序、数字自然升序、数字自然降序、最近更新优先、最早更新优先及手动顺序。选择自定义规则或手动移动后会暂停自动名称排序，再次勾选自动选项即可恢复。

## 语言与后台

界面支持简体中文、繁体中文、英语、日语、韩语、法语、德语、西班牙语、葡萄牙语、意大利语、俄语及阿拉伯语。阿拉伯语采用从右到左布局。

关闭工具窗口后，后台与托盘继续运行。通过托盘“打开聊天排序”重新显示界面，通过“退出程序”停止工具。Codex 关闭期间，后台等待可用连接。

## 技能与适用范围

AgentSkill/codex-sidebar-sorter 包含可选的助手技能、兼容说明和 MIT 许可证。Agensi 通过连接交付同一份技能。独立程序提供排序界面和后台。

排序覆盖符合接口条件的未置顶项目、Codex 项目内聊天，以及置顶或自定义分区内的聊天。最近聊天独立名称排序、置顶项目位置和 ChatGPT 项目内聊天需要客户端补充支持。实际客户端跨版本替换及其他构建继续分别验收。

Linux 真实验收使用两个独立测试项目和六条聊天。共享界面回归使用三十个测试项目和三百六十条测试聊天。

[统一下载](https://github.com/syj3426962511-glitch/codex-sidebar-sorter/releases/tag/v1.4.2-preview) · [验收记录](https://github.com/syj3426962511-glitch/codex-sidebar-sorter/blob/main/docs/02-validation.md) · [兼容记录](https://github.com/syj3426962511-glitch/codex-sidebar-sorter/blob/main/docs/04-compatibility.md)
