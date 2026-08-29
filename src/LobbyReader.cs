using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace D2R96TZ
{
    public sealed class RoomInfo
    {
        public int LobbyIndex { get; set; }
        public string Name { get; set; }
        public int Players { get; set; }
        public int? GameTimeSec { get; set; }
    }

    public sealed class SelectedGameInfo
    {
        public string Name { get; set; }
        public int Players { get; set; }
        public int GameTimeSec { get; set; }
        public List<string> PlayersNames { get; set; }
    }

    public sealed class LobbyReadResult
    {
        public List<RoomInfo> Rooms { get; set; }
        public long ReadMs { get; set; }
        public int AnomalousRecordCount { get; set; }
        public List<LobbyRecordDebug> DebugRecords { get; set; }
        public long AllGamesAddress { get; set; }
    }

    public sealed class LobbyRecordDebug
    {
        public int Index { get; set; }
        public long Address { get; set; }
        public string NameRaw { get; set; }
        public string Name { get; set; }
        public byte PlayersRaw { get; set; }
    }

    public sealed class LobbyReader
    {
        private readonly ProcessMemoryReader memory;
        private readonly AppConfig config;

        public LobbyReader(ProcessMemoryReader memory, AppConfig config)
        {
            this.memory = memory;
            this.config = config;
        }

        public LobbyReadResult ReadAllRooms(bool includeDebug)
        {
            var stopwatch = Stopwatch.StartNew();
            int size = checked(config.AllGamesRecordSize * config.AllGamesMaxRecords);
            IntPtr tableAddress = Add(memory.ModuleBase, config.AllGamesOffset);
            var table = memory.Read(tableAddress, size);
            var rooms = new List<RoomInfo>();
            var seenNames = new HashSet<string>(StringComparer.Ordinal);
            var debugRecords = new List<LobbyRecordDebug>();
            int anomalies = 0;

            for (int index = 0; index < config.AllGamesMaxRecords; index++)
            {
                int record = checked(index * config.AllGamesRecordSize);
                string name = ReadUtf8(table, record + config.AllGamesNameOffset, config.AllGamesNameLength);
                if (includeDebug && index < 5)
                {
                    debugRecords.Add(new LobbyRecordDebug
                    {
                        Index = index,
                        Address = tableAddress.ToInt64() + record,
                        NameRaw = ToHex(table, record + config.AllGamesNameOffset, config.AllGamesNameLength),
                        Name = name,
                        PlayersRaw = table[record + config.AllGamesPlayersOffset]
                    });
                }
                if (name.Length == 0)
                {
                    if (!includeDebug) break;
                    continue;
                }
                if (!includeDebug && !seenNames.Add(name)) break;
                byte players = table[record + config.AllGamesPlayersOffset];
                if (!IsPlausibleName(name) || players > 8) anomalies++;
                rooms.Add(new RoomInfo
                {
                    LobbyIndex = index,
                    Name = name,
                    Players = players,
                    GameTimeSec = null
                });
            }

            stopwatch.Stop();
            return new LobbyReadResult
            {
                Rooms = rooms,
                ReadMs = stopwatch.ElapsedMilliseconds,
                AnomalousRecordCount = anomalies,
                DebugRecords = debugRecords,
                AllGamesAddress = tableAddress.ToInt64()
            };
        }

        public SelectedGameInfo ReadSelectedGame()
        {
            int playerBlockEnd = config.SelectedGameNamesOffset + (config.SelectedGameMaxPlayers - 1) * config.SelectedGameNameStride + 32;
            int size = Math.Max(playerBlockEnd, Math.Max(config.SelectedGameTimeOffset + 4, config.SelectedGamePlayersOffset + 1));
            var data = memory.Read(Add(memory.ModuleBase, config.SelectedGameOffset), size);
            int playerCount = Math.Min(data[config.SelectedGamePlayersOffset], config.SelectedGameMaxPlayers);
            var players = new List<string>();
            for (int index = 0; index < playerCount; index++)
            {
                int offset = config.SelectedGameNamesOffset + index * config.SelectedGameNameStride;
                string name = ReadUtf8(data, offset, 32);
                if (name.Length > 0) players.Add(name);
            }

            return new SelectedGameInfo
            {
                Name = ReadUtf8(data, config.SelectedGameNameOffset, config.SelectedGameNameLength),
                Players = playerCount,
                GameTimeSec = BitConverter.ToInt32(data, config.SelectedGameTimeOffset),
                PlayersNames = players
            };
        }

        private static IntPtr Add(IntPtr address, long offset)
        {
            return new IntPtr(checked(address.ToInt64() + offset));
        }

        private static string ReadUtf8(byte[] data, int offset, int maxLength)
        {
            int end = Math.Min(data.Length, offset + maxLength);
            var bytes = new List<byte>();
            for (int index = offset; index < end && data[index] != 0; index++) bytes.Add(data[index]);
            return Encoding.UTF8.GetString(bytes.ToArray()).TrimEnd('\uFFFD');
        }

        private static bool IsPlausibleName(string name)
        {
            if (name.Length == 0 || name.Length > 16) return false;
            for (int index = 0; index < name.Length; index++)
            {
                char character = name[index];
                if (char.IsControl(character) || character == '\uFFFD') return false;
            }
            return true;
        }

        private static string ToHex(byte[] data, int offset, int length)
        {
            var builder = new StringBuilder();
            for (int index = offset; index < offset + length && index < data.Length; index++)
            {
                if (builder.Length > 0) builder.Append(' ');
                builder.Append(data[index].ToString("X2"));
            }
            return builder.ToString();
        }
    }
}
