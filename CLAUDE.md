# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 构建与测试

```powershell
dotnet build DbAgent.sln
dotnet test DbAgent.sln
```

## 开发运行

首次运行需先初始化配置：

```powershell
dotnet run --project .\src\DbAgent\DbAgent.csproj -- init
```

之后直接运行（无需参数），程序会自动读取 `~/.dbagent/appsettings.json`：

```powershell
dotnet run --project .\src\DbAgent\DbAgent.csproj
```

指定模型或配置文件：

```powershell
dotnet run --project .\src\DbAgent\DbAgent.csproj -- --model local-main
dotnet run --project .\src\DbAgent\DbAgent.csproj -- --config "D:\dbagent-dev\appsettings.json"
```

## 配置查找顺序

`--config` 参数 > `DBAGENT_CONFIG` 环境变量 > 当前目录 `appsettings.json` > `~/.dbagent/appsettings.json` > 程序目录 `appsettings.json`

本地配置文件 `appsettings.json` 和 `databases.json` 已 gitignore，不提交到版本控制。

## 外部依赖

MCP 服务器需全局安装：

```powershell
dotnet tool install --global DatabaseMcpServer
```

## 分支与 PR 规范

- 新功能使用 `feature/*` 分支
- 通过 PR 合并到 `master`，不直接推送 master

## 架构规则

- **Generic Host** 作为组合根，统一 DI 和日志生命周期
- **CommandDispatcher** 负责路由 `init / doctor / models / chat` 命令
- 通过接口解耦：`ICommandHandler`、`IAppConfigStore`、`IConfigPathResolver`、`IAgentFactory`、`IModelProfileService`、`IApprovalPolicy`、`IConsoleUi`
- 单主项目策略：业务代码在 `src/DbAgent`，测试在 `tests/DbAgent.Tests`，不拆多业务程序集
- 启用 `Nullable` 和 `ImplicitUsings`，目标框架 `net9.0`
