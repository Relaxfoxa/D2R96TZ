using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace D2R96TZ
{
    public sealed class AppConfig
    {
        public long AllGamesOffset { get; private set; }
        public long SelectedGameOffset { get; private set; }
        public int AllGamesRecordSize { get; private set; }
        public int AllGamesNameOffset { get; private set; }
        public int AllGamesNameLength { get; private set; }
        public int AllGamesPlayersOffset { get; private set; }
        public int AllGamesMaxRecords { get; private set; }
        public int SelectedGameNameOffset { get; private set; }
        public int SelectedGameNameLength { get; private set; }
        public int SelectedGameTimeOffset { get; private set; }
        public int SelectedGamePlayersOffset { get; private set; }
        public int SelectedGameNamesOffset { get; private set; }
        public int SelectedGameNameStride { get; private set; }
        public int SelectedGameMaxPlayers { get; private set; }
        public string SearchKeyword { get; private set; }
        public string SupportedFileVersion { get; private set; }
        public int MaxGameAgeSec { get; private set; }
        public int MinPlayers { get; private set; }
        public int LobbyRefreshWaitMs { get; private set; }
        public int SelectedInfoWaitMs { get; private set; }
        public int JoinTimeoutMs { get; private set; }
        public int NextRoomWaitWindowSec { get; private set; }
        public int NextRoomRetryIntervalMs { get; private set; }

        private AppConfig()
        {
            SearchKeyword = "96";
            MaxGameAgeSec = 300;
            MinPlayers = 0;
            LobbyRefreshWaitMs = 100;
            SelectedInfoWaitMs = 100;
            JoinTimeoutMs = 15000;
            NextRoomWaitWindowSec = 15;
            NextRoomRetryIntervalMs = 1000;
        }

        public static AppConfig Load(string path)
        {
            var config = new AppConfig();
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var rawLine in File.ReadAllLines(path))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                var separator = line.IndexOf('=');
                if (separator <= 0) continue;
                values[line.Substring(0, separator).Trim()] = line.Substring(separator + 1).Trim();
            }

            config.AllGamesOffset = ReadLong(values, "all_games_offset");
            config.SelectedGameOffset = ReadLong(values, "selected_game_offset");
            config.AllGamesRecordSize = ReadInt(values, "all_games_record_size");
            config.AllGamesNameOffset = ReadInt(values, "all_games_name_offset");
            config.AllGamesNameLength = ReadInt(values, "all_games_name_length", 32);
            config.AllGamesPlayersOffset = ReadInt(values, "all_games_players_offset");
            config.AllGamesMaxRecords = ReadInt(values, "all_games_max_records");
            config.SelectedGameNameOffset = ReadInt(values, "selected_game_name_offset");
            config.SelectedGameNameLength = ReadInt(values, "selected_game_name_length");
            config.SelectedGameTimeOffset = ReadInt(values, "selected_game_time_offset");
            config.SelectedGamePlayersOffset = ReadInt(values, "selected_game_players_offset");
            config.SelectedGameNamesOffset = ReadInt(values, "selected_game_names_offset");
            config.SelectedGameNameStride = ReadInt(values, "selected_game_name_stride");
            config.SelectedGameMaxPlayers = ReadInt(values, "selected_game_max_players");
            config.SearchKeyword = ReadString(values, "search_keyword", config.SearchKeyword);
            config.SupportedFileVersion = ReadString(values, "supported_file_version", string.Empty);
            config.MaxGameAgeSec = ReadInt(values, "max_game_age_sec", config.MaxGameAgeSec);
            config.MinPlayers = ReadInt(values, "min_players", config.MinPlayers);
            config.LobbyRefreshWaitMs = ReadInt(values, "lobby_refresh_wait_ms", config.LobbyRefreshWaitMs);
            config.SelectedInfoWaitMs = ReadInt(values, "selected_info_wait_ms", config.SelectedInfoWaitMs);
            config.JoinTimeoutMs = ReadInt(values, "join_timeout_ms", config.JoinTimeoutMs);
            config.NextRoomWaitWindowSec = ReadInt(values, "next_room_wait_window_sec", config.NextRoomWaitWindowSec);
            config.NextRoomRetryIntervalMs = ReadInt(values, "next_room_retry_interval_ms", config.NextRoomRetryIntervalMs);
            config.Validate();
            return config;
        }

        public void OverrideSearchKeyword(string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword) || keyword.Length > 16) throw new ArgumentException("搜索关键字长度必须为 1..16。");
            SearchKeyword = keyword;
        }

        private void Validate()
        {
            if (AllGamesOffset <= 0 || SelectedGameOffset <= 0) throw new InvalidDataException("配置中的房间结构偏移必须为正数。");
            if (AllGamesRecordSize <= 0 || AllGamesMaxRecords <= 0) throw new InvalidDataException("大厅 record 配置无效。");
            if (SelectedGameMaxPlayers < 1 || SelectedGameMaxPlayers > 8) throw new InvalidDataException("selected_game_max_players 必须在 1..8。");
            if (LobbyRefreshWaitMs < 0 || SelectedInfoWaitMs < 0 || JoinTimeoutMs < 1000) throw new InvalidDataException("大厅/Join 等待时间配置无效。");
            if (NextRoomWaitWindowSec < 1 || NextRoomRetryIntervalMs < 100) throw new InvalidDataException("下一房重试时间配置无效。");
        }

        private static string ReadString(Dictionary<string, string> values, string key, string fallback)
        {
            string value;
            return values.TryGetValue(key, out value) ? value : fallback;
        }

        private static int ReadInt(Dictionary<string, string> values, string key, int fallback = 0)
        {
            string value;
            if (!values.TryGetValue(key, out value)) return fallback;
            long number = ParseNumber(value, key);
            if (number > int.MaxValue || number < int.MinValue) throw new InvalidDataException(key + " 超出 Int32 范围。");
            return (int)number;
        }

        private static long ReadLong(Dictionary<string, string> values, string key)
        {
            string value;
            if (!values.TryGetValue(key, out value)) throw new InvalidDataException("缺少配置项: " + key);
            return ParseNumber(value, key);
        }

        private static long ParseNumber(string text, string key)
        {
            try
            {
                if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    return long.Parse(text.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                return long.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
            }
            catch (FormatException) { throw new InvalidDataException("配置项不是有效数字: " + key); }
        }
    }
}
