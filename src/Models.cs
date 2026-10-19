using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Xml;

namespace QuickCopy
{
    [DataContract]
    public class Snippet
    {
        [DataMember(Order = 1)] public string Title;
        [DataMember(Order = 2)] public string Text;

        public Snippet() { }
        public Snippet(string title, string text) { Title = title; Text = text; }

        public string DisplayTitle
        {
            get { return string.IsNullOrEmpty(Title) ? null : Title.Trim(); }
        }

        public Snippet Clone()
        {
            return new Snippet(Title, Text);
        }
    }

    [DataContract]
    public class SnippetGroup
    {
        [DataMember(Order = 1)] public string Name;
        [DataMember(Order = 2)] public List<Snippet> Items;

        public SnippetGroup() { Items = new List<Snippet>(); }
        public SnippetGroup(string name) : this() { Name = name; }
    }

    [DataContract]
    public class AppSettings
    {
        [DataMember] public bool TopMost;
        [DataMember] public bool AutoPaste;
        [DataMember] public bool HideAfterCopy;
        [DataMember] public int OpacityPercent;
        [DataMember] public float FontSize;
        [DataMember] public int WinX;
        [DataMember] public int WinY;
        [DataMember] public int WinW;
        [DataMember] public int WinH;
        [DataMember] public int CurrentGroup;
        [DataMember] public bool TrayTipShown;

        public AppSettings() { SetDefaults(); }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext c) { SetDefaults(); }

        private void SetDefaults()
        {
            TopMost = true;
            AutoPaste = false;
            HideAfterCopy = false;
            OpacityPercent = 100;
            FontSize = 10f;
            WinX = WinY = WinW = WinH = -1;
            CurrentGroup = 0;
            TrayTipShown = false;
        }

        public bool HasBounds { get { return WinW > 0 && WinH > 0; } }
    }

    [DataContract]
    public class AppData
    {
        [DataMember(Order = 1)] public int Version;
        [DataMember(Order = 2)] public AppSettings Settings;
        [DataMember(Order = 3)] public List<SnippetGroup> Groups;

        public AppData()
        {
            Version = 1;
            Settings = new AppSettings();
            Groups = new List<SnippetGroup>();
        }

        /// <summary>ซ่อมข้อมูลที่ขาดหาย (เช่น ไฟล์เก่า/แก้ไขด้วยมือ) ให้ใช้งานได้เสมอ</summary>
        public void Normalize()
        {
            if (Settings == null) Settings = new AppSettings();
            if (Groups == null) Groups = new List<SnippetGroup>();
            Groups.RemoveAll(g => g == null);
            foreach (var g in Groups)
            {
                if (string.IsNullOrWhiteSpace(g.Name)) g.Name = "ไม่มีชื่อ";
                if (g.Items == null) g.Items = new List<Snippet>();
                g.Items.RemoveAll(s => s == null || s.Text == null);
            }
            if (Groups.Count == 0) Groups.Add(new SnippetGroup("ทั่วไป"));
            if (Settings.CurrentGroup < 0 || Settings.CurrentGroup >= Groups.Count) Settings.CurrentGroup = 0;
            if (Settings.OpacityPercent < 30 || Settings.OpacityPercent > 100) Settings.OpacityPercent = 100;
            if (Settings.FontSize < 8f || Settings.FontSize > 20f) Settings.FontSize = 10f;
        }

        public static AppData CreateSample()
        {
            var d = new AppData();
            var g = new SnippetGroup("ทั่วไป");
            g.Items.Add(new Snippet(null, "จึงเรียนมาเพื่อโปรดทราบ"));
            g.Items.Add(new Snippet(null, "จึงเรียนมาเพื่อโปรดพิจารณา"));
            g.Items.Add(new Snippet(null, "จึงเรียนมาเพื่อโปรดทราบและพิจารณาดำเนินการต่อไปด้วย จะเป็นพระคุณ"));
            g.Items.Add(new Snippet(null, "ทราบ และดำเนินการตามเสนอ"));
            g.Items.Add(new Snippet(null, "เพื่อโปรดทราบและแจ้งผู้เกี่ยวข้องทราบ"));
            g.Items.Add(new Snippet("ลงวันที่วันนี้", "{วันที่}"));
            g.Items.Add(new Snippet("ตัวอย่างหลายบรรทัด", "ขอแสดงความนับถือ\r\n\r\n\r\n(                              )\r\nตำแหน่ง ......................"));
            d.Groups.Add(g);
            d.Groups.Add(new SnippetGroup("หนังสือราชการ"));
            d.Groups.Add(new SnippetGroup("ที่อยู่ / เบอร์โทร"));
            return d;
        }
    }

    /// <summary>
    /// อ่าน/บันทึกข้อมูลเป็นไฟล์ JSON (UTF-8)
    /// ค่าเริ่มต้นเก็บที่ %AppData%\QuickCopyPanel\data.json
    /// ถ้ามีไฟล์ portable.txt อยู่ข้างไฟล์ .exe จะเก็บข้อมูลไว้ข้าง .exe แทน (พกพาใส่ USB ได้)
    /// </summary>
    public static class Store
    {
        public const string FileName = "data.json";

        private static string _folder;

        public static string Folder
        {
            get
            {
                if (_folder == null)
                {
                    string exeDir = AppDomain.CurrentDomain.BaseDirectory;
                    if (File.Exists(Path.Combine(exeDir, "portable.txt")))
                        _folder = exeDir;
                    else
                        _folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "QuickCopyPanel");
                }
                return _folder;
            }
        }

        public static string DataPath { get { return Path.Combine(Folder, FileName); } }

        public static AppData Load()
        {
            string path = DataPath;
            AppData data = null;
            if (File.Exists(path))
            {
                data = TryRead(path);
                if (data == null && File.Exists(path + ".bak"))
                    data = TryRead(path + ".bak");
            }
            if (data == null) data = AppData.CreateSample();
            data.Normalize();
            return data;
        }

        public static AppData TryRead(string path)
        {
            try
            {
                using (var fs = File.OpenRead(path))
                {
                    var ser = new DataContractJsonSerializer(typeof(AppData));
                    return (AppData)ser.ReadObject(fs);
                }
            }
            catch
            {
                return null;
            }
        }

        public static void Save(AppData data)
        {
            Directory.CreateDirectory(Folder);
            string path = DataPath;
            string tmp = path + ".tmp";
            WriteTo(tmp, data);
            if (File.Exists(path))
            {
                try
                {
                    File.Replace(tmp, path, path + ".bak");
                    return;
                }
                catch
                {
                    File.Copy(path, path + ".bak", true);
                    File.Delete(path);
                }
            }
            File.Move(tmp, path);
        }

        public static void WriteTo(string path, AppData data)
        {
            using (var fs = File.Create(path))
            using (var writer = JsonReaderWriterFactory.CreateJsonWriter(fs, new UTF8Encoding(false), false, true, "  "))
            {
                var ser = new DataContractJsonSerializer(typeof(AppData));
                ser.WriteObject(writer, data);
                writer.Flush();
            }
        }
    }
}
