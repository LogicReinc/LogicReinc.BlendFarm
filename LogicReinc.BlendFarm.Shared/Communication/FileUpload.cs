using LogicReinc.BlendFarm.Shared.Communication.RenderNode;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace LogicReinc.BlendFarm.Shared.Communication
{
    /// <summary>
    /// Used to communicate a file upload over different packets
    /// </summary>
    public class FileUpload : IDisposable
    {
        public string TargetPath { get; set; }
        public Stream Stream { get; private set; }

        public object Context { get; set; }

        public FileUpload(string path, object context = null, Compression compression = Compression.Raw)
        {
            Context = context;
            Stream = new FileStream(path, FileMode.Create);
            Stream = GetCompressionStream(Stream, compression);
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


        private Stream GetCompressionStream(Stream stream, Compression compression)
        {
            switch (compression)
            {
                case Compression.Raw:
                    return stream;
                case Compression.GZip:
                    return new GZipStream(stream, CompressionMode.Decompress);
                default:
                    throw new NotImplementedException();
            }
        }

        public void Dispose()
        {
            Stream.Flush();
            Stream.Dispose();
        }
    }
}
