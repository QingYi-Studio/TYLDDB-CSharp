#if NET8_0_OR_GREATER
using System.Collections.Generic;
using System;
using System.Linq;
using TYLDDB.Basic.Exception;
using System.Threading;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace TYLDDB.Basic
{
    /// <summary>
    /// Three-value dictionary.<br />
    /// 三值字典。
    /// </summary>
    /// <typeparam name="TValue">The data type of the value.<br />值的数据类型。</typeparam>
    public class TripleDictionary<TValue> : IDisposable
    {
        private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);
        private readonly Dictionary<Tuple<string, string>, TValue> _dictionary;
        private readonly int? _capacity;
        private readonly ConcurrentDictionary<Tuple<string, string>, TValue> _cache;

        public event EventHandler<TripleDictionaryChangedEventArgs<TValue>> ItemAdded;
        public event EventHandler<TripleDictionaryChangedEventArgs<TValue>> ItemRemoved;
        public event EventHandler<TripleDictionaryChangedEventArgs<TValue>> ItemUpdated;

        /// <summary>
        /// Three-value dictionary.<br />
        /// 三值字典。
        /// </summary>
        public TripleDictionary(int? capacity = null)
        {
            _capacity = capacity;
            _dictionary = new Dictionary<Tuple<string, string>, TValue>();
            _cache = new ConcurrentDictionary<Tuple<string, string>, TValue>();
        }

        /// <summary>
        /// Add an element, only if the key for the combination does not exist.<br />
        /// 添加元素，只有当该组合的键不存在时才添加。
        /// </summary>
        /// <param name="type">Data type.<br />数据类型。</param>
        /// <param name="key">Key.<br />键。</param>
        /// <param name="value">Value.<br />值。</param>
        /// <returns>Whether the value is added successfully.<br />是否成功添加。</returns>
        public bool Add(string type, string key, TValue value)
        {
            if (_capacity.HasValue && _dictionary.Count >= _capacity.Value)
            {
                throw new TripleDictionaryException("Dictionary capacity exceeded");
            }

            var keyTuple = new Tuple<string, string>(type, key);

            try
            {
                _lock.EnterWriteLock();

                if (_dictionary.ContainsKey(keyTuple))
                {
                    return false;
                }

                _dictionary[keyTuple] = value;
                _cache.TryAdd(keyTuple, value);

                OnItemAdded(type, key, value);
                return true;
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public async Task<bool> AddAsync(string type, string key, TValue value)
        {
            return await Task.Run(() => Add(type, key, value));
        }

        public async Task<bool> UpdateValueAsync(string type, string key, TValue newValue)
        {
            return await Task.Run(() => UpdateValue(type, key, newValue));
        }

        public async Task<TValue> GetAsync(string type, string key)
        {
            return await Task.Run(() => Get(type, key));
        }

        /// <summary>
        /// Only values are updated, types and keys cannot be updated.<br />
        /// 只更新值，不能更新类型和键。
        /// </summary>
        /// <param name="type">Data type.<br />数据类型。</param>
        /// <param name="key">Key.<br />键。</param>
        /// <param name="newValue">New value.<br />新值。</param>
        /// <returns>Whether the update is successful.<br />是否成功更新。</returns>
        public bool UpdateValue(string type, string key, TValue newValue)
        {
            lock (_lock)
            {
                var keyTuple = new Tuple<string, string>(type, key);

                // 检查该键组合是否存在
                if (!_dictionary.ContainsKey(keyTuple))
                {
                    return false; // 如果不存在，返回 false
                }

                // 如果存在，更新值
                _dictionary[keyTuple] = newValue;
                return true; // 更新成功
            }
        }

        /// <summary>
        /// Get the element.<br />
        /// 获取元素。
        /// </summary>
        /// <param name="type">Data type.<br />数据类型。</param>
        /// <param name="key">Key.<br />键。</param>
        /// <returns>Value.<br />值。</returns>
        /// <exception cref="TripleDictionaryKeyNotFoundException">The specified key was not found.<br />未找到指定的键。</exception>
        public TValue Get(string type, string key)
        {
            var keyTuple = new Tuple<string, string>(type, key);

            if (_cache.TryGetValue(keyTuple, out TValue cachedValue))
            {
                return cachedValue;
            }

            try
            {
                _lock.EnterReadLock();

                if (_dictionary.TryGetValue(keyTuple, out TValue value))
                {
                    _cache.TryAdd(keyTuple, value);
                    return value;
                }

                throw new TripleDictionaryKeyNotFoundException("The key combination was not found.");
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        public void AddRange(IEnumerable<(string type, string key, TValue value)> items)
        {
            try
            {
                _lock.EnterWriteLock();

                foreach (var (type, key, value) in items)
                {
                    if (_capacity.HasValue && _dictionary.Count >= _capacity.Value)
                    {
                        throw new TripleDictionaryException("Dictionary capacity exceeded");
                    }

                    var keyTuple = new Tuple<string, string>(type, key);
                    if (!_dictionary.ContainsKey(keyTuple))
                    {
                        _dictionary[keyTuple] = value;
                        _cache.TryAdd(keyTuple, value);
                        OnItemAdded(type, key, value);
                    }
                }
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        protected virtual void OnItemAdded(string type, string key, TValue value)
        {
            ItemAdded?.Invoke(this, new TripleDictionaryChangedEventArgs<TValue>(type, key, value, ChangeType.Added));
        }

        protected virtual void OnItemRemoved(string type, string key, TValue value)
        {
            ItemRemoved?.Invoke(this, new TripleDictionaryChangedEventArgs<TValue>(type, key, value, ChangeType.Removed));
        }

        protected virtual void OnItemUpdated(string type, string key, TValue value)
        {
            ItemUpdated?.Invoke(this, new TripleDictionaryChangedEventArgs<TValue>(type, key, value, ChangeType.Updated));
        }

        public void Dispose()
        {
            _lock.Dispose();
            _cache.Clear();
            _dictionary.Clear();
        }

        public void ClearCache()
        {
            _cache.Clear();
        }

        public void InvalidateCache(string type, string key)
        {
            var keyTuple = new Tuple<string, string>(type, key);
            _cache.TryRemove(keyTuple, out _);
        }

        public IEnumerable<TValue> GetValuesByType(string type)
        {
            try
            {
                _lock.EnterReadLock();
                return _dictionary.Where(x => x.Key.Item1 == type)
                                .Select(x => x.Value)
                                .ToList();
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        public IEnumerable<string> GetKeysByType(string type)
        {
            try
            {
                _lock.EnterReadLock();
                return _dictionary.Where(x => x.Key.Item1 == type)
                                .Select(x => x.Key.Item2)
                                .ToList();
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        public int CacheSize => _cache.Count;
        public bool IsCacheEnabled => true;
        public int Capacity => _capacity ?? -1;

        public Dictionary<string, int> GetTypeStatistics()
        {
            try
            {
                _lock.EnterReadLock();
                return _dictionary.GroupBy(x => x.Key.Item1)
                                .ToDictionary(g => g.Key, g => g.Count());
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        /// <summary>
        /// Checks whether the specified type and key combination are included.<br />
        /// 检查是否包含指定的类型和键组合。
        /// </summary>
        /// <param name="type">Data type.<br />数据类型。</param>
        /// <param name="key">Key.<br />键。</param>
        /// <returns>Whether the key is included.<br />是否包含该键。</returns>
        public bool ContainsKey(string type, string key)
        {
            lock (_lock)
            {
                return _dictionary.ContainsKey(new Tuple<string, string>(type, key));
            }
        }

        /// <summary>
        /// Deleting a certain type removes all the key values of the class.<br />
        /// 删除某个类型会删除该类型下的所有键值对。
        /// </summary>
        /// <param name="type">Data type.<br />数据类型。</param>
        /// <returns>Whether the data type is removed successfully.<br />是否成功移除该数据类型。</returns>
        public bool RemoveType(string type)
        {
            lock (_lock)
            {
                var keysToRemove = _dictionary.Keys.Where(k => k.Item1 == type).ToList();

                if (keysToRemove.Count == 0)
                {
                    return false; // 如果该类型没有键值对，返回 false
                }

                foreach (var key in keysToRemove)
                {
                    _dictionary.Remove(key, out _);
                }
                return true;
            }
        }

        /// <summary>
        /// Deletes the specified key and corresponding value from the specified type.<br />
        /// 删除指定类型中的指定键和对应的值。
        /// </summary>
        /// <param name="type">Data type.<br />数据类型。</param>
        /// <param name="key">Key.<br />键。</param>
        /// <returns>Whether the key is successfully removed.<br />是否成功移除该键。</returns>
        public bool RemoveKey(string type, string key)
        {
            lock (_lock)
            {
                var keyTuple = new Tuple<string, string>(type, key);

                // 尝试移除指定的键值对
                if (_dictionary.Remove(keyTuple, out _))
                {
                    return true; // 删除成功
                }
                else
                {
                    return false; // 如果没有找到该键组合，返回 false
                }
            }
        }

        /// <summary>
        /// Removes the specified key and corresponding value from all types.<br />
        /// 删除所有类型中的指定键和对应的值。
        /// </summary>
        /// <param name="key">Key.<br />键。</param>
        /// <returns>Whether the key is successfully removed.<br />是否成功移除该键。</returns>
        public bool RemoveKey(string key)
        {
            lock (_lock)
            {
                bool removed = false;

                // 查找所有包含该 Key 的项，并删除它们
                var keysToRemove = _dictionary.Where(entry => entry.Key.Item2 == key)
                                            .Select(entry => entry.Key)
                                            .ToList();

                foreach (var keyTuple in keysToRemove)
                {
                    if (_dictionary.Remove(keyTuple, out _))
                    {
                        removed = true;
                    }
                }

                return removed; // 如果至少删除了一个键值对，返回 true
            }
        }

        /// <summary>
        /// Prints all types of key-value pairs.<br />
        /// 打印所有类型的键值对。
        /// </summary>
        public void PrintAll()
        {
            lock (_lock)
            {
                foreach (var entry in _dictionary)
                {
                    Console.WriteLine($"Type: {entry.Key.Item1}, Key: {entry.Key.Item2} -> Value: {entry.Value}");
                }
            }
        }
    }

    public class TripleDictionaryChangedEventArgs<T> : EventArgs
    {
        public string Type { get; }
        public string Key { get; }
        public T Value { get; }
        public ChangeType ChangeType { get; }

        public TripleDictionaryChangedEventArgs(string type, string key, T value, ChangeType changeType)
        {
            Type = type;
            Key = key;
            Value = value;
            ChangeType = changeType;
        }
    }

    public enum ChangeType
    {
        Added,
        Removed,
        Updated
    }
}
#endif