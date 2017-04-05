// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.IO;
using System.Text;
using Microsoft.AspNetCore.Sockets.Internal.Formatters;
using Xunit;
using System.Threading.Tasks;
using System.IO.Pipelines;

namespace Microsoft.AspNetCore.Sockets.Common.Tests.Internal.Formatters
{
    public class ServerSentEventsParserTests
    {
        [Theory]
        [InlineData("data: T\r\ndata: Hello, World\r\n\r\n", MessageType.Text, "Hello, World")]
        [InlineData("data: T\r\ndata: Hello\r\ndata: , World\r\n\r\n", MessageType.Text, "Hello, World")]
        public async Task ParseSSEMessage(string encodedMessage, MessageType messageType, string expectedMessage)
        {
            var stream = new MemoryStream();
            var streamWriter = new StreamWriter(stream);
            streamWriter.Write(encodedMessage);
            streamWriter.Flush();
            stream.Seek(0, SeekOrigin.Begin);

            var pipelineReader = stream.AsPipelineReader();
            var buffer = await pipelineReader.ReadToEndAsync();
            var parser = new ServerSentEventsMessageParser();

            var consumed = new ReadCursor();
            var examined = new ReadCursor();

            var parsePhase = parser.ParseMessage(buffer, out consumed, out examined, out Message message);
            Assert.Equal(ServerSentEventsMessageParser.ParseResult.Completed, parsePhase);

            var result = Encoding.UTF8.GetString(message.Payload);
            Assert.Equal(expectedMessage, result);
        }
    }
}
