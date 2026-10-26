using System;
using System.IO;
using System.Text;

namespace ChargeGuard
{
    public static class Log
    {
        private static readonly object Gate = new object();
        private static string _filePath = "";

        public static string FilePath
        {
            get { return _filePath; }
            set { _filePath = value; }
        }

        public static void Write(string message)
        {
            if (string.IsNullOrEmpty(_filePath)) return;
            lock (Gate)
            {
                try
                {
                    string dir = Path.GetDirectoryName(_filePath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        Directory.CreateDirectory(dir);

                    if (File.Exists(_filePath))
                    {
                        FileInfo fi = new FileInfo(_filePath);
                        if (fi.Length > 1024 * 1024)
                        {
                            string old = _filePath + ".old";
                            if (File.Exists(old)) File.Delete(old);
                            File.Move(_filePath, old);
                        }
                    }

                    File.AppendAllText(_filePath,
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + Environment.NewLine,
                        Encoding.UTF8);
                }
                catch
                {
                    // Logging must never take the app down.
                }
            }
        }
    }
}
