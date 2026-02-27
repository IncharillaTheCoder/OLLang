using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ollang.Values;

namespace Ollang.Async
{
    public class Channel
    {
        private readonly BlockingCollection<IValue> _queue;
        private readonly int _capacity;

        public Channel(int capacity = 0)
        {
            _capacity = capacity;
            _queue = capacity > 0
                ? new BlockingCollection<IValue>(capacity)
                : new BlockingCollection<IValue>();
        }

        public void Send(IValue value) => _queue.Add(value);

        public bool TrySend(IValue value, int timeoutMs = 0)
        {
            if (timeoutMs <= 0) return _queue.TryAdd(value);
            return _queue.TryAdd(value, timeoutMs);
        }

        public IValue Receive() => _queue.Take();

        public IValue? TryReceive(int timeoutMs = 0)
        {
            if (timeoutMs <= 0)
                return _queue.TryTake(out var val) ? val : null;
            return _queue.TryTake(out var val2, timeoutMs) ? val2 : null;
        }

        public void Close() => _queue.CompleteAdding();
        public bool IsClosed => _queue.IsAddingCompleted;
        public int Count => _queue.Count;
    }

    public class ChannelValue : IValue
    {
        public Channel Channel { get; }
        public ChannelValue(Channel channel) => Channel = channel;
        public double AsNumber() => Channel.Count;
        public bool AsBool() => !Channel.IsClosed;
        public override string ToString() => $"<channel:{Channel.Count}>";
        public IValue GetIndex(IValue index) => throw new Exception("Cannot index a channel");
        public void SetIndex(IValue index, IValue value) => throw new Exception("Cannot index a channel");
        public IValue GetOffset() => new NumberValue(0);
    }

    public class TaskValue : IValue
    {
        public Task<IValue> Task { get; }
        public TaskValue(Task<IValue> task) => Task = task;
        public double AsNumber() => Task.IsCompleted ? 1 : 0;
        public bool AsBool() => Task.IsCompleted;
        public override string ToString() => $"<task:{Task.Status}>";
        public IValue GetIndex(IValue index) => throw new Exception("Cannot index a task");
        public void SetIndex(IValue index, IValue value) => throw new Exception("Cannot index a task");
        public IValue GetOffset() => new NumberValue(0);
    }

    public class MutexValue : IValue
    {
        private readonly SemaphoreSlim _semaphore = new(1, 1);
        public void Lock() => _semaphore.Wait();
        public void Unlock() => _semaphore.Release();
        public bool TryLock(int timeoutMs = 0) => _semaphore.Wait(timeoutMs);
        public double AsNumber() => _semaphore.CurrentCount;
        public bool AsBool() => _semaphore.CurrentCount > 0;
        public override string ToString() => $"<mutex:{(_semaphore.CurrentCount > 0 ? "unlocked" : "locked")}>";
        public IValue GetIndex(IValue index) => throw new Exception("Cannot index a mutex");
        public void SetIndex(IValue index, IValue value) => throw new Exception("Cannot index a mutex");
        public IValue GetOffset() => new NumberValue(0);
    }

    public class WaitGroupValue : IValue
    {
        private readonly CountdownEvent _event;
        public WaitGroupValue(int count) => _event = new CountdownEvent(count);
        public void Done() => _event.Signal();
        public void Wait() => _event.Wait();
        public bool WaitTimeout(int timeoutMs) => _event.Wait(timeoutMs);
        public void Add(int count) => _event.AddCount(count);
        public double AsNumber() => _event.CurrentCount;
        public bool AsBool() => _event.CurrentCount > 0;
        public override string ToString() => $"<waitgroup:{_event.CurrentCount}>";
        public IValue GetIndex(IValue index) => throw new Exception("Cannot index a waitgroup");
        public void SetIndex(IValue index, IValue value) => throw new Exception("Cannot index a waitgroup");
        public IValue GetOffset() => new NumberValue(0);
    }
}
