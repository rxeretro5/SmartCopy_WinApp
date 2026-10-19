using System;
using System.Drawing;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("แผงข้อความด่วน (QuickCopy Panel)")]
[assembly: AssemblyProduct("QuickCopy Panel")]
[assembly: AssemblyDescription("แผงเก็บข้อความที่ใช้บ่อย คลิกเพื่อคัดลอก")]
[assembly: AssemblyVersion("1.0.1.0")]
[assembly: AssemblyFileVersion("1.0.1.0")]

namespace QuickCopy
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            bool createdNew;
            using (var mutex = new Mutex(true, "QuickCopyPanel_SingleInstance_v1", out createdNew))
            {
                if (!createdNew)
                {
                    // เปิดซ้ำ → แค่สั่งให้ตัวที่เปิดอยู่แสดงหน้าต่าง
                    uint msg = Native.RegisterWindowMessage("QuickCopyPanel_Show_v1");
                    Native.PostMessage(Native.HWND_BROADCAST, msg, IntPtr.Zero, IntPtr.Zero);
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                using (var g = Graphics.FromHwnd(IntPtr.Zero))
                    Theme.Scale = g.DpiX / 96f;

                Application.ThreadException += (s, e) =>
                    MessageBox.Show("เกิดข้อผิดพลาด: " + e.Exception.Message, MainForm.AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);

                Application.Run(new MainForm());
                GC.KeepAlive(mutex);
            }
        }
    }
}
