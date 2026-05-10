开发模式下有 3 种用法。最推荐第 1 种，因为它和以后正式安装后的行为完全一致。

## 方式 1：先在开发模式执行 `init`

在项目根目录运行：

```powershell
dotnet run --project .\src\DbAgent\DbAgent.csproj -- init
```

它会生成：

```text
C:\Users\你的用户名\.dbagent\appsettings.json
C:\Users\你的用户名\.dbagent\databases.json
```

然后你修改这两个文件，再运行：

```powershell
dotnet run --project .\src\DbAgent\DbAgent.csproj
```

之后 Visual Studio 直接 F5 也能启动，因为程序会自动去找：

```text
~/.dbagent/appsettings.json
```

你截图里的报错，就是因为现在还没有这个默认配置文件。

---

## 方式 2：在 Visual Studio 里先调试 `init`

在 VS 中：

1. 右键项目 `DbAgent`
2. 选择“属性”
3. 打开“调试”
4. 在“命令行参数”里填：

```text
init
```

5. F5 运行一次

生成默认配置后，把“命令行参数”清空，再 F5 启动 Agent。

---

## 方式 3：开发时指定自己的配置文件

例如你想把配置放在项目外的某个位置：

```powershell
dotnet run --project .\src\DbAgent\DbAgent.csproj -- --config "D:\dbagent-dev\appsettings.json"
```

在 Visual Studio 的“命令行参数”里则填：

```text
--config "D:\dbagent-dev\appsettings.json"
```

这种方式适合你准备多套环境配置，比如：

```text
dev.json
test.json
prod.json
```

---

# 我建议你的开发流程

```powershell
# 第一次
dotnet run --project .\src\DbAgent\DbAgent.csproj -- init

# 修改配置后
dotnet run --project .\src\DbAgent\DbAgent.csproj

# 指定模型启动
dotnet run --project .\src\DbAgent\DbAgent.csproj -- --model local-main

# 查看模型
dotnet run --project .\src\DbAgent\DbAgent.csproj -- models
```

## 配置查找顺序

程序现在会按这个顺序找配置：

```text
1. --config 指定的文件
2. 环境变量 DBAGENT_CONFIG
3. 当前目录 appsettings.json
4. C:\Users\你\.dbagent\appsettings.json
5. 程序目录 appsettings.json
```

所以开发模式最省事的是：
**先跑一次 `init`，之后就直接 F5。**

---

## Visual Studio 推荐配置

调试属性里可以这样设置：

### 第一次初始化

```text
命令行参数:
init
```

### 平时开发

```text
命令行参数:
留空
```

### 用特定模型调试

```text
命令行参数:
--model local-qwen
```

### 用特定配置文件调试

```text
命令行参数:
--config "D:\dbagent-dev\appsettings.json"
```

这样你不用安装全局工具，也能在开发阶段完整测试 `dbagent` 的行为。
