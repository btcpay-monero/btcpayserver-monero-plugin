using BTCPayServer.Plugins.Monero.RPC.Models;

using Newtonsoft.Json;

using Xunit;

namespace BTCPayServer.Plugins.UnitTests.Monero.RPC.Models
{
    public class ParseStringConverterTests
    {
        [Fact]
        public void ReadJson_WithValidString_ReturnsLong()
        {
            // Create JSON string with a valid long "12345"
            var json = "\"12345\"";
            using var sr = new StringReader(json);
            using var reader = new JsonTextReader(sr);
            var serializer = new JsonSerializer();
            reader.Read();

            var result = ParseStringConverter.Singleton.ReadJson(reader, typeof(long), null, serializer);

            Assert.IsType<long>(result);
            Assert.Equal(12345L, (long)result);
        }

        [Fact]
        public void ReadJson_WithNullToken_ReturnsNull()
        {
            var json = "null";
            using var sr = new StringReader(json);
            using var reader = new JsonTextReader(sr);
            var serializer = new JsonSerializer();
            reader.Read();

            var result = ParseStringConverter.Singleton.ReadJson(reader, typeof(long?), null, serializer);

            Assert.Null(result);
        }

        [Fact]
        public void ReadJson_WithInvalidString_ThrowsException()
        {
            // Create JSON string that cannot be parsed into a long.
            var json = "\"abc\"";
            using var sr = new StringReader(json);
            using var reader = new JsonTextReader(sr);
            var serializer = new JsonSerializer();
            reader.Read();

            Assert.Throws<FormatException>(() => ParseStringConverter.Singleton.ReadJson(reader, typeof(long), null, serializer));
        }

        [Fact]
        public void WriteJson_WithLong_WritesCorrectStringRepresentation()
        {
            var sw = new StringWriter();
            using var writer = new JsonTextWriter(sw);
            var serializer = new JsonSerializer();
            long input = 67890L;

            ParseStringConverter.Singleton.WriteJson(writer, input, serializer);
            writer.Flush();
            var output = sw.ToString();

            // The converter serializes the long as a string, so expected JSON is "\"67890\""
            Assert.Equal("\"67890\"", output);
        }

        [Fact]
        public void WriteJson_WithNull_WritesNull()
        {
            var sw = new StringWriter();
            using var writer = new JsonTextWriter(sw);
            var serializer = new JsonSerializer();

            ParseStringConverter.Singleton.WriteJson(writer, null, serializer);
            writer.Flush();
            var output = sw.ToString();

            // Expected output is the JSON literal null.
            Assert.Equal("null", output);
        }
    }
}