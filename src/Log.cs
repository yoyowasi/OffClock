using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security;

namespace OffClock
{
    // Sends Trace output (Build.ps1 defines TRACE) to a small file, so failures on users' PCs leave a record.
    internal sealed class FileLog : TraceListener
    {
        private const long MaxBytes = 256 * 1024;
        private readonly string path;

        private FileLog(string path)
        {
            this.path = path;
        }

        // Keeps one previous file as .old once the log grows past MaxBytes.
        public static void Start(string path)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var current = new FileInfo(path);
                if (current.Exists && current.Length > MaxBytes)
                {
                    File.Delete(path + ".old");
                    File.Move(path, path + ".old");
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (SecurityException) { }
            Trace.Listeners.Add(new FileLog(path));
        }

        public override void Write(string message)
        {
            Append(message);
        }

        public override void WriteLine(string message)
        {
            Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss ", CultureInfo.InvariantCulture) + message +
                Environment.NewLine);
        }

        // A failure repeating for days stops writing at 4 x MaxBytes instead of filling the disk.
        private void Append(string text)
        {
            try
            {
                var current = new FileInfo(path);
                if (current.Exists && current.Length > MaxBytes * 4) return;
                File.AppendAllText(path, text);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (SecurityException) { }
        }
    }
}
