using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using Ollang.Values;
using Ollang.VM;

namespace Ollang.GC
{
    public enum GCGeneration { Young, Old }

    public class Allocation
    {
        public IntPtr Pointer { get; }
        public int Size { get; }
        public GCGeneration Generation { get; set; }
        public int SurvivalCount { get; set; }

        public Allocation(IntPtr ptr, int size)
        {
            Pointer = ptr;
            Size = size;
            Generation = GCGeneration.Young;
            SurvivalCount = 0;
        }
    }

    public class GCStats
    {
        public long TotalAllocated { get; set; }
        public int AllocationCount { get; set; }
        public int YoungCount { get; set; }
        public int OldCount { get; set; }
        public int MinorCollections { get; set; }
        public int MajorCollections { get; set; }
        public double LastCollectionMs { get; set; }
        public double TotalCollectionMs { get; set; }
    }

    public class GarbageCollector
    {
        private readonly VirtualMachine _vm;
        private readonly Dictionary<IntPtr, Allocation> _allocations = new();
        private long _bytesAllocated = 0;
        private readonly SortedList<long, IntPtr> _sortedAddresses = new();

        private int _allocsSinceCollect = 0;
        private int _youngThreshold = 128;
        private int _majorThreshold = 512;
        private const int PROMOTION_AGE = 3;
        private const int MIN_THRESHOLD = 32;
        private const int MAX_THRESHOLD = 4096;

        private int _minorCollections = 0;
        private int _majorCollections = 0;
        private double _totalCollectionMs = 0;
        private double _lastCollectionMs = 0;
        private readonly Stopwatch _sw = new();

        public GarbageCollector(VirtualMachine vm) => _vm = vm;

        public IntPtr Allocate(int size)
        {
            if (size <= 0) throw new ArgumentException("Allocation size must be positive");
            if (size > 1024 * 1024 * 256) throw new ArgumentException("Allocation size exceeds 256MB limit");

            IntPtr ptr = Marshal.AllocHGlobal(size);
            _allocations[ptr] = new Allocation(ptr, size);
            _sortedAddresses[(long)ptr] = ptr;
            _bytesAllocated += size;

            _allocsSinceCollect++;
            if (_allocsSinceCollect >= _youngThreshold)
            {
                CollectYoung();
                _allocsSinceCollect = 0;

                if (_allocations.Count(a => a.Value.Generation == GCGeneration.Old) > _majorThreshold)
                    CollectFull();
            }

            return ptr;
        }

        public void Free(IntPtr ptr)
        {
            if (_allocations.TryGetValue(ptr, out var alloc))
            {
                Marshal.FreeHGlobal(ptr);
                _allocations.Remove(ptr);
                _sortedAddresses.Remove((long)ptr);
                _bytesAllocated -= alloc.Size;
            }
        }

        public void CollectYoung()
        {
            _sw.Restart();
            var reachable = new HashSet<IntPtr>();
            MarkRoots(reachable);

            var toFree = new List<IntPtr>();
            foreach (var kvp in _allocations)
            {
                if (kvp.Value.Generation == GCGeneration.Young)
                {
                    if (reachable.Contains(kvp.Key))
                    {
                        kvp.Value.SurvivalCount++;
                        if (kvp.Value.SurvivalCount >= PROMOTION_AGE)
                            kvp.Value.Generation = GCGeneration.Old;
                    }
                    else
                    {
                        toFree.Add(kvp.Key);
                    }
                }
            }

            foreach (var ptr in toFree)
                Free(ptr);

            _sw.Stop();
            _lastCollectionMs = _sw.Elapsed.TotalMilliseconds;
            _totalCollectionMs += _lastCollectionMs;
            _minorCollections++;

            AdaptThresholds(toFree.Count);
        }

