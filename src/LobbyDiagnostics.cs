using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace D2R96TZ
{
    public sealed class LobbyBenchmarkResult
    {
        public int Iterations { get; set; }
        public int Rooms { get; set; }
        public double MinMs { get; set; }
        public double AverageMs { get; set; }
        public double P95Ms { get; set; }
        public double MaxMs { get; set; }
    }

    public sealed class RecordTimeProbeResult
    {
        public string RoomName { get; set; }
        public int RoomIndex { get; set; }
        public int SelectedTimeBefore { get; set; }
        public int SelectedTimeAfter { get; set; }
        public int ChangedByteCount { get; set; }
        public List<int> CandidateOffsets { get; set; }
    }

    public sealed class LobbyDiagnostics
    {
        private readonly ProcessMemoryReader memory;
        private readonly AppConfig config;
        private readonly LobbyReader reader;

        public LobbyDiagnostics(ProcessMemoryReader memory, AppConfig config)
        {
            this.memory = memory;
            this.config = config;
            reader = new LobbyReader(memory, config);
        }

        public LobbyBenchmarkResult Benchmark(int iterations)
        {
            reader.ReadAllRooms(false);
            var samples = new List<double>(iterations);
            int rooms = 0;
            var stopwatch = new Stopwatch();
            for (int index = 0; index < iterations; index++)
            {
                stopwatch.Restart();
                rooms = reader.ReadAllRooms(false).Rooms.Count;
                stopwatch.Stop();
                samples.Add(stopwatch.Elapsed.TotalMilliseconds);
            }
            samples.Sort();
            int p95Index = Math.Min(samples.Count - 1, (int)Math.Ceiling(samples.Count * 0.95) - 1);
            return new LobbyBenchmarkResult
            {
                Iterations = iterations,
                Rooms = rooms,
                MinMs = samples[0],
                AverageMs = samples.Average(),
                P95Ms = samples[p95Index],
                MaxMs = samples[samples.Count - 1]
            };
        }

        public RecordTimeProbeResult ProbeSelectedRecord(int waitMilliseconds)
        {
            SelectedGameInfo selectedBefore = reader.ReadSelectedGame();
            if (selectedBefore.Name.Length == 0) throw new InvalidOperationException("请先在大厅手动选中一个房间。");

            int tableSize = checked(config.AllGamesRecordSize * config.AllGamesMaxRecords);
            IntPtr tableAddress = new IntPtr(checked(memory.ModuleBase.ToInt64() + config.AllGamesOffset));
            byte[] tableBefore = memory.Read(tableAddress, tableSize);
            int roomIndex = FindRoomIndex(tableBefore, selectedBefore.Name);
            if (roomIndex < 0) throw new InvalidOperationException("当前选中房间不在大厅快照中，无法对照 record。");

            byte[] recordBefore = SliceRecord(tableBefore, roomIndex);
            Thread.Sleep(waitMilliseconds);
            byte[] tableAfter = memory.Read(tableAddress, tableSize);
            byte[] recordAfter = SliceRecord(tableAfter, roomIndex);
            SelectedGameInfo selectedAfter = reader.ReadSelectedGame();
            if (!string.Equals(selectedBefore.Name, selectedAfter.Name, StringComparison.Ordinal))
                throw new InvalidOperationException("探测期间选中房间发生变化，结果已丢弃。");

            int selectedDelta = selectedAfter.GameTimeSec - selectedBefore.GameTimeSec;
            var candidates = new List<int>();
            int changedBytes = 0;
            for (int offset = 0; offset < recordBefore.Length; offset++)
                if (recordBefore[offset] != recordAfter[offset]) changedBytes++;
            if (selectedDelta > 0)
            {
                for (int offset = 0; offset <= recordBefore.Length - 4; offset++)
                {
                    int before = BitConverter.ToInt32(recordBefore, offset);
                    int after = BitConverter.ToInt32(recordAfter, offset);
                    if (after - before == selectedDelta && before >= 0 && before <= 86400) candidates.Add(offset);
                }
            }

            return new RecordTimeProbeResult
            {
                RoomName = selectedBefore.Name,
                RoomIndex = roomIndex,
                SelectedTimeBefore = selectedBefore.GameTimeSec,
                SelectedTimeAfter = selectedAfter.GameTimeSec,
                ChangedByteCount = changedBytes,
                CandidateOffsets = candidates
            };
        }

        private int FindRoomIndex(byte[] table, string selectedName)
        {
            for (int index = 0; index < config.AllGamesMaxRecords; index++)
            {
                int offset = index * config.AllGamesRecordSize + config.AllGamesNameOffset;
                string name = LobbyDiscovery.ReadName(table, offset, config.AllGamesNameLength);
                if (string.Equals(name, selectedName, StringComparison.Ordinal)) return index;
            }
            return -1;
        }

        private byte[] SliceRecord(byte[] table, int roomIndex)
        {
            var record = new byte[config.AllGamesRecordSize];
            Buffer.BlockCopy(table, roomIndex * config.AllGamesRecordSize, record, 0, record.Length);
            return record;
        }
    }
}
