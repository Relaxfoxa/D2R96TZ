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
