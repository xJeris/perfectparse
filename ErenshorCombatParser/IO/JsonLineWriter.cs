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
        private readonly string _filePath;

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
            _flushRequested.Set();
            _signal.Set();
            _flushDone.WaitOne(5000);
        }

        private readonly ManualResetEvent _flushRequested = new ManualResetEvent(false);
        private readonly ManualResetEvent _flushDone = new ManualResetEvent(false);

        private void WriterLoop()
        {
            try
            {
                var fs = new FileStream(_filePath, FileMode.Append, FileAccess.Write, FileShare.Read);
                using (var writer = new StreamWriter(fs, Encoding.UTF8))
                {
                    while (_running || !_queue.IsEmpty)
                    {
                        _signal.WaitOne(2000);
                        _signal.Reset();

                        int count = 0;
                        while (_queue.TryDequeue(out string line) && count < 500)
                        {
                            writer.WriteLine(line);
                            count++;
                        }

                        if (count > 0)
                            writer.Flush();

                        if (_flushRequested.WaitOne(0))
                        {
                            _flushRequested.Reset();
                            _flushDone.Set();
                        }
                    }
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
            _signal.Dispose();
            _flushRequested.Dispose();
            _flushDone.Dispose();
        }
    }
}
