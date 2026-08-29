using System;
using System.Collections.Generic;
using System.Globalization;

namespace D2R96TZ
{
    public sealed class ModulePatternScanner
    {
        private readonly ProcessMemoryReader memory;

        public ModulePatternScanner(ProcessMemoryReader memory)
        {
            this.memory = memory;
        }

        public long Find(string pattern)
        {
            byte?[] parsed = Parse(pattern);
            foreach (ProcessMemoryReader.MemoryRegion region in memory.EnumerateReadableModuleRegions())
            {
                if (region.Size > int.MaxValue) continue;
                byte[] data;
                if (!memory.TryRead(new IntPtr(region.Address), (int)region.Size, out data)) continue;
                int offset = FindOffset(data, parsed);
                if (offset >= 0) return region.Address + offset;
            }
            return 0;
        }

        internal static byte?[] Parse(string pattern)
        {
            string[] tokens = pattern.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var parsed = new byte?[tokens.Length];
            for (int index = 0; index < tokens.Length; index++)
            {
                if (tokens[index] == "??" || tokens[index] == "?") parsed[index] = null;
                else parsed[index] = byte.Parse(tokens[index], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            }
            return parsed;
        }

        internal static int FindOffset(byte[] data, byte?[] pattern)
        {
            for (int offset = 0; offset <= data.Length - pattern.Length; offset++)
            {
                bool match = true;
                for (int index = 0; index < pattern.Length; index++)
                {
                    if (pattern[index].HasValue && data[offset + index] != pattern[index].Value)
                    {
                        match = false;
                        break;
                    }
                }
                if (match) return offset;
            }
            return -1;
        }
    }
}
