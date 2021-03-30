using System;
using System.Collections.Generic;
using System.Threading;

namespace SimpleMessageBus
{
    public class ShardedCounter
    {
        private readonly object _shardedCounterLock = new();
        private long _deadShardSum;
        private List<Shard> _shards = new();
        private readonly LocalDataStoreSlot _slot = Thread.AllocateDataSlot();

        public void Increase(long amount)
        {
            var counter = IncreaseCounter();

            counter.Increase(amount);
        }

        public void Decrease(long amount)
        {
            var counter = IncreaseCounter();

            counter.Decrease(amount);
        }

        public long Count
        {
            get
            {
                var sum = _deadShardSum;

                var livingShards = new List<Shard>();

                lock (_shardedCounterLock)
                {
                    foreach (var shard in _shards)
                    {
                        sum += shard.Count;

                        if (shard.Owner.IsAlive)
                        {
                            livingShards.Add(shard);
                        }
                        else
                        {
                            _deadShardSum += shard.Count;
                        }
                    }

                    _shards = livingShards;
                }

                return sum;
            }
        }

        private Shard IncreaseCounter()
        {
            if (!(Thread.GetData(_slot) is Shard counter))
            {
                counter = new Shard
                {
                    Owner = Thread.CurrentThread
                };

                Thread.SetData(_slot, counter);

                lock (_shardedCounterLock)
                {
                    _shards.Add(counter);
                }
            }

            return counter;
        }
    }

    internal class Shard : InterlockedCounter
    {
        public Thread Owner { get; set; }
    }

    internal class InterlockedCounter
    {
        private long _count;

        public long Count => Interlocked.CompareExchange(ref _count, 0, 0);

        public void Increase(long amount)
        {
            Interlocked.Add(ref _count, amount);
        }

        public void Decrease(long amount)
        {
            Interlocked.Add(ref _count, -Math.Abs(amount));
        }
    }
}