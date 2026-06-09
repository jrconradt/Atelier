namespace {{ namespaceName }}
{
    public class {{ typeName }}Pool
    {
        private readonly BlockingCollection<{{ typeName }}> _pool;
        private int _currentSize = 0;
        private readonly int _maxSize;
        private readonly int _initialSize;

        public {{ typeName }}Pool(int maxSize, int initialSize)
        {
            _maxSize = maxSize;
            _initialSize = initialSize;
            _pool = new BlockingCollection<{{ typeName }}>(new ConcurrentBag<{{ typeName }}>(), maxSize);

            for (int i = 0; i < initialSize; i++)
            {
                _pool.Add(new {{ typeName }}());
                Interlocked.Increment(ref _currentSize);
            }
        }

        public {{ typeName }} Rent()
        {
            if (_pool.TryTake(out var instance))
            {
                return instance;
            }

            while (true)
            {
                int observed = Volatile.Read(ref _currentSize);
                if (observed >= _maxSize)
                {
                    break;
                }

                if (Interlocked.CompareExchange(ref _currentSize, observed + 1, observed) == observed)
                {
                    return new {{ typeName }}();
                }
            }

            return _pool.Take();
        }

        public void Return({{ typeName }} instance)
        {
            {{ resetLines }}

            if (!_pool.TryAdd(instance))
            {
                Interlocked.Decrement(ref _currentSize);
            }
        }

        public void Reset()
        {
            while (_pool.TryTake(out _))
            {
            }

            Interlocked.Exchange(ref _currentSize, 0);
        }
    }
}
