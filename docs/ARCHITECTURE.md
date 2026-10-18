# 代码结构与职责

## 总体结构

```text
非管理员 UI ──命令/心跳/状态── 控制 Worker（正常 UAC）
                               │
                    ownership ticket / 读写白名单
                               │
                         PawnIO 官方模块 ── EC / AMD 温度
                               │
                      独立 Guardian（同一授权令牌）
```

| 文件 | 职责 |
|---|---|
| `app/Program.cs` | UI / worker / guardian / 离线自测角色分发、单实例、正常 UAC |
| `app/MainForm.cs` | 连接、读回、应用事件、轮询、托盘 |
| `app/MainForm.Layout.cs` / `app/Presentation.cs` | 模式编辑区与布局 / 可离线验证的实时与运行状态描述 |
| `app/MainForm.Shutdown.cs` | 异步 QUIT、同会话恢复确认、正常退出 |
| `app/AppPaths.cs` / `app/RuntimeModules.cs` | 用户数据目录 / 单 EXE 内嵌模块释放与校验 |
| `app/CurveFormat.cs` | 曲线文本格式转换，不依赖 UI |
| `app/CurveEditor.cs` | 节点表增删/排序/校验、请求曲线预览 |
| `app/Configuration*.cs` | 配置持久化/备份及编辑状态；不等于硬件应用 |
| `app/ControlPolicy.cs` | 不触硬件的确定性控制决策与温度下限，主控/看护共享 |
| `app/Backend.cs` | 身份核验、命令执行、周期采样、独立恢复、传感器适配 |
| `app/Protocol.cs` | JSON DTO、原子文件交换、心跳租约、控制所有权 |
| `app/Autostart*.cs` | 当前用户登录启动项与 UI；自启只到托盘，不接管 |
| `app/AppTests.cs` | 纯模拟策略、UI、配置、心跳、自启开关的回归 |
| `app/SmokeTest.cs` | 明确调用才执行的真机诊断；CI 不调用 |
| `tools/StrictSuperIoProbe.cs` | 原生绑定、严格结果长度/状态检查、固定只读路径 |
| `tools/StrictIteFanControl.cs` | 独立 opt-in 部分，仅三处控制 RAM 写入白名单 |
| `scripts/` | 离线测试、官方依赖恢复、可复现打包 |
| `.github/workflows/build.yml` | Windows 构建/测试/独立 EXE + ZIP 验证/Artifact/标签发布 |

## 决策流程

0.9 的重构分析和回归范围见 [REFACTOR-0.9.md](REFACTOR-0.9.md)。运行时不要求源码目录结构；用户数据在 LocalAppData，模块在普通权限 UI 中校验后释放，提权进程只读这些模块。

当前温度 → 模式请求百分比 → 明确停转/最低输出 → 同一温度下限函数 → 有界启动助推 → 三地址白名单执行。
95°C、传感器/心跳错误、持续真实失速会进入恢复固件自动路径，不随正常曲线自动重新接管。
UI 读回状态确认后才显示已生效，不把编辑值当作已执行值。

## 硬件范围

精确匹配 TM2113 / 6800H / BIOS P1111 / EC P.11 / IT5571。固定 I2EC 索引读取两路固件计算 RPM，
大端字段 `0x0298/0299`、`0x02B8/02B9`。控制仅写 `0x0100` 的手动模式位、`0x0105/0106` 两路目标；
PWM 硬件寄存器不由主机直接写入。恢复时对最新 flags 做 RMW，只清本程序的模式位并清目标。
身份/固件/权限/PWM 配置不符拒绝控制，不修改权限位，不扫描未知写寄存器。

## 公共发布边界

不发布本机配置、运行会话、硬件日志、驱动安装器、BIOS/EC 固件或临时逆向工具。
模块从固定官方版本下载并核验，保留其许可证与源码；动态加载用户已安装的 PawnIOLib。
IPC 是同一当前用户的本地文件通道，不是远程接口，也不是隔离本地恶意管理员的安全边界。
