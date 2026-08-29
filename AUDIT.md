# Phase 0 审计记录

审计对象：

- [D2R-BMBot](https://github.com/bouletmarc/D2R-BMBot)，审计提交 `4ec5d63213c5f668e2545c29e746e2da2461220e`，提交时间 2024-06-28。
- [D2R-multiclient-tools](https://github.com/Chobotz/D2R-multiclient-tools)，当前抓取提交 `1bbdd3c7cebbc6258aaf87af29252d00543e07a4`。

## 结论

1. BMBot 的大厅表读取可以复用“刷新后批量读取”的思路，但不能直接宣称兼容当前 D2R。`Strucs/PatternsScan.cs` 中 `AllGamesOffset`、`GameSelectedOffset` 和 `SelectedChar` 仍是硬编码值；通用 unit/UI/roster 部分才使用了 Pattern Scan。
2. `GameStruc.GetAllGamesNames()` 使用固定布局：大厅基址为 `BaseAddress + AllGamesOffset`，每条 record 为 `0x128` 字节，名字在 `+0x08`，人数在 `+0xF8`，最多读 40 条；原实现还依赖固定屏幕坐标和 60ms 等待。
3. `GetSelectedGameInfo()` 使用选中房布局：房龄 `+0xF0`，人数 `+0x108`，房名 `+0x08`，玩家名数组从 `+0x138` 开始、步长 `0x78`。被审计代码没有从每个大厅 record 读取 `GameTime` 的字段，因此当前 Phase 1 不会把房龄伪装成批量可读。
4. `BaalLeech`/`ChaosLeech` 是逐个候选 `SelectGame` 后读取 `SelectedGameTime`，按“第一个符合年龄的房”进入，不是人数/房龄排序，也没有加入前的完整事务确认。
5. `PlayerScan.ScanForLeecher()` 能按 roster 中的名字找到目标玩家和指针，但审计不到“第一个主动邀请者”的事件来源；不能直接把 `GameOwnerName` 或 roster 第一人当作邀请者。
6. `D2R-multiclient-tools/LobbyController/D2R_lobby_controller.au3` 提供了固定窗口、坐标、键盘输入、退出后 `+1` 的参考流程，但它假设 1280×720，且没有内存校验、目标房存在性校验或成功进入确认，不能直接用于本 Phase。

## 偏移/Pattern 判断

当前参考代码是“混合模式”：

- 动态：部分 unit table、UI、expansion、game data、menu、hover、roster 由字节模式搜索并解析 RIP-relative displacement。
- 固定：大厅表、选中房、SelectedChar 的当前活动实现是固定地址；旧的大厅/选中房 Pattern Scan 代码被注释掉。
- 内存读：`Mem.ReadProcessMemory`、`ReadRawMemory`、`ReadMemString`、`ReadInt32Raw`、`ReadByteRaw` 均存在，但原项目同时声明/实现了 `WriteProcessMemory`，所以本工具只重写最小只读读取器。

## 许可证注意

两个仓库根目录均未发现明确的 `LICENSE`/`COPYING` 文件（BMBot 仓库内的许可证文件来自若干依赖包）。本项目只记录参考来源并重写最小必要逻辑，没有复制大段源码；如未来公开发布，仍应分别向上游确认许可与 D2R 服务条款。

## 必须实机验证的项目

- 当前 D2R 版本与 `D2R.exe` 的文件版本/构建号。
- 三个固定偏移和 record 字段布局是否仍有效。
- 单次 `ReadProcessMemory` 批量读取能否稳定拿到全部可见房间。
- `SelectedGameTime` 单位是否为秒，以及与 UI 的刷新误差。
- 连续多轮 Refresh 的耗时和房间列表一致性。
- 当前版本是否在大厅 record 中新增了可验证的房龄字段；在没有内存证据前不采用猜测偏移。

## 2026-08-26 实机验证记录

当前运行实例：

- PID：`34400`
- 路径：`D:\Diablo II Resurrected\D2R.exe`
- 文件版本：`3.3.93854`
- 进程：x64（`IsWow64Process2`: native x64，process machine 0）
- BaseAddress：`0x7FF63FB70000`
- `OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_VM_READ)`：PASS
- 窗口检查：确认处于 Online Lobby → Join Game，搜索框为 `96`，画面上有可见房间列表。

使用 `config/reference-offsets.ini` 实际运行三轮：

```text
Scan #1: games_found=0, lobby_read_ms=1, valid=0, anomalous=0
Scan #2: games_found=0, lobby_read_ms=0, valid=0, anomalous=0
Scan #3: games_found=0, lobby_read_ms=0, valid=0, anomalous=0
```

`--debug-memory` 结果：参考 `AllGamesOffset` 解析地址为 `0x7FF642589F10`；record[0..4] 的 name raw bytes 全为 16 个 `00`，players raw byte 均为 `0x00`。因此不是 OCR、字符串编码或权限问题，而是参考结构在当前 build 上未定位到实际大厅数据。

手动未改变选中状态时运行 `selected`：`SelectedGameName=""`、`Players=0`、`GameTime=0`、玩家列表为空；这只能记为未验证，不能证明 `GameTime` 单位。

对当前 D2R.exe 整个加载模块做只读 signature 检查：

- BMBot 的旧 `AllGames`、`GameSelected`、`SelectedChar` signature：均未命中。
- BMBot 的 `unit`、`ui`、`expansion` signature：均未命中。
- `gameData`、`roster`、`menu`、`hover` signature：命中，但这不足以推出大厅表地址。
- 近期相关资料 [diablo2utils 的 offset 文档](https://github.com/ChrisTitusTech/diablo2utils/blob/master/docs/d2r-memory-offsets.md) 标注的是 D2R `3.1.91636`，不是当前 `3.3.93854`，因此只参考其动态 signature 方法，没有把其数值复制进本项目。

本轮结论：

- 大厅列表读取：FAIL
- 房间名：FAIL
- 人数：FAIL
- `SelectedGameName`：未验证
- `SelectedGamePlayerCount`：未验证
- `SelectedGameTime`：未验证
- 玩家名单：未验证
- 大厅 record 是否有 `GameTime`：INCONCLUSIVE
- 当前 Offset：尚未解决；旧值在当前 build 实测失效，未盲目更新。
- Phase 1：FAIL，停止在 Phase 1，不进入 Phase 2。

## 2026-08-27 偏移修复与回归验证

在同一 D2R 文件版本 `3.3.93854`、PID `39176` 上重新进入 Online Lobby → Join Game，搜索 `96`，以只读方式扫描 D2R 加载模块。结果：

- 当前大厅表 RVA：`0x32CF048`
- 当前已选房间块 RVA：`0x32DE000`
- 大厅 record 步长仍为 `0x128`
- 房名仍在 `+0x08`，但当前中文房名应按 UTF-8 读取，长度扩展为 32 字节
- 大厅人数仍在 `+0xF8`
- 已选房间计时仍在 `+0xF0`，连续采样从 366 秒增长到 368 秒
- 已选房间人数仍在 `+0x108`
- 玩家名单仍从 `+0x138` 开始、步长 `0x78`

现场结果：

```text
Lobby rooms: 40
valid_room_count=40
anomalous_record_count=0
lobby_read_ms=1..2

Game: 96碎片拼房我3开塔拉夏01
Players: 8
GameTime: 366 -> 368 sec
PlayersList: 8 names decoded successfully
```

根因确认：旧的固定 RVA 已失效；此外原验证器按 ASCII、16 字节读取房名，无法正确处理当前大厅中的 UTF-8 中文名称。两项均已修复。配置增加了 `supported_file_version=3.3.93854`，版本不匹配时正式读取命令会失败关闭，避免把偏移失效误报为“大厅为空”。新增 `discover` 和 `discover-selected` 只读定位命令，供后续补丁重新验证。

本轮结论：Phase 1 PASS。仍未发现可从每条大厅 record 直接批量读取房龄的可靠字段；批量房龄继续保持 unknown，只有手动选中房间后读取 `SelectedGameTime`。

## 2026-08-27 Phase 1 补充指标

100 次纯内存大厅读取与解析（每次 40 条，不含 UI Refresh）：

```text
min=0.019 ms
avg=0.026 ms
p95=0.030 ms
max=0.102 ms
```

选中 `96恐惧我刷塔墓003` 后进行动态对照：`SelectedGameTime` 从 995 增长到 1001，但对应大厅 record 的 `0x128` 字节没有任何变化，匹配递增值的 Int32 字段为 none。结合多轮间隔 10 秒/20 秒时计时分别增加 10/20，确认 `SelectedGameTime` 单位为秒；大厅 record 房龄结论为 NO（当前布局中没有可验证的动态房龄字段）。

## 2026-08-27 Phase 2 推荐 dry-run

实现内容：

- 正式匹配按用户确认改为房名包含 `96`，不再强制字面量 `96TZ`
- Refresh 后批量读取 40 条记录
- 只对当前可见前 14 行中的 Top-5 候选逐项读取选中详情
- 过滤满 8 人、房龄超过 300 秒的房间
- 合法候选按人数降序、房龄升序排序
- 每次读取都验证 `SelectedGameName == 目标房名`
- 任一候选发生 `selection_changed` 时整轮抑制推荐，要求重新 Refresh
- dry-run 没有 Join 点击路径

初版曾因连续点击同一个列表坐标被 D2R 解释为双击，意外进入一个房间。已删除该路径：现在每个候选只直接单击其自身可见行，列表点击间隔至少 600 ms，不再使用反复点击首行和 Home/Down；实机由用户手动退出后完成回归。

稳定参数：

```text
lobby_refresh_wait_ms=500
selected_info_wait_ms=3000
candidate_top_n=5
```

多轮实机结果：大厅每轮读取 40 条，Refresh+读取约 1.15～1.21 秒；3 个候选详情读取约 2.66～4.08 秒。最终回归三项均精确匹配：

```text
96恐惧我刷塔墓003        age=1473  REJECT(too_old)
96碎片你别来塔拉夏3       age=3130  REJECT(too_old)
96恐惧全开别来塔拉夏01    age=16803 REJECT(too_old)
Recommended: none
```

测试期间当前大厅没有房龄小于等于 300 秒的可见候选，因此正式配置返回 `Recommended: none`。临时放宽年龄上限时已验证正向推荐输出，随后恢复 300 秒。最终画面确认仍停留在 Join Game 大厅。Phase 2 dry-run：PASS；Phase 3 自动 Join 尚未开始。

## 2026-08-27 Phase 3 自动加入

根据用户最终确认，初始筛选只使用运行时关键词（本轮为 `96`），不再限定 `96TZ`。大厅内存表在有效记录尾部可能保留重复缓存；实现按上游逻辑在首个重复房名处截断，并使用上游 `27.3` 行距、滚轮回顶和滚动条定位处理超过首屏的索引。D2R 窗口客户区过窄、右侧大厅被裁切时，程序只在宽度不足 800 的情况下调整为当前显示器内的 16:9 可用窗口。

一次正向 dry-run：

```text
96碎片全开我塔墓02 players=4 age=57 sec
Recommended: 96碎片全开我塔墓02
```

实际 Phase 3 加入：

```text
target=96拼房 别来塔拉夏3
players=6
age=160 sec
```

Join 前房名、人数、未满员和房龄复核均通过，只执行一次 Join 点击。角色实际进入目标房。旧 BMBot 使用的 `gameData+0x40` 在当前 build 为空；只读 UTF-8 模块扫描确认真实游戏内房名位于 `gameData+0x20`（RVA `0x3290EA8`），修正后 `game-state` 精确返回 `96拼房 别来塔拉夏3`。再次运行加入命令返回 `already_in_game`，不会重复操作。Phase 3：PASS。

## 2026-08-27 Phase 4 手动追房

新增 `follow-next-manual`：

- F8：读取并保留当前房名，退出后只尝试末尾数字 +1；15 秒窗口内每秒重试，不存在时不跳号。
- F12：紧急停止监听。
- 只有游戏内房名确认等于目标后才事务式更新 `current_room`。
- 配置项：`next_room_wait_window_sec=15`、`next_room_retry_interval_ms=1000`。

实机启动能读取 `current_room=96拼房 别来塔拉夏3`；短按 F12 输出 `emergency_stop` 并以退出码 0 结束，当前游戏保持不变。F8 会退出当前房，按 Phase 4 设计保留为人工触发，尚未进行破坏性实机验收。

## 2026-08-28 Phase 4 自动跟随实机验证

启动监听后成功读取：

```text
tracked_owner=灵灵术士
current_room=杀死巴尔006
```

房主退出后，程序连续确认约 1 秒，自动执行：

```text
tracked_owner_left owner=灵灵术士; auto_follow
target_next_room=杀死巴尔007
leaving_game=杀死巴尔006
```

目标房 `杀死巴尔007` 未创建，程序按 15 秒窗口重复搜索，随后清空搜索框并进入 `manual_keyword_required` 状态。自动触发、完整房名 `+1`、退出动作和未找到后的人工关键字等待均通过。
