using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace D2R96TZ
{
    public sealed class CandidateInspection
    {
        public RoomInfo Snapshot { get; set; }
        public RoomInfo Current { get; set; }
        public string RejectReason { get; set; }
        public bool SnapshotChanged { get; set; }
    }

    public sealed class Phase2DryRunResult
    {
        public int RoomsFound { get; set; }
        public List<CandidateInspection> Inspections { get; set; }
        public RoomInfo Recommended { get; set; }
        public long RefreshAndReadMs { get; set; }
        public long SelectedInfoMs { get; set; }
        public long CandidateSortMs { get; set; }
        public bool LobbyChanged { get; set; }
        public string FilterKeyword { get; set; }
        public int VisibleRoomsFound { get; set; }
    }

    public sealed class Phase2DryRun
    {
        private readonly LobbyReader reader;
        private readonly LobbyUiController ui;
        private readonly AppConfig config;

        public Phase2DryRun(ProcessMemoryReader memory, AppConfig config)
        {
            reader = new LobbyReader(memory, config);
            ui = new LobbyUiController(memory, config);
            this.config = config;
        }

        public Phase2DryRunResult Run()
        {
            if (string.IsNullOrWhiteSpace(config.SearchKeyword)) return RunCurrentFilter();
            return RunInternal(config.SearchKeyword, config.SearchKeyword, null);
        }

        public Phase2DryRunResult RunCurrentFilter(ISet<string> excludedNames = null)
        {
            string currentKeyword = ui.ReadSearchKeyword();
            if (string.IsNullOrWhiteSpace(currentKeyword))
                return RunInternal(null, string.Empty, excludedNames);
            return RunInternal(null, currentKeyword.Trim(), excludedNames);
        }

        public Phase2DryRunResult RunFilter(string filterKeyword, ISet<string> excludedNames = null)
        {
            if (string.IsNullOrWhiteSpace(filterKeyword))
                return RunInternal(null, string.Empty, excludedNames);
            return RunInternal(null, filterKeyword.Trim(), excludedNames);
        }

        private Phase2DryRunResult RunInternal(string uiSearchKeyword, string filterKeyword, ISet<string> excludedNames)
        {
            var refreshWatch = Stopwatch.StartNew();
            if (uiSearchKeyword == null) ui.RefreshCurrentSearch();
            else ui.SearchAndRefresh(uiSearchKeyword);
            LobbyReadResult lobby = reader.ReadAllRooms(false);
            refreshWatch.Stop();

            var visibleRooms = BuildVisibleRooms(lobby.Rooms, filterKeyword);
            var inspectionOrder = BuildInspectionOrderFromVisible(visibleRooms, config, excludedNames);
            var inspections = new List<CandidateInspection>();
            var selectedWatch = Stopwatch.StartNew();
            var playerGroups = inspectionOrder.GroupBy(room => room.Players).OrderByDescending(group => group.Key).ToList();
            for (int groupIndex = 0; groupIndex < playerGroups.Count; groupIndex++)
            {
                var playerGroup = playerGroups[groupIndex];
                foreach (RoomInfo snapshot in playerGroup)
                {
                    var inspection = new CandidateInspection { Snapshot = snapshot };
                    ui.SelectLobbyIndex(snapshot.VisibleIndex, visibleRooms.Count);
                    SelectedGameInfo selected = WaitForSelectedGame(snapshot.Name);
                    if (!string.Equals(selected.Name, snapshot.Name, StringComparison.Ordinal))
                    {
                        inspection.RejectReason = "selection_changed";
                    }
                    else
                    {
                        inspection.Current = new RoomInfo
                        {
                            LobbyIndex = snapshot.LobbyIndex,
                            VisibleIndex = snapshot.VisibleIndex,
                            Name = selected.Name,
                            Players = selected.Players,
                            GameTimeSec = selected.GameTimeSec
                        };
                        inspection.SnapshotChanged = selected.Players != snapshot.Players;
                        if (selected.Players >= 8) inspection.RejectReason = "full";
                        else if (selected.GameTimeSec < 0) inspection.RejectReason = "invalid_age";
                        else if (selected.GameTimeSec > config.MaxGameAgeSec) inspection.RejectReason = "too_old";
                    }
                    inspections.Add(inspection);
                    if (IsLobbyChange(inspection.RejectReason)) break;
                }
                if (inspections.Any(item => IsLobbyChange(item.RejectReason))) break;
                int bestPlayers = inspections
                    .Where(item => item.RejectReason == null && item.Current != null)
                    .Select(item => item.Current.Players)
                    .DefaultIfEmpty(-1)
                    .Max();
                int nextSnapshotPlayers = groupIndex + 1 < playerGroups.Count ? playerGroups[groupIndex + 1].Key : -1;
                if (bestPlayers > nextSnapshotPlayers) break;
            }
            selectedWatch.Stop();

            var sortWatch = Stopwatch.StartNew();
            bool lobbyChanged = inspections.Any(item => IsLobbyChange(item.RejectReason));
            RoomInfo recommended = SelectRecommendation(inspections, config, filterKeyword);
            sortWatch.Stop();
            return new Phase2DryRunResult
            {
                RoomsFound = lobby.Rooms.Count,
                Inspections = inspections,
                Recommended = recommended,
                RefreshAndReadMs = refreshWatch.ElapsedMilliseconds,
                SelectedInfoMs = selectedWatch.ElapsedMilliseconds,
                CandidateSortMs = sortWatch.ElapsedMilliseconds,
                LobbyChanged = lobbyChanged,
                FilterKeyword = filterKeyword,
                VisibleRoomsFound = visibleRooms.Count
            };
        }

        public static List<RoomInfo> BuildInspectionOrder(IEnumerable<RoomInfo> rooms, AppConfig config)
        {
            return BuildInspectionOrder(rooms, config, config.SearchKeyword, null);
        }

        public static List<RoomInfo> BuildInspectionOrder(IEnumerable<RoomInfo> rooms, AppConfig config, ISet<string> excludedNames)
        {
            return BuildInspectionOrder(rooms, config, config.SearchKeyword, excludedNames);
        }

        private static List<RoomInfo> BuildInspectionOrder(IEnumerable<RoomInfo> rooms, AppConfig config, string searchKeyword, ISet<string> excludedNames)
        {
            return BuildInspectionOrderFromVisible(BuildVisibleRooms(rooms, searchKeyword), config, excludedNames);
        }

        internal static List<RoomInfo> BuildVisibleRooms(IEnumerable<RoomInfo> rooms, string searchKeyword)
        {
            var visible = rooms
                .Where(room => room.LobbyIndex >= 0)
                .GroupBy(room => room.Name, StringComparer.Ordinal)
                .Select(group => group.OrderBy(room => room.LobbyIndex).First())
                .OrderBy(room => room.LobbyIndex)
                .Where(room => string.IsNullOrEmpty(searchKeyword) || room.Name.IndexOf(searchKeyword, StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(room => new RoomInfo
                {
                    LobbyIndex = room.LobbyIndex,
                    Name = room.Name,
                    Players = room.Players,
                    GameTimeSec = room.GameTimeSec
                })
                .ToList();
            for (int index = 0; index < visible.Count; index++) visible[index].VisibleIndex = index;
            return visible;
        }

        private static List<RoomInfo> BuildInspectionOrderFromVisible(IEnumerable<RoomInfo> visibleRooms, AppConfig config, ISet<string> excludedNames)
        {
            return visibleRooms
                .Where(room => room.Players >= config.MinPlayers)
                .Where(room => room.Players < 8)
                .Where(room => room.LobbyIndex >= 0 && room.LobbyIndex < config.AllGamesMaxRecords)
                .Where(room => excludedNames == null || !excludedNames.Contains(room.Name))
                .OrderByDescending(room => room.Players)
                .ThenBy(room => room.LobbyIndex)
                .ToList();
        }

        public static RoomInfo SelectRecommendation(IEnumerable<CandidateInspection> inspections, AppConfig config)
        {
            return SelectRecommendation(inspections, config, config.SearchKeyword);
        }

        private static RoomInfo SelectRecommendation(IEnumerable<CandidateInspection> inspections, AppConfig config, string searchKeyword)
        {
            var items = inspections.ToList();
            if (items.Any(item => IsLobbyChange(item.RejectReason))) return null;
            return RoomSelector.Rank(items.Where(item => item.RejectReason == null).Select(item => item.Current), config, searchKeyword).FirstOrDefault();
        }

        private static bool IsLobbyChange(string rejectReason)
        {
            return rejectReason == "selection_changed";
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
            while (stopwatch.ElapsedMilliseconds < config.SelectedInfoWaitMs);
            return selected;
        }

    }
}
