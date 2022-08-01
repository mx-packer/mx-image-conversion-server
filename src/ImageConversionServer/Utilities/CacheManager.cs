using BitFaster.Caching;
using BitFaster.Caching.Lru;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ImageConversionServer.Utilities
{
    internal class CacheManager
    {
        internal int Capacity { get; private set; }
        
        internal IAsyncCache<string, byte[]> Storage { get; private set; }

        internal CacheManager(int capacity, int duration = 10)
        {
            Capacity = capacity;
            Storage = new ConcurrentLruBuilder<string, byte[]>()
                .WithCapacity(capacity)
                .WithAtomicGetOrAdd()
                .WithExpireAfterWrite(TimeSpan.FromMinutes(duration))
                .AsAsyncCache()
                .Build();
        }
    }
}
