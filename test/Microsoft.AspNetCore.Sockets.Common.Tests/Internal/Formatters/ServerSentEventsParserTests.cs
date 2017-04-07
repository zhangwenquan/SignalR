// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.IO;
using System.Text;
using Microsoft.AspNetCore.Sockets.Internal.Formatters;
using Xunit;
using System.Threading.Tasks;
using System.IO.Pipelines;
using System;

namespace Microsoft.AspNetCore.Sockets.Common.Tests.Internal.Formatters
{
    public class ServerSentEventsParserTests
    {
        [Theory]
        [InlineData("data: T\r\ndata: Hello, World\r\n\r\n", MessageType.Text, "Hello, World")]
        [InlineData("data: E\r\ndata: Hello, World\r\n\r\n", MessageType.Error, "Hello, World")]
        [InlineData("data: T\r\ndata: Hello\r\ndata: , World\r\n\r\n", MessageType.Text, "Hello, World")]
        [InlineData("data: T\r\ndata: Major\r\ndata:  Key\r\ndata:  Alert\r\n\r\n", MessageType.Text, "Major Key Alert")]
        [InlineData("data: T\r\n\r\n", MessageType.Text, "")]
        public async Task ParseSSEMessageSuccessCases(string encodedMessage, MessageType messageType, string expectedMessage)
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


        [Theory]
        [InlineData("", MessageType.Text, "Error parsing data from event stream")]
        [InlineData("data: X\r\n\r\n", MessageType.Text, "Unknown message type: 'X'")]
        [InlineData("data: X\r\n", MessageType.Text, "Unknown message type: 'X'")]
        [InlineData("data:", MessageType.Text, "Expected a '\r\n' line ending")]
        [InlineData("data: T\n", MessageType.Text, "There was an error parsing the message type")]
        [InlineData("data: T\r\nda", MessageType.Text, "Expected a '\r\n' line ending")]
        [InlineData("data: T\r\ndata:", MessageType.Text, "Expected a '\r\n' line ending")]
        [InlineData("data: T\r\ndata: Hello, World", MessageType.Text, "Expected a '\r\n' line ending")]
        [InlineData("data: T\r\ndata: Hello, World\r", MessageType.Text, "Expected a '\r\n' line ending")]
        [InlineData("data: T\r\ndata: Hello, World\r\n", MessageType.Text, "Error parsing data from event stream")]
        [InlineData("data: T\r\ndata: Hello, World\r\n\r", MessageType.Text, "Expected a '\r\n' line ending")]
        [InlineData("data: T\r\ndata: Hello, World\r\n\r\\", MessageType.Text, "Expected a '\r\n' line ending")]
        [InlineData("data: T\r\ndata: Hello, World\r\n\r\n\n", MessageType.Text, "Unexpected data after line ending")]
        [InlineData("data: Not the message type\r\n\r\n", MessageType.Text, "There was an error parsing the message type")]
        [InlineData("data: Not the message type\r\r\n", MessageType.Text, "There was an error parsing the message type")]
        [InlineData("data: T\r\ndata: Hello, World\r\r\n\n", MessageType.Text, "Error parsing data from event stream")]
        [InlineData("data: T\r\ndata: Hello, World\n\n", MessageType.Text, "Error parsing data from event stream")]
        [InlineData("data: T\r\ndata: Major\r\ndata:  Key\rndata:  Alert\r\n\r\\", MessageType.Text, "Expected a '\r\n' line ending")]
        [InlineData("data: T\r\ndata: Major\r\ndata:  Key\r\ndata:  Alert\r\n\r\\", MessageType.Text, "Expected a '\r\n' line ending")]
        //[InlineData("data: T\r\nfoo: Hello, World\r\n\r\n", MessageType.Text, "Hello, World")]
        public async Task ParseSSEMessageFailureCases(string encodedMessage, MessageType messageType, string expectedExceptionMessage)
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

            var ex = Assert.Throws<FormatException>(() => { parser.ParseMessage(buffer, out consumed, out examined, out Message message); });
            Assert.Equal(expectedExceptionMessage, ex.Message);
        }
    }
}