        public void CollectFull()
        {
            _sw.Restart();
            var reachable = new HashSet<IntPtr>();
            MarkRoots(reachable);

            var toFree = new List<IntPtr>();
            foreach (var ptr in _allocations.Keys)
                if (!reachable.Contains(ptr))
                    toFree.Add(ptr);

            foreach (var ptr in toFree)
                Free(ptr);

            _sw.Stop();
            _lastCollectionMs = _sw.Elapsed.TotalMilliseconds;
            _totalCollectionMs += _lastCollectionMs;
            _majorCollections++;
        }

        public void Collect() => CollectFull();

        private void AdaptThresholds(int freedCount)
        {
            int liveCount = _allocations.Count;
            double freedRatio = _allocsSinceCollect > 0 ? (double)freedCount / _allocsSinceCollect : 0;

            if (freedRatio < 0.1)
                _youngThreshold = Math.Min(_youngThreshold * 2, MAX_THRESHOLD);
            else if (freedRatio > 0.5)
                _youngThreshold = Math.Max(_youngThreshold / 2, MIN_THRESHOLD);
        }

        private void MarkRoots(HashSet<IntPtr> reachable)
        {
            foreach (var val in _vm.GetGlobals().Values)
                MarkValue(val, reachable);

            foreach (var val in _vm.GetStack())
                MarkValue(val, reachable);

            foreach (var frame in _vm.GetFrames())
                foreach (var local in frame.Locals)
                    if (local != null) MarkValue(local, reachable);
        }

        private void MarkValue(IValue val, HashSet<IntPtr> reachable)
        {
            if (val == null) return;

            if (val is PointerValue pv)
            {
                IntPtr found = FindContainingAllocation(pv.Address);
                if (found != IntPtr.Zero) reachable.Add(found);
            }
            else if (val is ArrayValue av)
            {
                foreach (var elem in av.Elements) MarkValue(elem, reachable);
            }
            else if (val is DictValue dv)
            {
                foreach (var entry in dv.Entries)
                {
                    MarkValue(entry.Key, reachable);
                    MarkValue(entry.Value, reachable);
                }
            }
            else if (val is InstanceValue iv)
            {
                foreach (var field in iv.Fields) MarkValue(field.Value, reachable);
            }
            else if (val is BoundMethodValue bmv)
            {
                MarkValue(bmv.Self, reachable);
            }
        }

        private IntPtr FindContainingAllocation(IntPtr address)
        {
            long ptrVal = (long)address;
            if (_sortedAddresses.Count == 0) return IntPtr.Zero;

            var keys = _sortedAddresses.Keys;
            int lo = 0, hi = keys.Count - 1;
            int candidate = -1;

            while (lo <= hi)
            {
                int mid = lo + (hi - lo) / 2;
                if (keys[mid] <= ptrVal) { candidate = mid; lo = mid + 1; }
                else hi = mid - 1;
            }

            if (candidate >= 0)
            {
                long start = keys[candidate];
                IntPtr allocPtr = _sortedAddresses[start];
                if (_allocations.TryGetValue(allocPtr, out var alloc))
                    if (ptrVal >= start && ptrVal < start + alloc.Size)
                        return allocPtr;
            }

            return IntPtr.Zero;
        }

        public long GetTotalAllocated() => _bytesAllocated;
        public int GetAllocationCount() => _allocations.Count;

        public GCStats GetStats() => new GCStats
        {
            TotalAllocated = _bytesAllocated,
            AllocationCount = _allocations.Count,
            YoungCount = _allocations.Values.Count(a => a.Generation == GCGeneration.Young),
            OldCount = _allocations.Values.Count(a => a.Generation == GCGeneration.Old),
            MinorCollections = _minorCollections,
            MajorCollections = _majorCollections,
            LastCollectionMs = _lastCollectionMs,
            TotalCollectionMs = _totalCollectionMs
        };

        public void Dispose()
        {
            foreach (var ptr in _allocations.Keys.ToList())
                Marshal.FreeHGlobal(ptr);
            _allocations.Clear();
            _sortedAddresses.Clear();
            _bytesAllocated = 0;
        }
    }
}
