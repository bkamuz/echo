using Microsoft.Extensions.Logging;

namespace echo.Engines.ParakeetNpu.Ort;

/// <summary>
/// Runs QNN/ORT native calls on a dedicated MTA thread so COM and HTP probing stay off the UI thread.
/// </summary>
internal static class QnnNativeWorker
{
    private static readonly object Gate = new();
    private static Thread? _worker;
    private static BlockingQueue? _queue;

    public static T Run<T>(Func<T> work, ILogger? logger = null)
    {
        EnsureWorker(logger);
        var item = new WorkItem<T>(work);
        _queue!.Enqueue(item);
        return item.Wait();
    }

    public static void Run(Action work, ILogger? logger = null) =>
        Run(() =>
        {
            work();
            return 0;
        }, logger);

    private static void EnsureWorker(ILogger? logger)
    {
        lock (Gate)
        {
            if (_worker is not null)
            {
                return;
            }

            _queue = new BlockingQueue();
            _worker = new Thread(WorkerLoop)
            {
                IsBackground = true,
                Name = "echo-qnn-native",
            };
            _worker.SetApartmentState(ApartmentState.MTA);
            _worker.Start(logger);
        }
    }

    private static void WorkerLoop(object? state)
    {
        var logger = state as ILogger;
        ComApartmentHelper.EnsureInitialized(logger);

        while (true)
        {
            _queue!.Dequeue().Execute();
        }
    }

    private sealed class BlockingQueue
    {
        private readonly Queue<IWorkItem> _items = new();
        private readonly Lock _lock = new();

        public void Enqueue(IWorkItem item)
        {
            lock (_lock)
            {
                _items.Enqueue(item);
                Monitor.Pulse(_lock);
            }
        }

        public IWorkItem Dequeue()
        {
            lock (_lock)
            {
                while (_items.Count == 0)
                {
                    Monitor.Wait(_lock);
                }

                return _items.Dequeue();
            }
        }
    }

    private interface IWorkItem
    {
        void Execute();
    }

    private sealed class WorkItem<T> : IWorkItem
    {
        private readonly Func<T> _work;
        private readonly ManualResetEventSlim _done = new(false);
        private T _result = default!;
        private Exception? _error;

        public WorkItem(Func<T> work) => _work = work;

        public T Wait()
        {
            _done.Wait();
            if (_error is not null)
            {
                throw new InvalidOperationException("QNN native worker failed.", _error);
            }

            return _result;
        }

        public void Execute()
        {
            try
            {
                _result = _work();
            }
            catch (Exception ex)
            {
                _error = ex;
            }
            finally
            {
                _done.Set();
            }
        }
    }

}
