using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Driscod.Tracking
{
    public interface IMessageAttachment
    {
        string FileName { get; }

        byte[] Content { get; }
    }

    public class FileAttachment : IMessageAttachment
    {
        private readonly string _path;

        public string FileName => Path.GetFileName(_path);

        public byte[] Content => File.ReadAllBytes(_path);

        public FileAttachment(string path)
        {
            _path = path;
        }
    }

    public class ByteStreamAttachment : IMessageAttachment
    {
        private readonly Stream _stream;

        private byte[]? _content;

        public string FileName { get; private set; }

        public byte[] Content
        {
            get
            {
                if (_content == null)
                {
                    var bytes = new List<byte>();
                    var buffer = new byte[1024];

                    while (true)
                    {
                        var bytesRead = _stream.Read(buffer, 0, buffer.Length);

                        if (bytesRead < buffer.Length)
                        {
                            bytes.AddRange(buffer.Take(bytesRead));
                            break;
                        }

                        bytes.AddRange(buffer);
                    }

                    _content = bytes.ToArray();
                }
                return _content;
            }
        }

        public ByteStreamAttachment(Stream stream, string fileName)
        {
            FileName = fileName;
            _stream = stream;
        }
    }
}
