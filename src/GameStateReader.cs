using System;

namespace D2R96TZ
{
    public sealed class GameStateReader
    {
        private const string GameDataPattern = "44 88 25 ?? ?? ?? ?? 66 44 89 25";
        private const int CurrentGameNameLength = 64;
        private readonly ProcessMemoryReader memory;

        public long PatternAddress { get; private set; }
        public long GameDataOffset { get; private set; }
        public long GameNameAddress { get; private set; }

        public GameStateReader(ProcessMemoryReader memory)
        {
            this.memory = memory;
            var scanner = new ModulePatternScanner(memory);
            PatternAddress = scanner.Find(GameDataPattern);
            if (PatternAddress == 0) throw new InvalidOperationException("当前 D2R build 未找到 gameData 特征码。");
            int displacement = BitConverter.ToInt32(memory.Read(new IntPtr(PatternAddress + 3), 4), 0);
            long patternOffset = PatternAddress - memory.ModuleBase.ToInt64();
            GameDataOffset = checked(patternOffset - 0x121 + displacement);
            GameNameAddress = checked(memory.ModuleBase.ToInt64() + GameDataOffset + 0x20);
        }

        public string ReadCurrentGameName()
        {
            byte[] data = memory.Read(new IntPtr(GameNameAddress), CurrentGameNameLength);
            return LobbyDiscovery.ReadName(data, 0, data.Length);
        }

    }
}
