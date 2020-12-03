using Driscod.Extensions;
using NUnit.Framework;
using System.Net.Http;

namespace Driscod.Tests.Extensions
{
    public class HttpResponseHeadersExtensionsTests
    {
        [Test]
        public void HttpResponseHeadersExtensions_GetFirstValueOrNull_Success()
        {
            var response = new HttpResponseMessage();

            response.Headers.Add("Test", "test");

            Assert.AreEqual("test", response.Headers.GetFirstValueOrNull("Test"));
            Assert.IsNull(response.Headers.GetFirstValueOrNull("NonExistant"));
        }

        [Test]
        public void HttpResponseHeadersExtensions_GetFirstValue_Success()
        {
            var response = new HttpResponseMessage();

            response.Headers.Add("Test", "test1");
            response.Headers.Add("Test", "test2");

            Assert.AreEqual("test1", response.Headers.GetFirstValueOrNull("Test"));
        }
    }
}
