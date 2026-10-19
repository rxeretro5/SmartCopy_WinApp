using System;
using System.Text;

namespace QuickCopy
{
    /// <summary>
    /// ตัวแปรพิเศษที่จะถูกแทนค่าตอนคัดลอก เช่น {วันที่} → 19 ตุลาคม 2569
    /// </summary>
    public static class Templates
    {
        private static readonly string[] MonthFull =
        {
            "มกราคม", "กุมภาพันธ์", "มีนาคม", "เมษายน", "พฤษภาคม", "มิถุนายน",
            "กรกฎาคม", "สิงหาคม", "กันยายน", "ตุลาคม", "พฤศจิกายน", "ธันวาคม"
        };

        private static readonly string[] MonthShort =
        {
            "ม.ค.", "ก.พ.", "มี.ค.", "เม.ย.", "พ.ค.", "มิ.ย.",
            "ก.ค.", "ส.ค.", "ก.ย.", "ต.ค.", "พ.ย.", "ธ.ค."
        };

        private static readonly string[] DayNames =
        {
            "อาทิตย์", "จันทร์", "อังคาร", "พุธ", "พฤหัสบดี", "ศุกร์", "เสาร์"
        };

        /// <summary>รายการตัวแปร (ชื่อ, คำอธิบาย) สำหรับแสดงในเมนูและหน้าวิธีใช้</summary>
        public static readonly string[][] Variables =
        {
            new[] { "{วันที่}", "วันที่เต็ม เช่น 19 ตุลาคม 2569" },
            new[] { "{วันที่ย่อ}", "วันที่ย่อ เช่น 19 ต.ค. 69" },
            new[] { "{วันที่เลขไทย}", "วันที่เลขไทย เช่น ๑๙ ตุลาคม ๒๕๖๙" },
            new[] { "{วันนี้}", "เช่น วันจันทร์ที่ 19 ตุลาคม 2569" },
            new[] { "{เดือนปี}", "เช่น ตุลาคม 2569" },
            new[] { "{ปี}", "ปี พ.ศ. เช่น 2569" },
            new[] { "{เวลา}", "เวลาปัจจุบัน เช่น 14:30" },
        };

        public static bool HasVariables(string s)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf('{') < 0) return false;
            foreach (var v in Variables)
                if (s.Contains(v[0])) return true;
            return false;
        }

        public static string Expand(string s)
        {
            if (!HasVariables(s)) return s;
            DateTime n = DateTime.Now;
            int be = n.Year + 543;
            string full = n.Day + " " + MonthFull[n.Month - 1] + " " + be;
            var sb = new StringBuilder(s);
            sb.Replace("{วันที่เลขไทย}", ToThaiDigits(full));
            sb.Replace("{วันที่ย่อ}", n.Day + " " + MonthShort[n.Month - 1] + " " + (be % 100).ToString("00"));
            sb.Replace("{วันที่}", full);
            sb.Replace("{วันนี้}", "วัน" + DayNames[(int)n.DayOfWeek] + "ที่ " + full);
            sb.Replace("{เดือนปี}", MonthFull[n.Month - 1] + " " + be);
            sb.Replace("{ปี}", be.ToString());
            sb.Replace("{เวลา}", n.ToString("HH:mm"));
            return sb.ToString();
        }

        public static string ToThaiDigits(string s)
        {
            var chars = s.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
                if (chars[i] >= '0' && chars[i] <= '9')
                    chars[i] = (char)('๐' + (chars[i] - '0'));
            return new string(chars);
        }
    }
}
