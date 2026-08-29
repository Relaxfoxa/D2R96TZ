using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace D2R96TZ
{
    public sealed class RosterPlayer
    {
        public string Name { get; set; }
        public uint UnitId { get; set; }
        public ushort PartyId { get; set; }
        public uint PartyFlags { get; set; }
    }

    public sealed class RosterReader
    {
        private const string RosterPattern = "02 45 33 D2 4D 8B";
        private const int UnitIdOffset = 0x48;
        private const int PartyIdOffset = 0x5A;
        private const int PartyFlagsOffset = 0x68;
        private const int RosterNextOffset = 0x148;
        private readonly ProcessMemoryReader memory;
        private readonly long rosterPointerAddress;

        public RosterReader(ProcessMemoryReader memory)
        {
            this.memory = memory;
            var scanner = new ModulePatternScanner(memory);
            long patternAddress = scanner.Find(RosterPattern);
            if (patternAddress == 0) throw new InvalidOperationException("当前 D2R build 未找到 roster 特征码。");

            int displacement = BitConverter.ToInt32(memory.Read(new IntPtr(patternAddress - 3), 4), 0);
            long absoluteCandidate = checked(patternAddress + 1 + displacement);
            rosterPointerAddress = ResolvePointerAddress(absoluteCandidate);
            if (rosterPointerAddress == 0) throw new InvalidOperationException("roster 特征码命中，但无法解析 roster 指针。");
        }

        public List<string> ReadPlayerNames()
        {
            return ReadPlayers().Select(player => player.Name).ToList();
        }

        public List<RosterPlayer> ReadPlayers()
        {
            var players = new List<RosterPlayer>();
            byte[] pointerBytes;
            if (!memory.TryRead(new IntPtr(rosterPointerAddress), 8, out pointerBytes)) return players;
            long node = BitConverter.ToInt64(pointerBytes, 0);
            var visited = new HashSet<long>();
            for (int index = 0; index < 8 && node != 0 && visited.Add(node); index++)
            {
                byte[] nodeData;
                if (!memory.TryRead(new IntPtr(node), RosterNextOffset + 8, out nodeData)) break;
                RosterPlayer player = ParsePlayer(nodeData);
                if (player.Name.Length > 0) players.Add(player);
                node = BitConverter.ToInt64(nodeData, RosterNextOffset);
            }
            return players;
        }

        internal static RosterPlayer ParsePlayer(byte[] nodeData)
        {
            if (nodeData == null || nodeData.Length <= PartyFlagsOffset + 3)
                throw new ArgumentException("roster node 数据长度不足。", "nodeData");
            return new RosterPlayer
            {
                Name = ReadUtf8(nodeData, 0, Math.Min(32, nodeData.Length)),
                UnitId = BitConverter.ToUInt32(nodeData, UnitIdOffset),
                PartyId = BitConverter.ToUInt16(nodeData, PartyIdOffset),
                PartyFlags = BitConverter.ToUInt32(nodeData, PartyFlagsOffset)
            };
        }

        private long ResolvePointerAddress(long absoluteCandidate)
        {
            long moduleBase = memory.ModuleBase.ToInt64();
            long[] candidates = { absoluteCandidate, checked(moduleBase + absoluteCandidate) };
            foreach (long candidate in candidates)
            {
                if (candidate <= 0) continue;
                byte[] pointerBytes;
                if (!memory.TryRead(new IntPtr(candidate), 8, out pointerBytes)) continue;
                // An empty party can legitimately have a null head pointer.
                // The global pointer is still valid and may populate later.
                return candidate;
            }
            return 0;
        }

        private static string ReadUtf8(byte[] data, int offset, int maxLength)
        {
            int end = Math.Min(data.Length, offset + maxLength);
            int length = 0;
            while (offset + length < end && data[offset + length] != 0) length++;
            if (length == 0) return string.Empty;
            return Encoding.UTF8.GetString(data, offset, length).TrimEnd('\uFFFD');
        }
    }
}
