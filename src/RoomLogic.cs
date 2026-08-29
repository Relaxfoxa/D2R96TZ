using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace D2R96TZ
{
    public static class RoomSelector
    {
        public static List<RoomInfo> Rank(IEnumerable<RoomInfo> rooms, AppConfig config)
        {
            return Rank(rooms, config, config.SearchKeyword);
        }

        public static List<RoomInfo> Rank(IEnumerable<RoomInfo> rooms, AppConfig config, string searchKeyword)
        {
            var query = rooms
                .Where(room => room.GameTimeSec.HasValue)
                .Where(room => room.Players >= config.MinPlayers)
                .Where(room => room.Players < 8)
                .Where(room => room.GameTimeSec.Value <= config.MaxGameAgeSec)
                .OrderByDescending(room => room.Players)
                .ThenBy(room => room.GameTimeSec.Value)
                .ThenBy(room => room.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (string.IsNullOrEmpty(searchKeyword)) return query;
            return query.Where(room => room.Name.IndexOf(searchKeyword, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        }

        public static string NextRoomName(string currentRoom)
        {
            if (string.IsNullOrWhiteSpace(currentRoom)) return null;
            var match = Regex.Match(currentRoom, "^(.*?)(\\d+)$");
            if (!match.Success) return null;
            long number;
            if (!long.TryParse(match.Groups[2].Value, out number) || number == long.MaxValue) return null;
            return match.Groups[1].Value + (number + 1).ToString(new string('0', match.Groups[2].Value.Length));
        }
    }
}
