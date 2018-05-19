using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace MTProtoProxy
{
    public static class MemoryManager
    {
        private static Thread _thread;

        [DllImport("psapi.dll")]
        public static extern bool EmptyWorkingSet(IntPtr hProcess);
        public static void EmptyWorking()
        {
            while (true)
            {
                try
                {
                    EmptyWorkingSet(Process.GetCurrentProcess().Handle);

                }
                catch
                {
                    //
                }
                Thread.Sleep(5 * 60 * 1000);
            }
        }
        public static void Start()
        {
            if (_thread == null)
            {
                _thread = new Thread(EmptyWorking);
                _thread.Start();
            }
        }
        public static void Stop()
        {
            try
            {
                if (_thread != null)
                {
                    _thread.Abort();
                }
            }
            catch
            {
            }
            _thread = null;
        }
    }
}