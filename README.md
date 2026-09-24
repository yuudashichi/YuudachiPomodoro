# 惜立番茄钟

一款Windows10/11的简洁番茄钟

![番茄钟与今日任务](docs/images/focus.png)

![每日专注折线图](docs/images/history.png)

## 下载与运行

在 [Releases](https://github.com/yuudashichi/YuudachiPomodoro/releases) 下载 **YuudachiPomodoro-1.0.2-win-x64.zip**，完整解压后进入 **惜立番茄钟** 文件夹，双击 **惜立番茄钟.exe** 即可

## 开发

技术栈：C# / .NET 10、WinUI 3、Win2D、SQLite。构建需要 Windows x64、.NET 10 SDK 和 Windows SDK，以及 Visual Studio C++ x64 构建工具（用于生成无需运行库的原生启动 EXE）；首次还原依赖需联网。

```powershell
dotnet restore src/XiliPomodoro/XiliPomodoro.csproj --packages .packages --locked-mode
dotnet run --project tests/XiliPomodoro.Tests -c Release
powershell -NoProfile -ExecutionPolicy Bypass -File tools/publish.ps1
```

发布脚本从干净目录打包，将唯一可运行版本放在 `dist/惜立番茄钟`，保留已有数据，并将旧结构的 `data` 迁移到 `app/data`；ZIP 和 SHA-256 校验文件放在 `artifacts/packages`。源代码、发布包均排除数据库与个人备份。

- `src/Launcher`：原生启动 EXE，运行 `app` 内的主程序后立即退出。
- `src/XiliPomodoro.Core`：计时规则与统计模型。
- `src/XiliPomodoro`：桌面界面、SQLite 存储与系统集成。
- `tests`：核心测试与可选的界面回归工具；界面诊断代码不编入正式版。
