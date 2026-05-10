# DbAgent CLI 框架规范化计划（CLI + Generic Host）

## Summary
- 目标是把当前单文件、强耦合的 CLI 工具规范成 ASP.NET Core / Microsoft.Extensions 风格的可维护控制台应用，但不改产品形态，仍然保持 `dbagent` 命令行工具。
- 本轮采用单主项目 + 单测试项目的稳妥路线：主代码仍在 `src/DbAgent`，新增 `tests/DbAgent.Tests`，不拆成多业务程序集。
- 外部行为保持兼容：CLI 命令名、配置文件名、配置查找顺序、交互式体验、`doctor/models/init/chat` 语义都不变。
- 实施会以 `src/DbAgent/Program.cs` 瘦身为核心，把现有 1800+ 行职责拆成可注入服务和命令处理器。

## Key Changes
- 引入 Generic Host 作为组合根：
  - `Program.cs` 只负责解析原始启动参数、创建 `HostApplicationBuilder`、注册服务、分发命令。
  - 增加 `Microsoft.Extensions.Hosting`、`Microsoft.Extensions.Logging.Console` 等基础依赖，统一 DI 和日志生命周期。
- 采用单项目分层目录，不做多 csproj 清架构：
  - `Configuration`：配置模型、配置读取/保存、路径解析、配置校验。
  - `Commands`：`init`、`doctor`、`models`、`chat` 的 handler。
  - `Services`：模型选择、Agent 创建、MCP 工具过滤、审批策略、会话运行。
  - `ConsoleUi`：Spectre.Console 输出、提示输入、面板渲染。
  - `Models`：`CliOptions`、`DoctorCheck`、运行时状态类与纯数据对象。
- 明确内部接口：
  - `ICommandHandler`
  - `IAppConfigStore`
  - `IConfigPathResolver`
  - `IAgentFactory`
  - `IModelProfileService`
  - `IApprovalPolicy`
  - `IConsoleUi`
- 配置策略保持兼容优先：
  - 不把用户配置强行改造成默认 ASP.NET Core `appsettings.*` 流水线主入口。
  - 继续保留当前“显式路径 -> 环境变量 -> 当前目录 -> 用户目录 -> 程序目录”的查找顺序。
  - `AppConfig` 等 POCO 继续作为配置契约；运行时通过 `IAppConfigStore` 管理。
- 命令与交互重构边界：
  - `init`：只负责生成默认配置。
  - `doctor`：只负责探测模型、MCP、数据库配置并输出报告。
  - `models` 与 `/model ...`：统一走 `IModelProfileService`。
  - `chat` 与流式输出：统一走 `ChatSessionRunner`，其中再拆审批收集、token 统计、流式 UI 渲染。
- 兼容性要求：
  - CLI 帮助文本可以整理，但命令集合不删不改名。
  - 配置 JSON 结构保持向后兼容，现有 `OpenAI` legacy 字段兼容逻辑继续保留。
  - `dbagent` 仍保留打包为 dotnet tool 的方式，不改变 `PackageId` / `ToolCommandName`。

## Public Interfaces / Behavior
- 对外 CLI 接口保持不变：
  - `dbagent`
  - `dbagent init [--force]`
  - `dbagent models`
  - `dbagent doctor [--model <name>]`
  - `dbagent --model <name>`
  - `dbagent --config <path>`
- 对外配置契约保持不变：
  - `appsettings.json` 顶层 `Models` / `Database` / `Mcp` / `Safety` 结构不变。
  - `databases.json` 路径解析语义不变。
- 新增的只是内部接口与目录组织，不引入新的用户必填配置项。

## Test Plan
- 新增 `tests/DbAgent.Tests`，使用 xUnit。
- 单元测试最少覆盖：
  - `CliOptions.Parse`
  - `ConfigPathResolver`
  - `AppConfig` 规范化/校验
  - `ModelProfileService`
  - `ApprovalPolicy`
- 手工 smoke test：
  - `dotnet build 'DbAgent.sln'`
  - `dotnet run --project '.\src\DbAgent\DbAgent.csproj' -- --help`
  - `dotnet run --project '.\src\DbAgent\DbAgent.csproj' -- init`
  - `dotnet run --project '.\src\DbAgent\DbAgent.csproj' -- models`
  - `dotnet run --project '.\src\DbAgent\DbAgent.csproj' -- doctor`

## Assumptions
- 采用已确认路线：CLI + Host、稳妥规范、补基础单测。
- 不转 ASP.NET Core Web API，不引入 Controller/Middleware 作为主结构。
- 不拆多项目 Clean Architecture；先用单主项目分层降低影响面。
- Spectre.Console 继续作为 UI 层实现，不替换终端交互库。
- 若某些高度耦合逻辑不适合一次性完全拆散，优先先提纯服务，再收窄 `Program.cs`。
