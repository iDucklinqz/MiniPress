using System.Runtime.InteropServices;

namespace MiniPress
{
    static class Native
    {
        [DllImport("kernel32.dll")]
        static extern uint SetThreadExecutionState(uint flags);

        /// <summary>Stops the PC from going to sleep while a long encode runs.</summary>
        public static void KeepAwake(bool on)
        {
            const uint Continuous = 0x80000000, SystemRequired = 0x00000001;
            SetThreadExecutionState(on ? Continuous | SystemRequired : Continuous);
        }
    }
}
