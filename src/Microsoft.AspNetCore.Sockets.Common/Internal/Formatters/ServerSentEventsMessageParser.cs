// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.using System;

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;

namespace Microsoft.AspNetCore.Sockets.Internal.Formatters
{
    public class ServerSentEventsMessageParser
    {
        const byte ByteCR = (byte)'\r';
        const byte ByteLF = (byte)'\n';
        const byte ByteSpace = (byte)' ';

        private InternalParsePhase _internalParserState = InternalParsePhase.ReadMessageType;
        private IList<byte[]> _data = new List<byte[]>();

        public ParseResult ParseMessage(ReadableBuffer buffer, out ReadCursor consumed, out ReadCursor examined, out Message message)
        {
            consumed = buffer.Start;
            examined = buffer.End;
            message = new Message();
            var messageType = MessageType.Text;
            var reader = new ReadableBufferReader(buffer);

            var start = consumed;
            var end = examined;

            while (!reader.End)
            {
                if (ReadCursorOperations.Seek(start, end, out var lineEnd, ByteLF) == -1)
                {
                    // Not there
                    return ParseResult.Incomplete;
                }

                lineEnd = buffer.Move(lineEnd, 1);
                var line = ConvertBufferToSpan(buffer.Slice(start, lineEnd));
                reader.Skip(line.Length);

                //Strip the \r\n from the span
                line = line.Slice(0, line.Length - 2);

                switch (_internalParserState)
                {
                    case InternalParsePhase.ReadMessageType:
                        messageType = GetMessageType(line);
                        start = lineEnd;
                        _internalParserState = InternalParsePhase.ReadMessagePayload;
                        consumed = lineEnd;
                        break;

                    case InternalParsePhase.ReadMessagePayload:

                        //Slice away the 'data: '
                        var newData = line.Slice(line.IndexOf(ByteSpace) + 1).ToArray();
                        start = lineEnd;
                        _data.Add(newData);

                        //peek into next byte. If it is the carriage return byte, then advance to the next state
                        if (reader.Peek() == ByteCR)
                        {
                            _internalParserState = InternalParsePhase.ReadEndOfMessage;
                        }
                        consumed = lineEnd;
                        break;

                    case InternalParsePhase.ReadEndOfMessage:
                        if (ReadCursorOperations.Seek(start, end, out lineEnd, ByteLF) == -1)
                        {
                            // The message has ended with \r\n\r
                            return ParseResult.Incomplete;
                        }

                        if (_data.Count > 0)
                        {
                            //Find the final size of the payload
                            var payloadSize = 0;
                            foreach (var dataLine in _data)
                            {
                                payloadSize += dataLine.Length;
                            }
                            var payload = new byte[payloadSize];

                            //Copy the contents of the data array to a single buffer
                            var marker = 0;
                            foreach (var dataLine in _data)
                            {
                                dataLine.CopyTo(payload, marker);
                                marker += dataLine.Length;
                            }

                            consumed = buffer.End;
                            message = new Message(payload, messageType);
                            return ParseResult.Completed;
                        }
                        return ParseResult.Incomplete;

                    default:
                        return ParseResult.Incomplete;
                }
            }

            return ParseResult.Incomplete;
            //while (!reader.End)
            //{
            //    var span = reader.Span;
            //    var backup = reader;
            //    var ch1 = reader.Take();
            //    var ch2 = reader.Take();
            //    if (ch1 == ByteCR)
            //    {
            //        if (ch2 == ByteLF)
            //        {
            //            consumed = buffer.End;
            //            message = new Message(data, messageType);
            //            return ParsePhase.Completed;
            //        }

            //        //When we are only missing the final \n
            //        return ParsePhase.Incomplete;
            //    }

            //    reader = backup;
            //    if (ReadCursorOperations.Seek(start, end, out var lineEnd, ByteLF) == -1)
            //    {
            //        // Not there
            //        return ParsePhase.Incomplete;
            //    }

            //    // Make sure LF is included in lineEnd
            //    lineEnd = buffer.Move(lineEnd, 1);
            //    var line = ConvertBufferToSpan(buffer.Slice(start, lineEnd));
            //    reader.Skip(line.Length);

            //    //Strip the \r\n from the span
            //    line = line.Slice(0, line.Length - 2);

            //    if (firstLine)
            //    {
            //        messageType = GetMessageType(line);
            //        start = lineEnd;
            //        firstLine = false;
            //        continue;
            //    }

            //    //Slice away the 'data: '
            //    var newData = line.Slice(line.IndexOf(ByteSpace) + 1).ToArray();
            //    start = lineEnd;

            //    if (data?.Length > 0)
            //    {
            //        var tempData = new byte[data.Length + newData.Length];
            //        data.CopyTo(tempData, 0);
            //        newData.CopyTo(tempData, data.Length);
            //        data = tempData;
            //    }
            //    else
            //    {
            //        data = newData;
            //    }
            //}

            //consumed = buffer.End;
            //message = new Message(data, messageType);
            //return ParsePhase.Completed;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Span<byte> ConvertBufferToSpan(ReadableBuffer buffer)
        {
            if (buffer.IsSingleSpan)
            {
                return buffer.First.Span;
            }
            return buffer.ToArray();
        }

        public void Reset()
        {
            _internalParserState = InternalParsePhase.ReadMessageType;
            _data.Clear();
        }

        private MessageType GetMessageType(ReadOnlySpan<byte> line)
        {
            //Skip the "data: " part of the line
            if (line.Length != 7)
            {
                throw new FormatException("There was an error parsing the message type");
            }
            var type = (char)line[6];
            switch (type)
            {
                case 'T':
                    return MessageType.Text;
                case 'B':
                    return MessageType.Binary;
                case 'C':
                    return MessageType.Close;
                case 'E':
                    return MessageType.Error;
                default:
                    throw new FormatException($"Unknown message type: '{type}'");
            }
        }

        public enum ParseResult
        {
            Completed,
            Incomplete,
            Error
        }

        private enum InternalParsePhase
        {
            Initial,
            ReadMessageType,
            ReadMessagePayload,
            ReadEndOfMessage,
            Error
        }
    }
}
