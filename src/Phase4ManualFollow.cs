using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace D2R96TZ
{
    public sealed class Phase4ManualFollow
    {
        private const int VkF8 = 0x77;
        private const int VkF12 = 0x7B;
        private const uint PartyFlagAcceptInvite = 2;

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int virtualKey);

        private readonly ProcessMemoryReader memory;
        private readonly AppConfig config;
        private readonly LobbyReader reader;
        private readonly LobbyUiController ui;
        private readonly GameStateReader gameState;
        private RosterReader roster;
        private string currentRoom;
        private string trackedOwner;
        private DateTime ownerMissingSinceUtc;
        private bool waitingForManualKeyword;
        private bool trackingEnabled;
        private bool paused;
        private bool stoppedStateCaptured;
        private string gameNameAtStop;
        private readonly StatusWindow status = new StatusWindow();

        public Phase4ManualFollow(ProcessMemoryReader memory, AppConfig config)
        {
            this.memory = memory;
            this.config = config;
            reader = new LobbyReader(memory, config);
            ui = new LobbyUiController(memory, config);
            gameState = new GameStateReader(memory);
            // D2R keeps the previous room name in this buffer after leaving,
            // so adopt it only when a live in-game roster also exists.
            currentRoom = string.Empty;
            try
            {
                roster = new RosterReader(memory);
                TryAdoptCurrentGame();
            }
            catch (Exception ex)
            {
                Log("auto_follow_unavailable reason={0}; F8_manual_only", ex.Message);
            }
        }

        public void Run()
        {
            status.Start();
            UpdateStatus(currentRoom.Length == 0 ? "等待 F8" : "检测到已在游戏；按 F8 监听邀请人");
            Log("phase4_ready current_room={0} mode={1}", currentRoom.Length == 0 ? "none" : currentRoom, currentRoom.Length == 0 ? "lobby_keyword_scan" : "existing_game_wait_F8");
            Log("F8=大厅选房/重新检测当前游戏并启动监听; 自动入房后无需F8; 跟踪已开启时F8忽略; F12=停止并清空监听状态");
            bool f8WasDown = false;
            bool f12WasDown = false;
            try
            {
                while (true)
                {
                    bool f12Down = IsKeyDown(VkF12);
                    if (f12Down && !f12WasDown) StopMonitoring();
                    f12WasDown = f12Down;

                    bool f8Down = IsKeyDown(VkF8);
                    if (paused)
                    {
                        if (f8Down && !f8WasDown)
                        {
                            RestartMonitoring();
                            HandleF8();
                        }
                        f8WasDown = f8Down;
                        Thread.Sleep(50);
                        continue;
                    }

                    try
                    {
                        CheckTrackedOwner();
                        if (f8Down && !f8WasDown)
                        {
                            HandleF8();
                        }
                        f8WasDown = f8Down;
                    }
                    catch (Exception ex)
                    {
                        f8WasDown = IsKeyDown(VkF8);
                        Log("listener_action_error reason={0}; listener_kept_alive", ex.Message);
                        UpdateStatus("操作失败，监听仍运行；可重试或按 F12 暂停");
                        Thread.Sleep(300);
                    }
                    Thread.Sleep(50);
                }
            }
            finally { status.Dispose(); }
        }

        private void HandleF8()
        {
            if (currentRoom.Length > 0 && trackingEnabled)
            {
                Log("F8_ignored reason=tracking_already_active owner={0}", string.IsNullOrEmpty(trackedOwner) ? "waiting_inviter" : trackedOwner);
                return;
            }

            UpdateStatus("F8 已触发");
            if (!waitingForManualKeyword && currentRoom.Length == 0 && TryAdoptCurrentGame())
            {
                EnableTracking();
                return;
            }
            if (waitingForManualKeyword || currentRoom.Length == 0) JoinManualKeywordSelection();
            else EnableTracking();
        }

        private void StopMonitoring()
        {
            if (paused) return;
            try { gameNameAtStop = gameState.ReadCurrentGameName(); }
            catch (Exception) { gameNameAtStop = string.Empty; }
            stoppedStateCaptured = true;
            paused = true;
            currentRoom = string.Empty;
            trackedOwner = null;
            trackingEnabled = false;
            waitingForManualKeyword = false;
            ownerMissingSinceUtc = DateTime.MinValue;
            Log("monitoring_stopped tracking_state_cleared game_name_at_stop={0}; F8_to_restart", string.IsNullOrEmpty(gameNameAtStop) ? "none" : gameNameAtStop);
            UpdateStatus("已停止并清空；按 F8 重新检测当前状态");
        }

        private void RestartMonitoring()
        {
            paused = false;
            ownerMissingSinceUtc = DateTime.MinValue;
            Log("monitoring_restarted");
            UpdateStatus("监听已重新启动，正在检测当前状态");
        }

        private void FollowNextRoom()
        {
            if (currentRoom.Length == 0)
            {
                Log("F8_ignored reason=current_room_unknown");
                return;
            }

            string targetRoom = RoomSelector.NextRoomName(currentRoom);
            if (targetRoom == null)
            {
                Log("F8_ignored reason=no_trailing_number current_room={0}", currentRoom);
                return;
            }

            Log("target_next_room={0}", targetRoom);
            Log("leaving_game={0}", currentRoom);
            UpdateStatus("退出当前房，准备追 " + targetRoom);
            ui.LeaveGame();
            // The D2R game-name buffer remains populated after leaving. The
            // previous room is already stored, so go straight to the lobby UI.
            Thread.Sleep(1200);

            TryJoinTarget(targetRoom);
        }

        private void EnableTracking()
        {
            if (trackingEnabled)
            {
                Log("tracking_already_enabled owner={0}", string.IsNullOrEmpty(trackedOwner) ? "waiting_inviter" : trackedOwner);
                return;
            }

            BeginInvitationTracking("F8");
            CheckTrackedOwner();
        }

        private bool TryAdoptCurrentGame()
        {
            string detectedRoom = gameState.ReadCurrentGameName();
            if (string.IsNullOrWhiteSpace(detectedRoom)) return false;
            bool hasRoster = roster != null && roster.ReadPlayers().Count > 0;
            if (!CanAdoptDetectedGame(hasRoster, stoppedStateCaptured, detectedRoom, gameNameAtStop)) return false;
            currentRoom = detectedRoom;
            waitingForManualKeyword = false;
            stoppedStateCaptured = false;
            gameNameAtStop = null;
            Log("existing_game_detected current_room={0}", currentRoom);
            return true;
        }

        internal static bool CanAdoptDetectedGame(bool hasRoster, bool hasStoppedState, string detectedRoom, string gameNameAtStop)
        {
            if (string.IsNullOrWhiteSpace(detectedRoom)) return false;
            if (hasRoster) return true;
            return hasStoppedState && !string.Equals(detectedRoom, gameNameAtStop, StringComparison.Ordinal);
        }

        private void BeginInvitationTracking(string source)
        {
            trackedOwner = null;
            trackingEnabled = true;
            ownerMissingSinceUtc = DateTime.MinValue;
            Log("invitation_tracking_started source={0}", source);
            UpdateStatus("正在等待真实邀请人");
        }

        private void TryJoinTarget(string targetRoom)
        {
            var waitWindow = Stopwatch.StartNew();
            bool searchInitialized = false;
            while (waitWindow.Elapsed.TotalSeconds < config.NextRoomWaitWindowSec)
            {
                if (PauseRequested()) return;
                if (!searchInitialized)
                {
                    // Enter the exact target once. All retries keep this
                    // search text intact and only refresh the result list.
                    ui.SearchAndRefresh(targetRoom);
                    searchInitialized = true;
                    Log("next_room_search_set target={0}", targetRoom);
                    UpdateStatus("已设置搜索：" + targetRoom);
                }
                else ui.RefreshCurrentSearch();
                LobbyReadResult lobby = reader.ReadAllRooms(false);
                List<RoomInfo> visibleRooms = Phase2DryRun.BuildVisibleRooms(lobby.Rooms, targetRoom);
                RoomInfo target = visibleRooms.FirstOrDefault(room => string.Equals(room.Name, targetRoom, StringComparison.Ordinal));
                if (target == null)
                {
                    Log("next_room_not_found target={0}", targetRoom);
                    UpdateStatus("刷新等待：未找到 " + targetRoom);
                    WaitRetryInterval();
                    continue;
                }

                ui.SelectLobbyIndex(target.VisibleIndex, visibleRooms.Count);
                SelectedGameInfo selected = WaitForSelectedGame(targetRoom);
                if (!string.Equals(selected.Name, targetRoom, StringComparison.Ordinal))
                {
                    Log("next_room_selection_changed target={0}", targetRoom);
                    WaitRetryInterval();
                    continue;
                }
                if (selected.Players >= 8)
                {
                    Log("next_room_full target={0}", targetRoom);
                    WaitRetryInterval();
                    continue;
                }
                if (selected.GameTimeSec < 0 || selected.GameTimeSec > config.MaxGameAgeSec)
                {
                    Log("next_room_stale target={0} age={1}", targetRoom, selected.GameTimeSec);
                    WaitRetryInterval();
                    continue;
                }

                Log("next_room_found target={0} players={1} age={2}", targetRoom, selected.Players, selected.GameTimeSec);
                UpdateStatus("找到目标，正在加入");
                string gameNameBeforeJoin = gameState.ReadCurrentGameName();
                ui.ClickJoin();
                if (WaitForJoinedRoom(targetRoom, gameNameBeforeJoin))
                {
                    currentRoom = targetRoom;
                    Log("join_success current_room={0}", currentRoom);
                    BeginInvitationTracking("next_room_auto_join");
                    return;
                }
                Log("join_failed target={0}", targetRoom);
                UpdateStatus("加入失败，继续等待目标房");
                ui.DismissJoinFailure();
                WaitRetryInterval();
            }
            Log("next_room_timeout target={0}; current_room_unchanged={1}", targetRoom, currentRoom);
            ui.ClearSearchBox();
            waitingForManualKeyword = true;
            UpdateStatus("追房超时：已清空搜索框；输入新关键词后按 F8");
            Log("manual_keyword_required type_keyword_then_press_F8");
        }

        private void JoinManualKeywordSelection()
        {
            string currentKeyword = ui.ReadSearchKeyword();
            if (string.IsNullOrWhiteSpace(currentKeyword))
            {
                TryJoinFirstVisibleResult();
                return;
            }
            currentKeyword = currentKeyword.Trim();
            var failedRooms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (!paused)
            {
                Log("manual_keyword_scan_start excluded_failed={0}", failedRooms.Count);
                UpdateStatus(failedRooms.Count == 0 ? "刷新当前关键词并筛选" : "加入失败，重新筛选其他房间");
                Phase2DryRunResult dryRun = null;
                for (int attempt = 1; attempt <= 3; attempt++)
                {
                    dryRun = new Phase2DryRun(memory, config).RunFilter(currentKeyword, failedRooms);
                    if (!dryRun.LobbyChanged || attempt == 3) break;
                    Log("manual_keyword_retry reason=selection_changed attempt={0}", attempt + 1);
                    Thread.Sleep(200);
                }
                Log("manual_keyword_scan_result keyword={0} rooms={1} inspections={2} lobby_changed={3} recommended={4}",
                    string.IsNullOrEmpty(dryRun.FilterKeyword) ? "<empty>" : dryRun.FilterKeyword,
                    dryRun.RoomsFound, dryRun.Inspections.Count, dryRun.LobbyChanged,
                    dryRun.Recommended == null ? "none" : dryRun.Recommended.Name);
                if (dryRun.Recommended == null)
                {
                    foreach (CandidateInspection inspection in dryRun.Inspections.Where(item => item.RejectReason != null).Take(5))
                        Log("manual_keyword_reject target={0} reason={1}", inspection.Snapshot.Name, inspection.RejectReason);
                    Log("manual_keyword_no_recommendation excluded_failed={0}; type_new_keyword_then_press_F8", failedRooms.Count);
                    UpdateStatus(DescribeNoRecommendation(dryRun, failedRooms.Count));
                    return;
                }

                RoomInfo recommended = dryRun.Recommended;
                ui.SelectLobbyIndex(recommended.VisibleIndex, dryRun.VisibleRoomsFound);
                SelectedGameInfo selected = WaitForSelectedGame(recommended.Name);
                if (paused) return;
                if (!string.Equals(selected.Name, recommended.Name, StringComparison.Ordinal))
                {
                    Log("manual_keyword_selection_changed target={0}; rescanning", recommended.Name);
                    Thread.Sleep(200);
                    continue;
                }
                if (selected.Players >= 8 || selected.GameTimeSec < 0 || selected.GameTimeSec > config.MaxGameAgeSec)
                {
                    failedRooms.Add(recommended.Name);
                    Log("manual_keyword_candidate_invalid target={0} players={1} age={2}; rescanning", recommended.Name, selected.Players, selected.GameTimeSec);
                    continue;
                }

                Log("manual_keyword_join target={0} players={1} age={2}", selected.Name, selected.Players, selected.GameTimeSec);
                UpdateStatus("正在加入：" + selected.Name);
                string gameNameBeforeJoin = gameState.ReadCurrentGameName();
                ui.ClickJoin();
                if (!WaitForJoinedRoom(recommended.Name, gameNameBeforeJoin))
                {
                    if (paused) return;
                    failedRooms.Add(recommended.Name);
                    Log("manual_keyword_join_failed target={0}; rescanning", recommended.Name);
                    UpdateStatus("加入失败，重新筛选其他房间");
                    ui.DismissJoinFailure();
                    Thread.Sleep(300);
                    continue;
                }

                currentRoom = recommended.Name;
                waitingForManualKeyword = false;
                BeginInvitationTracking("lobby_auto_join");
                Log("join_success current_room={0}; invitation_tracking=active", currentRoom);
                return;
            }
        }

        private void TryJoinFirstVisibleResult()
        {
            Log("manual_keyword_unreadable fallback=first_visible_result");
            UpdateStatus("无法读取搜索词，复核第一个可见结果");
            ui.RefreshCurrentSearch();
            ui.SelectLobbyIndex(0, 1);
            Thread.Sleep(config.LobbyRefreshWaitMs);

            SelectedGameInfo selected = reader.ReadSelectedGame();
            LobbyReadResult lobby = reader.ReadAllRooms(false);
            string rejectReason = ValidateFirstVisibleSelection(selected, lobby.Rooms, config);
            if (rejectReason != null)
            {
                Log("first_visible_reject target={0} reason={1}", selected.Name, rejectReason);
                UpdateStatus("首个结果不可加入：" + rejectReason);
                return;
            }

            Log("first_visible_join target={0} players={1} age={2}", selected.Name, selected.Players, selected.GameTimeSec);
            UpdateStatus("正在加入：" + selected.Name);
            string gameNameBeforeJoin = gameState.ReadCurrentGameName();
            ui.ClickJoin();
            if (!WaitForJoinedRoom(selected.Name, gameNameBeforeJoin))
            {
                if (paused) return;
                Log("first_visible_join_failed target={0}", selected.Name);
                UpdateStatus("加入失败：" + selected.Name);
                ui.DismissJoinFailure();
                return;
            }

            currentRoom = selected.Name;
            waitingForManualKeyword = false;
            BeginInvitationTracking("first_visible_auto_join");
            Log("join_success current_room={0}; invitation_tracking=active", currentRoom);
        }

        internal static string ValidateFirstVisibleSelection(SelectedGameInfo selected, IEnumerable<RoomInfo> lobbyRooms, AppConfig config)
        {
            if (selected == null || string.IsNullOrWhiteSpace(selected.Name)) return "未选中房间";
            if (!lobbyRooms.Any(room => string.Equals(room.Name, selected.Name, StringComparison.Ordinal))) return "选中详情不在当前大厅";
            if (selected.Name.IndexOf(config.SearchKeyword, StringComparison.OrdinalIgnoreCase) < 0) return "房名不含 " + config.SearchKeyword;
            if (selected.Players >= 8) return "房间已满";
            if (selected.GameTimeSec < 0) return "房龄无效";
            if (selected.GameTimeSec > config.MaxGameAgeSec) return "房龄超限";
            return null;
        }

        private static string DescribeNoRecommendation(Phase2DryRunResult dryRun, int excludedFailedCount)
        {
            if (dryRun.LobbyChanged) return "列表变化导致房名复核失败；请再按 F8";
            if (dryRun.VisibleRoomsFound == 0) return "没有匹配房：" + dryRun.FilterKeyword;
            CandidateInspection rejected = dryRun.Inspections.FirstOrDefault(item => item.RejectReason != null);
            if (rejected != null)
            {
                if (rejected.RejectReason == "full") return "候选房已满：" + rejected.Snapshot.Name;
                if (rejected.RejectReason == "too_old") return "候选房龄超限：" + rejected.Snapshot.Name;
                if (rejected.RejectReason == "invalid_age") return "无法读取候选房龄：" + rejected.Snapshot.Name;
                return "候选被拒绝：" + rejected.RejectReason;
            }
            return excludedFailedCount == 0 ? "匹配房均已满或不可用" : "重新筛选后暂无其他可加入房";
        }

        private void CheckTrackedOwner()
        {
            if (roster == null || !trackingEnabled || waitingForManualKeyword || currentRoom.Length == 0) return;
            List<RosterPlayer> players = roster.ReadPlayers();
            if (players.Count == 0) return;

            if (string.IsNullOrEmpty(trackedOwner))
            {
                RosterPlayer inviter = players.FirstOrDefault(player => player.PartyFlags == PartyFlagAcceptInvite);
                if (inviter == null) return;
                trackedOwner = inviter.Name;
                ownerMissingSinceUtc = DateTime.MinValue;
                Log("tracked_owner={0} source=party_flag_accept unit_id={1} party_id={2}", trackedOwner, inviter.UnitId, inviter.PartyId);
                UpdateStatus("已识别邀请人，持续跟踪");
                return;
            }

            if (players.Any(player => string.Equals(player.Name, trackedOwner, StringComparison.OrdinalIgnoreCase)))
            {
                ownerMissingSinceUtc = DateTime.MinValue;
                return;
            }

            if (ownerMissingSinceUtc == DateTime.MinValue)
            {
                ownerMissingSinceUtc = DateTime.UtcNow;
                Log("tracked_owner_missing owner={0}; confirming", trackedOwner);
                UpdateStatus("跟踪玩家暂时消失，确认中");
                return;
            }
            if ((DateTime.UtcNow - ownerMissingSinceUtc).TotalMilliseconds < 1000) return;

            Log("tracked_owner_left owner={0}; auto_follow", trackedOwner);
            UpdateStatus("跟踪玩家已离开，自动追下一房");
            ownerMissingSinceUtc = DateTime.MinValue;
            FollowNextRoom();
        }

        private void UpdateStatus(string action)
        {
            string mode = paused ? "已停止" : waitingForManualKeyword ? "等待手动关键词" :
                (currentRoom.Length == 0 ? "大厅关键词扫描" : (trackingEnabled ? "邀请/离房监听" : "已有游戏，待按 F8"));
            string trackingStatus;
            if (paused) trackingStatus = "跟踪信息已清空；按 F8 重新检测当前状态";
            else if (!trackingEnabled) trackingStatus = currentRoom.Length == 0 ? "等待进入房间" : "按 F8 开始监听邀请人";
            else if (string.IsNullOrEmpty(trackedOwner)) trackingStatus = "等待真实邀请人发出邀请";
            else if (action == "跟踪玩家暂时消失，确认中") trackingStatus = "确认 " + trackedOwner + " 已离开";
            else if (action == "跟踪玩家已离开，自动追下一房") trackingStatus = "已离开，自动追下一房";
            else if (currentRoom.Length == 0) trackingStatus = waitingForManualKeyword ? "等待输入关键词后按 F8" : "等待加入房间";
            else trackingStatus = "跟踪中：" + trackedOwner + "；离开后自动追下一房";
            status.Update(mode, currentRoom, trackedOwner, trackingStatus, action);
        }

        private SelectedGameInfo WaitForSelectedGame(string expectedName)
        {
            var stopwatch = Stopwatch.StartNew();
            SelectedGameInfo selected;
            do
            {
                selected = reader.ReadSelectedGame();
                if (string.Equals(selected.Name, expectedName, StringComparison.Ordinal)) return selected;
                Thread.Sleep(50);
            }
            while (stopwatch.ElapsedMilliseconds < config.SelectedInfoWaitMs && !PauseRequested());
            return selected;
        }

        private bool WaitForJoinedRoom(string expectedName, string gameNameBeforeJoin)
        {
            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.ElapsedMilliseconds < config.JoinTimeoutMs)
            {
                if (PauseRequested()) return false;
                Thread.Sleep(200);
                string current = gameState.ReadCurrentGameName();
                if (string.Equals(current, expectedName, StringComparison.Ordinal)) return true;
                if (IsUnexpectedJoinedRoom(current, expectedName, gameNameBeforeJoin)) return false;
            }
            return false;
        }

        internal static bool IsUnexpectedJoinedRoom(string currentName, string expectedName, string gameNameBeforeJoin)
        {
            if (string.IsNullOrEmpty(currentName)) return false;
            if (string.Equals(currentName, expectedName, StringComparison.Ordinal)) return false;
            return !string.Equals(currentName, gameNameBeforeJoin, StringComparison.Ordinal);
        }

        private void WaitRetryInterval()
        {
            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.ElapsedMilliseconds < config.NextRoomRetryIntervalMs && !PauseRequested()) Thread.Sleep(50);
        }

        private bool PauseRequested()
        {
            if (!IsKeyDown(VkF12)) return paused;
            StopMonitoring();
            return true;
        }

        private static bool IsKeyDown(int virtualKey)
        {
            short state = GetAsyncKeyState(virtualKey);
            return (state & 0x8000) != 0 || (state & 1) != 0;
        }

        private static void Log(string format, params object[] values)
        {
            Console.WriteLine("[{0}] {1}", DateTime.Now.ToString("HH:mm:ss.fff"), string.Format(format, values));
        }
    }
}
