using System;
using System.Diagnostics;
using System.Threading;

namespace D2R96TZ
{
    public sealed class Phase3JoinResult
    {
        public Phase2DryRunResult DryRun { get; set; }
        public string TargetRoom { get; set; }
        public string CurrentGameName { get; set; }
        public string Status { get; set; }
        public long JoinMs { get; set; }
    }

    public sealed class Phase3Join
    {
        private readonly ProcessMemoryReader memory;
        private readonly AppConfig config;

        public Phase3Join(ProcessMemoryReader memory, AppConfig config)
        {
            this.memory = memory;
            this.config = config;
        }

        public Phase3JoinResult Run()
        {
            var gameState = new GameStateReader(memory);
            string currentBefore = gameState.ReadCurrentGameName();
            if (currentBefore.Length > 0)
                return new Phase3JoinResult { CurrentGameName = currentBefore, Status = "already_in_game" };

            Phase2DryRunResult dryRun = new Phase2DryRun(memory, config).Run();
            var result = new Phase3JoinResult { DryRun = dryRun };
            if (dryRun.Recommended == null)
            {
                result.Status = dryRun.LobbyChanged ? "lobby_changed" : "no_recommendation";
                return result;
            }

            result.TargetRoom = dryRun.Recommended.Name;
            var reader = new LobbyReader(memory, config);
            var ui = new LobbyUiController(memory, config);
            ui.SelectLobbyIndex(dryRun.Recommended.LobbyIndex, dryRun.RoomsFound);
            SelectedGameInfo selected = WaitForSelectedGame(reader, result.TargetRoom);
            if (!string.Equals(selected.Name, result.TargetRoom, StringComparison.Ordinal))
            {
                result.Status = "pre_join_name_mismatch";
                return result;
            }
            if (selected.Players != dryRun.Recommended.Players || selected.Players >= 8)
            {
                result.Status = "pre_join_players_changed";
                return result;
            }
            if (selected.GameTimeSec < 0 || selected.GameTimeSec > config.MaxGameAgeSec)
            {
                result.Status = "pre_join_age_changed";
                return result;
            }

            var stopwatch = Stopwatch.StartNew();
            ui.ClickJoin();
            string current = string.Empty;
            while (stopwatch.ElapsedMilliseconds < config.JoinTimeoutMs)
            {
                Thread.Sleep(200);
                current = gameState.ReadCurrentGameName();
                if (string.Equals(current, result.TargetRoom, StringComparison.Ordinal))
                {
                    stopwatch.Stop();
                    result.CurrentGameName = current;
                    result.JoinMs = stopwatch.ElapsedMilliseconds;
                    result.Status = "success";
                    return result;
                }
                if (current.Length > 0)
                {
                    stopwatch.Stop();
                    result.CurrentGameName = current;
                    result.JoinMs = stopwatch.ElapsedMilliseconds;
                    result.Status = "wrong_game";
                    return result;
                }
            }
            stopwatch.Stop();
            result.CurrentGameName = current;
            result.JoinMs = stopwatch.ElapsedMilliseconds;
            result.Status = "join_timeout";
            return result;
        }

        private SelectedGameInfo WaitForSelectedGame(LobbyReader reader, string expectedName)
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
