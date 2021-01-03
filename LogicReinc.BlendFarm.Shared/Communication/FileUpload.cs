using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace LogicReinc.BlendFarm.Shared.Communication
{
    /// <summary>
    /// Used to communicate a file upload over different packets
    /// </summary>
    public class FileUpload : IDisposable
    {
        public string TargetPath { get; set; }
        public FileStream Stream { get; private set; }

        public object Context { get; set; }

        public FileUpload(string path, object context = null)
        {
            Context = context;
            Stream = new FileStream(path, FileMode.Create);
        }

        public T GetContext<T>()
        {
            return (T)Context;
        }

        public void WriteBase64(string base64)
        {
            byte[] data = Convert.FromBase64String(base64);
            Write(data, 0, data.Length);
        }
        public void Write(byte[] bytes, int offset, int length)
        {
            Stream.Write(bytes, offset, length);
        }

        public void Dispose()
        {
            Stream.Flush();
            Stream.Dispose();
        }
    }
}
