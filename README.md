# DbAgent CLI

一个可像 `codex` 一样从命令行启动的数据库智能 Agent。

## 功能

- `dbagent` 在任意目录启动交互式数据库 Agent
- `dbagent init` 初始化默认配置到 `~/.dbagent/`
- `dbagent models` 查看模型配置
- `dbagent --model <name>` 使用指定模型启动
- `dbagent --config <path>` 使用指定配置启动
- MCP 接入 `DatabaseMcpServer`
- 工具调用展示、危险工具审批、流式输出、token 统计
- 应用内模型切换：`/models`、`/model use <name>`、`/model add`

## 1. 安装依赖

```powershell
dotnet tool install --global DatabaseMcpServer
```

## 2. 打包并安装为全局命令

在项目根目录执行：

```powershell
dotnet pack .\src\DbAgent\DbAgent.csproj -c Release
dotnet tool install --global --add-source .\nupkg DbAgent.Tool
```

安装后可直接运行：

```powershell
dbagent --help
dbagent init
dbagent
```

## 3. 配置

运行：

```powershell
dbagent init
```

会生成：

```text
%USERPROFILE%\.dbagent\appsettings.json
%USERPROFILE%\.dbagent\databases.json
```

编辑 `appsettings.json`：

- `Models.Profiles[].Endpoint`
- `Models.Profiles[].Model`
- `Models.Profiles[].ApiKey` 或 `ApiKeyEnvironmentVariable`

编辑 `databases.json`：

- 数据库连接串
- 数据库类型

## 4. CLI 用法

```powershell
dbagent
dbagent --model local-main
dbagent --config D:\profiles\prod.json
dbagent init
dbagent init --force
dbagent models
```

配置查找顺序：

1. `--config`
2. 环境变量 `DBAGENT_CONFIG`
3. 当前目录 `appsettings.json`
4. `~/.dbagent/appsettings.json`
5. 程序目录 `appsettings.json`

## 5. 应用内命令

```text
/help
/tools
/config
/stats
/clear
/models
/model
/model current
/model use <name>
/model add
/model edit <name>
/model remove <name>
/exit
```

## 6. 更新本地全局工具

```powershell
dotnet pack .\src\DbAgent\DbAgent.csproj -c Release
dotnet tool update --global --add-source .\nupkg DbAgent.Tool
```

## 7. 卸载

```powershell
dotnet tool uninstall --global DbAgent.Tool
```

## 说明

- 默认配置是只读模式。
- 若开启写入或架构写入工具，危险操作仍会进入人工审批。
- 若自定义模型端点在流式响应中不返回 usage，则 token 统计会显示 `usage unavailable`。
