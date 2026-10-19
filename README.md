# 知云自有 API 适配层

为知云文献翻译 V8.9.04 接入自己的 OpenAI 兼容 API，支持未登录时进行输入文字和 PDF 划选翻译。

本项目与知云官方无关联，使用时需自行安装知云。仓库不包含原程序、安装包或 API Key。

## 使用

1. 退出已经打开的知云，双击 `start.cmd`。
2. 首次使用时，在弹窗中选择包含 `ZhiYunTranslator.exe` 的安装文件夹。
3. 在右侧翻译面板顶部最左端打开「自有 API → 接口设置…」，填写 Base URL、Key、模型名和目标语言。
4. 使用「自有 API → 输入文字翻译…」，或打开 PDF 后划选文字。
5. 退出后重新启动必须同样双击` start.cmd `运行。
6. 若不需要使用该项目，直接启动安装目录原文件(ZhiYunTranslator.exe)即可。

更换安装位置时双击 `install.cmd` 重新选择，已有接口配置会保留。

## 要求与限制

- Windows、Windows PowerShell 5.1、.NET Framework 4.6.2 或更新的 4.x 版本。
- 仅适配且测试过知云 V8.9.04

- 接口需支持非流式 Chat Completions。
- **其他任何功能没有作修改**

## Base URL

| 填写内容 | 实际请求地址 |
| --- | --- |
| `https://example.com` | `https://example.com/v1/chat/completions` |
| `https://example.com/v1` | `https://example.com/v1/chat/completions` |
| `https://example.com/proxy/v1` | `https://example.com/proxy/v1/chat/completions` |
| `http://127.0.0.1:8000/v1` | `http://127.0.0.1:8000/v1/chat/completions` |
| 完整的 `/chat/completions` 地址 | 原样使用 |

`example.com` 为示例。实际请填写你的供应商的BaseURL。

## 本机配置

- `.local/settings.json`：真实接口配置，Key 明文保存在本机，不要分享。
- `.local/app-path.txt`：所选安装路径。
- `.local/bridge.log`：加载和错误日志。
- `bin/`：运行时生成的构建产物。

保存接口设置后，下次翻译立即生效。

只在新增的「自有 API」设置里填写 Key。原软件的 ChatGPT 设置与余额查询不属于本适配层。

## 文件说明

- `Bridge.cs`：核心代码，翻译接管、API 请求、设置窗口和译文显示。
- `run.ps1`：文件夹选择、版本与本地依赖校验、编译和启动。
- `0Harmony.dll`：Harmony 依赖。
- `start.cmd` / `install.cmd`：双击启动和重新选择安装目录。
- `settings.example.json`：无真实 Key 的配置模板。
- `LICENSE` / `Harmony-LICENSE.txt`：项目及第三方依赖许可证。

单独重新编译时运行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\run.ps1 -BuildOnly
```

## 回退与排错

退出后直接运行知云原 EXE，即恢复原软件行为。

完全移除时，退出软件，删除本适配层放入知云目录的 `OwnApiBridge.dll`、`0Harmony.dll`，再按需删除本项目。

- 中文乱码或 `UnexpectedToken`：保留 `run.ps1` 的 UTF-8 BOM，并使用提供的 CMD 入口。
- 找不到新增菜单：确认从 `start.cmd` 启动，标题包含「自有 API」；菜单位于右侧翻译面板顶部最左端。
- 路径错误：双击 `install.cmd` 重新选择。
- 版本不匹配：不能直接跳过哈希检查，需要重新适配。
- HTTP 401：检查 Key 和权限；404：检查 Base URL 路径。
- 缺少 `choices` 或 `content`：服务返回格式不兼容。

## 许可与依赖

项目代码使用 [MIT License](LICENSE)。知云属于独立的第三方软件，不在本许可证授权范围内。

随附的 `0Harmony.dll` 来自 [Lib.Harmony 2.3.3](https://github.com/pardeike/Harmony) 的 net452 版本，作者 Andreas Pardeike，MIT 许可全文见 [Harmony-LICENSE.txt](Harmony-LICENSE.txt)。

## 使用时出现问题？

请在本仓库issue处提出。