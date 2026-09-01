using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Linq;

namespace D2R96TZ
{
    public static class SelfTests
    {
        public static void Run(AppConfig config)
        {
            var rooms = new List<RoomInfo>
            {
                new RoomInfo { Name = "96TZ021", Players = 7, GameTimeSec = 42 },
                new RoomInfo { Name = "96TZ022", Players = 6, GameTimeSec = 15 },
                new RoomInfo { Name = "96TZ023", Players = 7, GameTimeSec = 110 },
                new RoomInfo { Name = "96ABC01", Players = 8, GameTimeSec = 30 },
                new RoomInfo { Name = "96TZ020", Players = 8, GameTimeSec = 600 },
                new RoomInfo { Name = "96TZ099", Players = 8, GameTimeSec = 30 },
                new RoomInfo { LobbyIndex = 14, Name = "96LATE", Players = 7, GameTimeSec = null },
                new RoomInfo { LobbyIndex = 25, Name = "96LATE", Players = 7, GameTimeSec = null }
            };
            var ranked = RoomSelector.Rank(rooms, config);
            Assert(ranked.Count == 3, "selector filters search keyword, full rooms, and age");
            Assert(ranked[0].Name == "96TZ021", "selector sorts players then age");
            Assert(Phase2DryRun.BuildInspectionOrder(rooms, config).Count == 4, "inspection order covers all matching non-full rooms");
            Assert(Phase2DryRun.BuildInspectionOrder(rooms, config).Any(room => room.LobbyIndex == 14), "inspection order includes rows below the visible page");
            Assert(Phase2DryRun.BuildInspectionOrder(rooms, config).Count(room => room.Name == "96LATE") == 1, "inspection order removes cached duplicate names");
            var filteredRows = Phase2DryRun.BuildVisibleRooms(new[]
            {
                new RoomInfo { LobbyIndex = 0, Name = "OTHER", Players = 1 },
                new RoomInfo { LobbyIndex = 1, Name = "96FIRST", Players = 1 },
                new RoomInfo { LobbyIndex = 2, Name = "OTHER2", Players = 1 },
                new RoomInfo { LobbyIndex = 3, Name = "96FULL", Players = 8 },
                new RoomInfo { LobbyIndex = 4, Name = "96SECOND", Players = 1 }
            }, "96");
            Assert(filteredRows.Count == 3, "visible rows include every matching room");
            Assert(filteredRows[0].LobbyIndex == 1 && filteredRows[0].VisibleIndex == 0, "first filtered row maps raw index to visible index");
            Assert(filteredRows[2].LobbyIndex == 4 && filteredRows[2].VisibleIndex == 2, "filtered row mapping counts full matching rooms");
            var failedRooms = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "96TZ021" };
            Assert(!Phase2DryRun.BuildInspectionOrder(rooms, config, failedRooms).Any(room => room.Name == "96TZ021"), "failed room is excluded from immediate rescan");
            var staleInspections = new List<CandidateInspection>
            {
                new CandidateInspection { Current = rooms[0] },
                new CandidateInspection { Snapshot = rooms[1], RejectReason = "selection_changed" }
            };
            Assert(Phase2DryRun.SelectRecommendation(staleInspections, config) == null, "stale lobby suppresses the whole recommendation");
            staleInspections[1].RejectReason = "room_disappeared";
            Assert(Phase2DryRun.SelectRecommendation(staleInspections, config) == rooms[0], "a disappeared candidate does not suppress valid rooms");
            Assert(RoomSelector.NextRoomName("96TZ001") == "96TZ002", "three-digit increment");
            Assert(RoomSelector.NextRoomName("96TZ09") == "96TZ10", "two-digit increment");
            Assert(RoomSelector.NextRoomName("96TZ099") == "96TZ100", "carry increment");
            Assert(RoomSelector.NextRoomName("96TZ024") == "96TZ025", "only trailing number changes");
            Assert(RoomSelector.NextRoomName("96恐惧全开刷编年我塔墓-02") == "96恐惧全开刷编年我塔墓-03", "UTF-8 room suffix increment");
            Assert(RoomSelector.NextRoomName("97TZ001") == "97TZ002", "prefix is preserved");
            Assert(RoomSelector.NextRoomName("96TZ") == null, "room without trailing number is rejected");
            Assert(!Phase4ManualFollow.IsUnexpectedJoinedRoom("旧房001", "目标002", "旧房001"), "stale previous room name keeps waiting for join");
            Assert(!Phase4ManualFollow.IsUnexpectedJoinedRoom(string.Empty, "目标002", "旧房001"), "empty game name keeps waiting for join");
            Assert(Phase4ManualFollow.IsUnexpectedJoinedRoom("错误003", "目标002", "旧房001"), "a different new room is rejected");
            var visibleSelected = new SelectedGameInfo { Name = "96随意0002", Players = 2, GameTimeSec = 81 };
            var visibleLobby = new[] { new RoomInfo { Name = "96随意0002", Players = 1 } };
            Assert(Phase4ManualFollow.ValidateFirstVisibleSelection(visibleSelected, visibleLobby, config) == null, "first visible matching room is eligible");
            Assert(Phase4ManualFollow.ValidateFirstVisibleSelection(new SelectedGameInfo { Name = "其他房", Players = 2, GameTimeSec = 81 }, visibleLobby, config) == "选中详情不在当前大厅", "stale first visible selection is rejected");
            Assert(Phase4ManualFollow.ValidateFirstVisibleSelection(new SelectedGameInfo { Name = "96随意0002", Players = 8, GameTimeSec = 81 }, visibleLobby, config) == "房间已满", "full first visible room is rejected");
            Assert(Phase4ManualFollow.ValidateFirstVisibleSelection(new SelectedGameInfo { Name = "96随意0002", Players = 2, GameTimeSec = config.MaxGameAgeSec + 1 }, visibleLobby, config) == "房龄超限", "old first visible room is rejected");
            Assert(!Phase4ManualFollow.CanAdoptDetectedGame(false, false, "旧房001", null), "stale game name without roster is not adopted at initial startup");
            Assert(!Phase4ManualFollow.CanAdoptDetectedGame(false, true, "旧房001", "旧房001"), "unchanged stopped game name without roster is not adopted");
            Assert(Phase4ManualFollow.CanAdoptDetectedGame(false, true, "新房002", "旧房001"), "changed game name after stop is adopted for manual join");
            Assert(Phase4ManualFollow.CanAdoptDetectedGame(true, true, "旧房001", "旧房001"), "live roster confirms a same-name current game");
            byte[] utf8Room = Encoding.UTF8.GetBytes("96碎片房\0");
            Assert(LobbyDiscovery.ReadName(utf8Room, 0, utf8Room.Length) == "96碎片房", "UTF-8 room name decoding");
            byte[] patternData = { 0x00, 0xAA, 0x10, 0xCC, 0x00 };
            Assert(ModulePatternScanner.FindOffset(patternData, ModulePatternScanner.Parse("AA ?? CC")) == 1, "wildcard module pattern scan");
            byte[] rosterNode = new byte[0x150];
            byte[] inviterName = Encoding.UTF8.GetBytes("实际邀请人\0");
            Buffer.BlockCopy(inviterName, 0, rosterNode, 0, inviterName.Length);
            Buffer.BlockCopy(BitConverter.GetBytes((uint)1234), 0, rosterNode, 0x48, 4);
            Buffer.BlockCopy(BitConverter.GetBytes((ushort)42), 0, rosterNode, 0x5A, 2);
            Buffer.BlockCopy(BitConverter.GetBytes((uint)2), 0, rosterNode, 0x68, 4);
            RosterPlayer inviter = RosterReader.ParsePlayer(rosterNode);
            Assert(inviter.Name == "实际邀请人", "roster inviter name decoding");
            Assert(inviter.UnitId == 1234 && inviter.PartyId == 42, "roster inviter identity fields");
            Assert(inviter.PartyFlags == 2, "roster accept-invite flag decoding");
            Console.WriteLine("Self-test passed: {0} assertions.", assertions);
        }

        private static int assertions;

        private static void Assert(bool condition, string name)
        {
            assertions++;
            if (!condition) throw new InvalidDataException("Self-test failed: " + name);
        }
    }
}
