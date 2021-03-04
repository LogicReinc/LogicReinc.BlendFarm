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

        public ICompressionHandler CompressionHandler { get; set; }
        public Compression Compression { get; set; }

        public FileUpload(string path, object context = null, Compression compression = Compression.Raw)
        {
            Context = context;
            Compression = compression;
            Stream = new FileStream(path, FileMode.Create);
            CompressionHandler = GetCompressionStream(compression);
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
            CompressionHandler.Write(bytes, offset, length, Stream);
        }

        public void FinalWrite()
        {
            CompressionHandler.FinalWrite(Stream);
        }

        private ICompressionHandler GetCompressionStream(Compression compression)
        {
            switch (compression)
            {
                case Compression.Raw:
                    return new RawCompressionHandler();
                case Compression.GZip:
                    return new GZipCompressionHandler();
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

    public interface ICompressionHandler
    {
        void Write(byte[] bytes, int offset, int length, Stream target);

        void FinalWrite(Stream stream);
    }

    public class RawCompressionHandler : ICompressionHandler
    {
        public void Write(byte[] bytes, int offset, int length, Stream target)
        {
            target.Write(bytes, offset, length);
        }

        public void FinalWrite(Stream stream)
        {
        }
    }
    public class GZipCompressionHandler : ICompressionHandler
    {
        private byte[] _buffer = new byte[4096];
        private int _read = 0;
        private MemoryStream _stream = new MemoryStream();

        public void Write(byte[] bytes, int offset, int length, Stream target)
        {
            _stream.Write(bytes, offset, length);
        }

        public void FinalWrite(Stream stream)
        {
            _stream.Seek(0, SeekOrigin.Begin);
            using(GZipStream str = new GZipStream(_stream, CompressionMode.Decompress))
            {
                while ((_read = str.Read(_buffer, 0, _buffer.Length)) > 0)
                    stream.Write(_buffer, 0, _read);
            }
        }
    }
}
