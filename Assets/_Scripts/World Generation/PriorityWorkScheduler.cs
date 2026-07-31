using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

internal sealed class PriorityWorkScheduler : IDisposable
{
    private readonly struct WorkItem
    {
        public readonly int priority;
        public readonly int requestId;
        public readonly long sequence;
        public readonly Action<CancellationToken> action;

        public WorkItem(int priority, int requestId, long sequence, Action<CancellationToken> action)
        {
            this.priority = priority;
            this.requestId = requestId;
            this.sequence = sequence;
            this.action = action;
        }
    }

    private readonly object gate = new object();
    private readonly List<WorkItem> heap = new List<WorkItem>();
    private readonly SemaphoreSlim signal = new SemaphoreSlim(0);
    private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
    private readonly Task[] workers;
    private long sequence;

    public PriorityWorkScheduler(int workerCount)
    {
        workers = new Task[Math.Max(1, workerCount)];
        for (int i = 0; i < workers.Length; i++)
            workers[i] = Task.Run(WorkerLoop);
    }

    public int QueuedCount
    {
        get { lock (gate) return heap.Count; }
    }

    public void Enqueue(int priority, Action<CancellationToken> action)
    {
        Enqueue(priority, 0, action);
    }

    public void Enqueue(int priority, int requestId, Action<CancellationToken> action)
    {
        if (action == null || cancellation.IsCancellationRequested)
            return;
        lock (gate)
        {
            Push(new WorkItem(priority, requestId, sequence++, action));
        }
        signal.Release();
    }

    public bool CancelQueued(int requestId)
    {
        if (requestId == 0)
            return false;
        lock (gate)
        {
            for (int i = 0; i < heap.Count; i++)
            {
                if (heap[i].requestId != requestId)
                    continue;
                RemoveAt(i);
                return true;
            }
        }
        return false;
    }

    private async Task WorkerLoop()
    {
        CancellationToken token = cancellation.Token;
        try
        {
            while (true)
            {
                await signal.WaitAsync(token).ConfigureAwait(false);
                WorkItem item;
                lock (gate)
                {
                    if (heap.Count == 0)
                        continue;
                    item = Pop();
                }
                item.action(token);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static bool Before(WorkItem a, WorkItem b)
    {
        return a.priority < b.priority ||
               (a.priority == b.priority && a.sequence < b.sequence);
    }

    private void Push(WorkItem item)
    {
        heap.Add(item);
        int index = heap.Count - 1;
        while (index > 0)
        {
            int parent = (index - 1) / 2;
            if (Before(heap[parent], item))
                break;
            heap[index] = heap[parent];
            index = parent;
        }
        heap[index] = item;
    }

    private WorkItem Pop()
    {
        WorkItem root = heap[0];
        WorkItem last = heap[heap.Count - 1];
        heap.RemoveAt(heap.Count - 1);
        if (heap.Count == 0)
            return root;

        int index = 0;
        while (true)
        {
            int left = index * 2 + 1;
            if (left >= heap.Count)
                break;
            int right = left + 1;
            int child = right < heap.Count && Before(heap[right], heap[left]) ? right : left;
            if (Before(last, heap[child]))
                break;
            heap[index] = heap[child];
            index = child;
        }
        heap[index] = last;
        return root;
    }

    private void RemoveAt(int index)
    {
        int lastIndex = heap.Count - 1;
        WorkItem last = heap[lastIndex];
        heap.RemoveAt(lastIndex);
        if (index == lastIndex)
            return;

        heap[index] = last;
        if (index > 0)
        {
            int parent = (index - 1) / 2;
            if (Before(last, heap[parent]))
            {
                while (index > 0)
                {
                    parent = (index - 1) / 2;
                    if (Before(heap[parent], last))
                        break;
                    heap[index] = heap[parent];
                    index = parent;
                }
                heap[index] = last;
                return;
            }
        }

        while (true)
        {
            int left = index * 2 + 1;
            if (left >= heap.Count)
                break;
            int right = left + 1;
            int child = right < heap.Count && Before(heap[right], heap[left]) ? right : left;
            if (Before(last, heap[child]))
                break;
            heap[index] = heap[child];
            index = child;
        }
        heap[index] = last;
    }

    public void Dispose()
    {
        if (cancellation.IsCancellationRequested)
            return;
        cancellation.Cancel();
    }
}
