using Driscod.Tracking;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;

namespace Driscod.Extensions
{
    public static class MultipartFormDataContentExtensions
    {
        public static MultipartFormDataContent AddJsonPayload(this MultipartFormDataContent content, string json)
        {
            if (json != null)
            {
                var jsonContent = new StringContent(json, Encoding.UTF8, "application/json");
                jsonContent.Headers.Add("Content-Disposition", "form-data; name=\"payload_json\"");
                content.Add(jsonContent);
            }
            return content;
        }

        public static MultipartFormDataContent AddAttachments(this MultipartFormDataContent content, IEnumerable<IMessageAttachment> attachments)
        {
            for (var i = 0; i < attachments.Count(); i++)
            {
                var attachment = attachments.ElementAt(i);
                var fileContent = new ByteArrayContent(attachment.Content, 0, attachment.Content.Length);
                fileContent.Headers.Add("Content-Disposition", $"form-data; name=\"files[{i}]\"; filename=\"{attachment.FileName}\"");
                content.Add(fileContent);
            }
            return content;
        }
    }
}
