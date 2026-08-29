using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace D2R96TZ
{
    public sealed class LobbyDiscoveryResult
    {
        public long TableAddress { get; set; }
        public long TableOffset { get; set; }
        public List<RoomInfo> Rooms { get; set; }
        public int PrefixHits { get; set; }
    }

    public sealed class LobbyDiscovery
    {
        private readonly ProcessMemoryReader memory;
        private readonly AppConfig config;

        public LobbyDiscovery(ProcessMemoryReader memory, AppConfig config)
        {
            this.memory = memory;
            this.config = config;
        }

        public LobbyDiscoveryResult Find(string prefix)
        {
            byte[] needle = Encoding.ASCII.GetBytes(prefix);
            var hits = FindBytes(needle);
            LobbyDiscoveryResult best = null;
            foreach (long hit in hits.Distinct())
            {
                long tableAddress = hit - config.AllGamesNameOffset;
                int tableSize = checked(config.AllGamesRecordSize * config.AllGamesMaxRecords);
                byte[] table;
                if (!memory.TryRead(new IntPtr(tableAddress), tableSize, out table)) continue;
                var rooms = ParsePrefixRooms(table, prefix);
                if (rooms.Count < 2) continue;
                if (best == null || rooms.Count > best.Rooms.Count)
                {
                    best = new LobbyDiscoveryResult
                    {
                        TableAddress = tableAddress,
                        TableOffset = tableAddress - memory.ModuleBase.ToInt64(),
                        Rooms = rooms,
                        PrefixHits = hits.Count
                    };
                }
            }
            if (best != null) return best;
            return new LobbyDiscoveryResult { Rooms = new List<RoomInfo>(), PrefixHits = hits.Count };
        }

        public List<long> FindUtf8(string text)
        {
            return FindBytes(Encoding.UTF8.GetBytes(text));
        }

        private List<long> FindBytes(byte[] needle)
        {
            var hits = new List<long>();
            foreach (var region in memory.EnumerateReadableModuleRegions())
            {
                const int chunkSize = 1024 * 1024;
                for (long offset = 0; offset < region.Size; offset += chunkSize)
                {
                    int size = (int)Math.Min(chunkSize, region.Size - offset);
                    byte[] data;
                    long address = region.Address + offset;
                    if (!memory.TryRead(new IntPtr(address), size, out data)) continue;
                    for (int index = 0; index <= data.Length - needle.Length; index++)
                    {
                        bool match = true;
                        for (int n = 0; n < needle.Length; n++)
                        {
                            if (data[index + n] != needle[n]) { match = false; break; }
                        }
                        if (match) hits.Add(address + index);
                    }
                }
            }
            return hits;
        }

        private List<RoomInfo> ParsePrefixRooms(byte[] table, string prefix)
        {
            var rooms = new List<RoomInfo>();
            for (int index = 0; index < config.AllGamesMaxRecords; index++)
            {
                int record = index * config.AllGamesRecordSize;
                string name = ReadName(table, record + config.AllGamesNameOffset, 32);
                if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                byte players = table[record + config.AllGamesPlayersOffset];
                if (players > 8) continue;
                rooms.Add(new RoomInfo { Name = name, Players = players, GameTimeSec = null });
            }
            return rooms;
        }

        internal static string ReadName(byte[] data, int offset, int maxLength)
        {
            int length = 0;
            while (length < maxLength && offset + length < data.Length && data[offset + length] != 0) length++;
            if (length == 0) return string.Empty;
            return Encoding.UTF8.GetString(data, offset, length).TrimEnd('\uFFFD');
        }
    }
}
