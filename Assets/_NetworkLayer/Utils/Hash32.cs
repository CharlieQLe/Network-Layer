using System.Collections.Generic;

namespace NetworkLayer.Utils {
    public static class Hash32 {
        private static readonly Dictionary<string, uint> StoredHashes;

        static Hash32() {
            StoredHashes = new Dictionary<string, uint>();
        }

        /// <summary>
        /// Generate a 32-bit hash via the Fowler-Noll-Vo hash function.
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static uint Generate(string value) {
            if (StoredHashes.TryGetValue(value, out uint hash)) return hash;
            hash = 2166136261;
            for (int i = 0; i < value.Length; i++) hash = (hash ^ value[i]) * 16777619;
            StoredHashes[value] = hash;
            return hash;
        }
    }
}