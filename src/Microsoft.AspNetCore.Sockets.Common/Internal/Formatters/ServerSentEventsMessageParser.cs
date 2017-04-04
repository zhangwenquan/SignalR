// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.using System;

using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;

namespace Microsoft.AspNetCore.Sockets.Internal.Formatters
{
    public  class ServerSentEventsMessageParser
    {

        public static ParsePhase ParseMessage(ReadableBuffer buffer, out ReadCursor consumed, out ReadCursor examined, out Message message)
        {
            const byte ByteCR = (byte)'\r';
            const byte ByteLF = (byte)'\n';
            const byte ByteSpace = (byte)' ';
            bool firstLine = true;
            byte[] data = null;
            consumed = buffer.Start;
            examined = buffer.End;
            message = new Message();
            MessageType messageType = MessageType.Text;
            var reader = new ReadableBufferReader(buffer);

            var start = consumed;
            var end = examined;

            while (!reader.End)
            {
                var span = reader.Span;
                var backup = reader;
                var ch1 = reader.Take();
                var ch2 = reader.Take();
                if (ch1 == ByteCR)
                {
                    if (ch2 == ByteLF)
                    {
                        consumed = buffer.End;
                        message = new Message(data, messageType);
                        return ParsePhase.Completed;
                    }

                    //When we are only missing the final \n
                    return ParsePhase.Incomplete;
                }

                reader = backup;
                if (ReadCursorOperations.Seek(start, end, out var lineEnd, ByteLF) == -1)
                {
                    // Not there
                    return ParsePhase.Incomplete;
                }

                // Make sure LF is included in lineEnd
                lineEnd = buffer.Move(lineEnd, 1);
                var line = ConvertBufferToSpan(buffer.Slice(start, lineEnd));
                reader.Skip(line.Length);

                //Strip the \r\n from the span
                line = line.Slice(0, line.Length - 2);

                if (firstLine)
                {
                    messageType = GetMessageType(line);
                    start = lineEnd;
                    firstLine = false;
                    continue;
                }

                var nothing = buffer.End;
                //Slice away the 'data: '
                var newData = line.Slice(line.IndexOf(ByteSpace) + 1).ToArray();
                start = lineEnd;

                if (data?.Length > 0)
                {
                    var tempData = new byte[data.Length + newData.Length];
                    data.CopyTo(tempData, 0);
                    newData.CopyTo(tempData, data.Length);
                    data = tempData;
                }
                else
                {
                    data = newData;
                }
            }

            consumed = buffer.End;
            message = new Message(data, messageType);
            return ParsePhase.Completed;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Span<byte> ConvertBufferToSpan(ReadableBuffer buffer)
        {
            if (buffer.IsSingleSpan)
            {
                return buffer.First.Span;
            }
            return buffer.ToArray();
        }

        private static MessageType GetMessageType(ReadOnlySpan<byte> line)
        {
            //Skip the "data: " part of the line
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
                    throw new FormatException("Invalid mesage type");
            }
        }
        public enum ParsePhase
        {
            Completed,
            Incomplete,
            Error
        }
    }
}
