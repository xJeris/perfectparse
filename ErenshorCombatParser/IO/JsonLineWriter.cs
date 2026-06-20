using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;

namespace ErenshorCombatParser.IO
{
    /// <summary>
    /// Thread-safe JSONL file writer. Events are enqueued from the game thread
    /// and written to disk by a dedicated background thread. Never blocks the
    /// game thread on I/O.
    /// </summary>
    public class JsonLineWriter : IDisposable
    {
        private readonly ConcurrentQueue<string> _queue = new ConcurrentQueue<string>();
        private readonly Thread _writerThread;
        private readonly ManualResetEvent _signal = new ManualResetEvent(false);
        private volatile bool _running = true;
        private volatile bool _disposed;
        private string _filePath;

        public string FilePath => _filePath;

        public JsonLineWriter(string filePath)
        {
            _filePath = filePath;
            _writerThread = new Thread(WriterLoop)
            {
                IsBackground = true,
                Name = "CombatParserWriter"
            };
            _writerThread.Start();
        }

        public void Enqueue(string jsonLine)
        {
            _queue.Enqueue(jsonLine);
            _signal.Set();
        }

        /// <summary>
        /// Forces the background thread to flush all queued events to disk immediately.
        /// Blocks until the flush is complete (up to 5 seconds).
        /// </summary>
        public void FlushSync()
        {
            if (_disposed) return;
            _flushDone.Reset();
            _flushRequested.Set();
            _signal.Set();
            _flushDone.WaitOne(5000);
        }

        private readonly ManualResetEvent _flushRequested = new ManualResetEvent(false);
        private readonly ManualResetEvent _flushDone = new ManualResetEvent(false);

        /// <summary>
        /// Rotates to a new JSONL file. Flushes all queued events to the current
        /// file first, then closes it and opens the new file. Blocks until complete
        /// (up to 5 seconds).
        /// </summary>
        public void Rotate(string newFilePath)
        {
            if (_disposed) return;
            _pendingRotatePath = newFilePath;
            _rotateDone.Reset();
            _rotateRequested.Set();
            _signal.Set();
            _rotateDone.WaitOne(5000);
        }

        private volatile string _pendingRotatePath;
        private readonly ManualResetEvent _rotateRequested = new ManualResetEvent(false);
        private readonly ManualResetEvent _rotateDone = new ManualResetEvent(false);

        private void WriterLoop()
        {
            try
            {
                var fs = new FileStream(_filePath, FileMode.Append, FileAccess.Write, FileShare.Read);
                var writer = new StreamWriter(fs, Encoding.UTF8);
                try
                {
                    while (_running || !_queue.IsEmpty)
                    {
                        _signal.WaitOne(2000);
                        _signal.Reset();

                        bool flushing = _flushRequested.WaitOne(0);
                        if (flushing) _flushRequested.Reset();

                        int count = 0;
                        int limit = flushing ? int.MaxValue : 500;
                        while (_queue.TryDequeue(out string line) && count < limit)
                        {
                            writer.WriteLine(line);
                            count++;
                        }

                        if (count > 0)
                            writer.Flush();

                        if (flushing)
                        {
                            _flushDone.Set();
                        }

                        if (_rotateRequested.WaitOne(0))
                        {
                            _rotateRequested.Reset();

                            // Drain any remaining queued lines to the current file
                            while (_queue.TryDequeue(out string remaining))
                                writer.WriteLine(remaining);
                            writer.Flush();

                            // Close current file and open new one
                            string newPath = _pendingRotatePath;
                            try
                            {
                                writer.Dispose();
                                fs = new FileStream(newPath, FileMode.Append, FileAccess.Write, FileShare.Read);
                                writer = new StreamWriter(fs, Encoding.UTF8);
                                _filePath = newPath;
                            }
                            catch (Exception rotEx)
                            {
                                // Re-open the original file so writing can continue
                                System.Diagnostics.Debug.WriteLine(
                                    "[CombatParser] Rotate failed, keeping current file: " + rotEx.Message);
                                fs = new FileStream(_filePath, FileMode.Append, FileAccess.Write, FileShare.Read);
                                writer = new StreamWriter(fs, Encoding.UTF8);
                            }

                            _rotateDone.Set();
                        }
                    }
                }
                finally
                {
                    writer.Dispose();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    "[CombatParser] Writer error: " + ex.Message);
            }
        }

        public void Dispose()
        {
            _running = false;
            _signal.Set();
            _writerThread.Join(5000);
            _disposed = true;
            _signal.Dispose();
            _flushRequested.Dispose();
            _flushDone.Dispose();
            _rotateRequested.Dispose();
            _rotateDone.Dispose();
        }
    }
}
