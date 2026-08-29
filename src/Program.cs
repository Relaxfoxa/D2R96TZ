using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace D2R96TZ
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                string command = args.Length == 0 ? "help" : args[0].ToLowerInvariant();
                if (command == "help") { PrintUsage(); return 0; }
                if (command == "audit") { PrintAuditStatus(); return 0; }
                string configPath = args.Length > 1 ? args[1] : Path.Combine("config", "reference-offsets.ini");
                var config = AppConfig.Load(configPath);
                if ((command == "recommend-dry-run" || command == "join-recommended" || command == "follow-next-manual") && args.Length > 2) config.OverrideSearchKeyword(args[2]);
                if (command == "self-test") { SelfTests.Run(config); return 0; }
                if (command != "scan" && command != "selected" && command != "discover" && command != "discover-selected" && command != "benchmark" && command != "probe-record-time" && command != "recommend-dry-run" && command != "game-state" && command != "join-recommended" && command != "find-text" && command != "roster-state" && command != "follow-next-manual") throw new ArgumentException("未知命令: " + command);
                if (command == "follow-next-manual") ConsoleWindow.Hide();

                using (var memory = new ProcessMemoryReader())
                {
                    memory.Open("D2R");
                    Console.WriteLine("[{0}] D2R process found: pid={1}", Timestamp(), memory.ProcessId);
                    Console.WriteLine("[{0}] exe={1}", Timestamp(), memory.FileVersion);
                    Console.WriteLine("[{0}] process_bitness={1}", Timestamp(), memory.Is64BitProcess ? "x64" : "not-confirmed-x64");
                    Console.WriteLine("[{0}] BaseAddress=0x{1:X}", Timestamp(), memory.ModuleBase.ToInt64());
                    Console.WriteLine("[{0}] ModuleSize=0x{1:X}", Timestamp(), memory.ModuleSize);
                    if (command != "discover" && command != "discover-selected" &&
                        config.SupportedFileVersion.Length > 0 &&
                        !string.Equals(config.SupportedFileVersion, memory.FileVersion, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException("D2R 版本不匹配：配置支持 " + config.SupportedFileVersion + "，当前为 " + memory.FileVersion + "。请先运行 discover 重新验证偏移。");
                    }
                    if (command == "discover" || command == "discover-selected")
                    {
                        var locator = new LobbyDiscovery(memory, config);
                        var discovery = locator.Find(config.SearchKeyword);
                        Console.WriteLine("prefix_hits={0}", discovery.PrefixHits);
                        if (discovery.Rooms.Count == 0) throw new InvalidOperationException("未找到符合旧记录布局的大厅表。当前版本可能改变了记录结构。");
                        Console.WriteLine("discovered_all_games_address=0x{0:X}", discovery.TableAddress);
                        Console.WriteLine("discovered_all_games_offset=0x{0:X}", discovery.TableOffset);
                        if (command == "discover-selected")
                        {
                            string selectedName = discovery.Rooms[0].Name;
                            Console.WriteLine("selected_name_probe={0}", selectedName);
                            foreach (long hit in locator.FindUtf8(selectedName))
                            {
                                long candidate = hit - config.SelectedGameNameOffset;
                                byte[] data;
                                if (!memory.TryRead(new IntPtr(candidate), 0x500, out data)) continue;
                                Console.WriteLine("name_hit=0x{0:X} candidate_base=0x{1:X} candidate_offset=0x{2:X} time@F0={3} players@108={4}",
                                    hit, candidate, candidate - memory.ModuleBase.ToInt64(), BitConverter.ToInt32(data, config.SelectedGameTimeOffset), data[config.SelectedGamePlayersOffset]);
                            }
                            return 0;
                        }
                        foreach (var room in discovery.Rooms) Console.WriteLine("{0,-32} players={1}", room.Name, room.Players);
                        return 0;
                    }
                    if (command == "find-text")
                    {
                        if (args.Length < 3) throw new ArgumentException("find-text 需要第三个参数作为 UTF-8 文本。");
                        foreach (long hit in new LobbyDiscovery(memory, config).FindUtf8(args[2]))
                            Console.WriteLine("text_hit=0x{0:X} rva=0x{1:X}", hit, hit - memory.ModuleBase.ToInt64());
                        return 0;
                    }
                    if (command == "follow-next-manual")
                    {
                        new Phase4ManualFollow(memory, config).Run();
                        return 0;
                    }
                    if (command == "roster-state")
                    {
                        foreach (RosterPlayer player in new RosterReader(memory).ReadPlayers())
                            Console.WriteLine("name={0} unit_id={1} party_id={2} party_flags={3}", player.Name, player.UnitId, player.PartyId, player.PartyFlags);
                        return 0;
                    }
                    var reader = new LobbyReader(memory, config);
                    if (command == "scan") PrintRooms(reader.ReadAllRooms(args.Contains("--debug-memory")), args.Contains("--debug-memory"));
                    else if (command == "selected") PrintSelected(reader.ReadSelectedGame());
                    else if (command == "benchmark") PrintBenchmark(new LobbyDiagnostics(memory, config).Benchmark(100));
                    else if (command == "probe-record-time") PrintRecordTimeProbe(new LobbyDiagnostics(memory, config).ProbeSelectedRecord(3000));
                    else if (command == "recommend-dry-run") PrintDryRun(new Phase2DryRun(memory, config).Run());
                    else if (command == "game-state") PrintGameState(new GameStateReader(memory));
                    else PrintJoinResult(new Phase3Join(memory, config).Run());
                }
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("ERROR: " + ex.Message);
                return 1;
            }
        }

        private static void PrintRooms(LobbyReadResult result, bool debug)
        {
            Console.WriteLine("Lobby rooms: {0}", result.Rooms.Count);
            foreach (var room in result.Rooms) Console.WriteLine("{0,-20} players={1} age=unknown", room.Name, room.Players);
            Console.WriteLine("lobby_read_ms={0}", result.ReadMs);
            Console.WriteLine("valid_room_count={0}", result.Rooms.Count - result.AnomalousRecordCount);
            Console.WriteLine("anomalous_record_count={0}", result.AnomalousRecordCount);
            if (debug)
            {
                Console.WriteLine("all_games_address=0x{0:X}", result.AllGamesAddress);
                Console.WriteLine("--- debug-memory (first 5 records) ---");
                foreach (var record in result.DebugRecords)
                {
                    Console.WriteLine("record[{0}]", record.Index);
                    Console.WriteLine("address=0x{0:X}", record.Address);
                    Console.WriteLine("name_raw={0}", record.NameRaw);
                    Console.WriteLine("name=\"{0}\"", record.Name);
                    Console.WriteLine("players_raw=0x{0:X2} ({1})", record.PlayersRaw, record.PlayersRaw);
                }
            }
            Console.WriteLine("Game age is intentionally unknown in batch mode; select a room manually and run 'selected'.");
            Console.WriteLine("No click, join, exit, or memory write was performed.");
        }

        private static void PrintSelected(SelectedGameInfo result)
        {
            Console.WriteLine("Game: " + result.Name);
            Console.WriteLine("Players: " + result.Players);
            Console.WriteLine("GameTime: " + result.GameTimeSec + " sec");
            Console.WriteLine("PlayersList:");
            foreach (var name in result.PlayersNames) Console.WriteLine("- " + name);
            Console.WriteLine("No click, join, exit, or memory write was performed.");
        }

        private static void PrintBenchmark(LobbyBenchmarkResult result)
        {
            Console.WriteLine("iterations={0} rooms={1}", result.Iterations, result.Rooms);
            Console.WriteLine("memory_read_parse_ms min={0:F3} avg={1:F3} p95={2:F3} max={3:F3}", result.MinMs, result.AverageMs, result.P95Ms, result.MaxMs);
            Console.WriteLine("UI Refresh time is excluded.");
        }

        private static void PrintRecordTimeProbe(RecordTimeProbeResult result)
        {
            Console.WriteLine("room={0} index={1}", result.RoomName, result.RoomIndex);
            Console.WriteLine("selected_time={0}->{1} delta={2}", result.SelectedTimeBefore, result.SelectedTimeAfter, result.SelectedTimeAfter - result.SelectedTimeBefore);
            Console.WriteLine("record_changed_bytes={0}", result.ChangedByteCount);
            Console.WriteLine("matching_int32_offsets={0}", result.CandidateOffsets.Count == 0 ? "none" : string.Join(",", result.CandidateOffsets.Select(offset => "0x" + offset.ToString("X"))));
            Console.WriteLine("No record field is accepted as game time without repeatable cross-room evidence.");
        }

        private static void PrintDryRun(Phase2DryRunResult result, bool dryRunOnly = true)
        {
            Console.WriteLine("[Lobby Scan] games_found={0}", result.RoomsFound);
            if (result.Inspections.Count == 0) Console.WriteLine("No room matched search_keyword; no recommendation.");
            foreach (CandidateInspection inspection in result.Inspections)
            {
                if (inspection.Current == null)
                    Console.WriteLine("{0} snapshot_players={1} lobby_index={2} REJECT({3})", inspection.Snapshot.Name, inspection.Snapshot.Players,
                        inspection.Snapshot.LobbyIndex, inspection.RejectReason);
                else
                    Console.WriteLine("{0} players={1} age={2} sec lobby_index={3}{4}{5}", inspection.Current.Name, inspection.Current.Players, inspection.Current.GameTimeSec,
                        inspection.Current.LobbyIndex,
                        inspection.SnapshotChanged ? " SNAPSHOT_CHANGED" : string.Empty,
                        inspection.RejectReason == null ? string.Empty : " REJECT(" + inspection.RejectReason + ")");
            }
            if (result.LobbyChanged) Console.WriteLine("lobby_changed=true; recommendation suppressed; refresh and retry.");
            if (result.Recommended == null)
            {
                Console.WriteLine("Recommended: none");
            }
            else
            {
                Console.WriteLine("Recommended: {0}", result.Recommended.Name);
                Console.WriteLine("Reason: keyword match, within configured age limit, players={0}, age={1}s", result.Recommended.Players, result.Recommended.GameTimeSec);
            }
            Console.WriteLine("refresh_and_read_ms={0}", result.RefreshAndReadMs);
            Console.WriteLine("selected_game_info_ms={0}", result.SelectedInfoMs);
            Console.WriteLine("candidate_sort_ms={0}", result.CandidateSortMs);
            if (dryRunOnly) Console.WriteLine("DRY-RUN ONLY: candidates were selected for read-only inspection; Join was never clicked.");
        }

        private static void PrintGameState(GameStateReader reader)
        {
            Console.WriteLine("game_data_pattern_address=0x{0:X}", reader.PatternAddress);
            Console.WriteLine("game_data_offset=0x{0:X}", reader.GameDataOffset);
            Console.WriteLine("game_name_address=0x{0:X}", reader.GameNameAddress);
            Console.WriteLine("current_game_name={0}", reader.ReadCurrentGameName());
        }

        private static void PrintJoinResult(Phase3JoinResult result)
        {
            if (result.DryRun != null) PrintDryRun(result.DryRun, false);
            Console.WriteLine("join_target={0}", result.TargetRoom ?? string.Empty);
            Console.WriteLine("join_status={0}", result.Status);
            Console.WriteLine("current_game_name={0}", result.CurrentGameName ?? string.Empty);
            Console.WriteLine("join_ms={0}", result.JoinMs);
            Console.WriteLine(result.Status == "success" ? "Join verified by current in-game name." : "Current room state was not advanced.");
        }

        private static void PrintAuditStatus()
        {
            Console.WriteLine("Phase 0/1 status");
            Console.WriteLine("- BMBot batch fields: name at +0x08, players at +0xF8, record size 0x128, max 40 records.");
            Console.WriteLine("- BMBot selected fields: time +0xF0, players +0x108, names +0x138 with 0x78 stride.");
            Console.WriteLine("- Batch room age was not found in the audited code; it remains a selected-room read.");
            Console.WriteLine("- D2R 3.3.93854 offsets are live-validated and protected by exact file version.");
            Console.WriteLine("- Phase 2 dry-run uses the configured/runtime search keyword; it never clicks Join.");
            Console.WriteLine("- Phase 3 uses one Join-button click and verifies the current in-game name before reporting success.");
            Console.WriteLine("- State inspection only uses PROCESS_VM_READ; Phase 2-4 actions use normal mouse/keyboard input.");
        }

        private static void PrintUsage()
        {
            Console.WriteLine("D2R96TZ Phase 1-4 lobby verifier, recommender, joiner, and manual next-room follower");
            Console.WriteLine("  D2R96TZ.exe audit");
            Console.WriteLine("  D2R96TZ.exe self-test [config path]");
            Console.WriteLine("  D2R96TZ.exe scan [config path]      # batch names + player counts");
            Console.WriteLine("  D2R96TZ.exe scan [config path] --debug-memory");
            Console.WriteLine("  D2R96TZ.exe selected [config path]  # manually selected room details");
            Console.WriteLine("  D2R96TZ.exe discover [config path]  # find the current lobby table read-only");
            Console.WriteLine("  D2R96TZ.exe discover-selected [config path]  # locate the manually selected room");
            Console.WriteLine("  D2R96TZ.exe benchmark [config path]  # 100 read+parse iterations, no UI refresh");
            Console.WriteLine("  D2R96TZ.exe probe-record-time [config path]  # compare selected time against its lobby record");
            Console.WriteLine("  D2R96TZ.exe recommend-dry-run [config path] [keyword]  # refresh, rank matching rooms; never Join");
            Console.WriteLine("  D2R96TZ.exe game-state [config path]  # pattern-locate current in-game name");
            Console.WriteLine("  D2R96TZ.exe find-text [config path] [text]  # diagnostic UTF-8 module scan");
            Console.WriteLine("  D2R96TZ.exe roster-state [config path]  # read player party id/flags");
            Console.WriteLine("  D2R96TZ.exe join-recommended [config path] [keyword]  # recommend, revalidate, click Join once, verify");
            Console.WriteLine("  D2R96TZ.exe follow-next-manual [config path] [keyword]  # F8 start/resume; F12 pause actions");
        }

        private static string Timestamp() { return DateTime.Now.ToString("HH:mm:ss.fff"); }

        private static class ConsoleWindow
        {
            private const int SwHide = 0;

            [DllImport("kernel32.dll")]
            private static extern IntPtr GetConsoleWindow();

            [DllImport("user32.dll")]
            private static extern bool ShowWindow(IntPtr window, int command);

            public static void Hide()
            {
                IntPtr window = GetConsoleWindow();
                if (window != IntPtr.Zero) ShowWindow(window, SwHide);
            }
        }
    }
}
