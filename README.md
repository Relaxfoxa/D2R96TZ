# D2R96TZ

面向 Windows 的轻量 D2R 大厅选房与追房命令行工具。状态读取只使用 `ReadProcessMemory`，操作只使用正常鼠标键盘输入；不写游戏内存，也不包含战斗、寻路或拾取逻辑。

## 当前状态

已在 D2R `3.3.93854` 上完成 Phase 1～4 实机验证：批量大厅表、UTF-8 中文房名、人数、已选房间、房间计时、玩家名单、智能推荐、单次自动加入、F8/F12 和自动跟随房主均通过。验证过程与地址记录见 `AUDIT.md`。

配置带有精确文件版本保护。D2R 更新后，`scan`/`selected` 会拒绝沿用旧 RVA；可在 Join Game 页面运行 `discover` 重新定位大厅表。大厅自动选房不再使用 `96` 或其他固定关键词，只使用 D2R 搜索框中的当前输入。

## 使用

用 Visual Studio 打开 `D2R96TZ.csproj`，目标为 .NET Framework 4.7.2、x64。让角色停留在 Join Game 页面后运行：

```text
D2R96TZ.exe audit
D2R96TZ.exe self-test config\reference-offsets.ini
D2R96TZ.exe scan config\reference-offsets.ini
D2R96TZ.exe scan config\reference-offsets.ini --debug-memory
D2R96TZ.exe discover config\reference-offsets.ini
D2R96TZ.exe benchmark config\reference-offsets.ini
D2R96TZ.exe probe-record-time config\reference-offsets.ini
D2R96TZ.exe recommend-dry-run config\reference-offsets.ini
D2R96TZ.exe game-state config\reference-offsets.ini
D2R96TZ.exe join-recommended config\reference-offsets.ini
D2R96TZ.exe follow-next-manual config\reference-offsets.ini
```

`scan` 一次读取大厅表并输出房名/人数；它不会读取或猜测每个 record 的房龄。手动在游戏中选择一个房间后运行：

```text
D2R96TZ.exe selected config\reference-offsets.ini
```

这会输出 `SelectedGameName`、人数、`SelectedGameTime` 和玩家列表。

`recommend-dry-run` 会输入运行时搜索词、刷新大厅、在重复缓存记录处截断列表，并按人数分组逐项读取必要候选的房龄。超过首屏的候选通过滚动条定位；每次都等待 `SelectedGameName` 精确匹配，错房时整轮熔断。该命令没有 Join 点击路径。

当前配置为：房名包含 D2R 搜索框当前输入、房龄不超过 600 秒（10 分钟）、未满 8 人；按人数降序、房龄升序推荐。`lobby_refresh_wait_ms` 和 `selected_info_wait_ms` 分别控制刷新渲染等待及选中详情超时。

`join-recommended` 会在 Join 前重新选中推荐房，并复核房名、人数、未满员和房龄，然后只单击一次 Join。当前 build 的游戏内房名由特征码定位 `gameData` 后从 `+0x20` 读取，用于确认真正进入了目标房。

`follow-next-manual` 在 Join Game 大厅启动时从当前搜索框关键词开始；搜索框为空时不会选房，输入关键词后按 F8 才会按该关键词筛选并加入。自动加入成功后立即监听 roster 的 PartyFlags，首个出现 `Accept(2)` 状态的玩家就是本轮真实邀请人；不再把大厅玩家列表或 roster 第一人当作邀请人，也不需要再次按 F8。监听已开启时 F8 被忽略。若角色进入游戏时监听器尚未运行，稍后启动监听器会利用“当前房名 + 有效 roster”确认确实仍在游戏，此时按一次 F8 开始监听后续邀请。若筛选出的房间在点击加入时已满或加入失败，程序会关闭失败提示、临时排除该房并立即重新筛选其他候选。被跟踪玩家连续消失确认后自动退出并追末尾数字 +1；进入下一房后自动重新等待该房的真实邀请人。程序只在第一次尝试时把搜索框改为完整目标房名，后续 15 秒重试只刷新结果，不重复复制。只有确认进入后才更新当前房。若下一房在窗口内未创建，程序会清空搜索框并等待手动输入新关键字；输入完成后再次按 F8，程序刷新当前列表，按人数最多、房龄最短选房并加入。F12 停止监听并清空当前房间、邀请人和跟踪状态；之后按 F8 会重新读取此刻所在房间并启动新的邀请人跟踪，若处于大厅则执行大厅选房。若 roster 定位失败，自动跟随关闭但大厅 F8 选房功能仍保留。

`--debug-memory` 只打印前 5 条 record 的地址、name raw bytes 和 players raw byte，用于区分基址、record stride、字段偏移问题；它不会尝试猜测新偏移。实机动态对照未发现大厅 record 内可可靠使用的房龄字段，因此房龄仍通过少量候选逐项选中读取。

手动选中大厅第一条房间后，可以用下面的命令定位选中房间块：

```text
D2R96TZ.exe discover-selected config\reference-offsets.ini
```

## 安全边界

进程句柄权限只有 `PROCESS_QUERY_INFORMATION | PROCESS_VM_READ`；源代码没有 D2R 写入路径。Phase 2～4 会产生普通窗口点击和按键。参考项目本身包含写内存声明和完整 Bot 逻辑，因此没有复制这些部分。
