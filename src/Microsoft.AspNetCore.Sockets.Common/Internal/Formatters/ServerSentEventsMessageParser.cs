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
        private bool _firstLine = true;
        private byte[] data;

        public ParsePhase ParseMessage(ReadableBuffer buffer, out ReadCursor consumed, out ReadCursor examined, out Message message)
        {
            consumed = buffer.Start;
            examined = buffer.End;
            message = new Message();
            MessageType messageType = MessageType.Text;

            var start = consumed;
            var end = examined;

            while(start != end)
            {
                if (ReadCursorOperations.Seek(start, end, out var found, (byte)'\r',(byte)'\n') == -1)
                {
                    return ParsePhase.Incomplete;
                }

                if( found == start)
                {
                    consumed = buffer.Move(start, 2);
                    message = new Message(data, messageType);
                    return ParsePhase.Completed;
                }
                var line = ToSpan(buffer.Slice(start, found));

                //Parse the message type if its the first line
                if (_firstLine)
                {
                    messageType = GetMessageFormat(line);
                    start = buffer.Move(found, 2);
                    _firstLine = false;
                    continue;
                }

                consumed = buffer.End;
                var newData = line.Slice(line.IndexOf((byte)' ') + 1).ToArray();

                //This handles the case when we receive multiple lines with data
                if (data?.Length > 0)
                {
                    var tempData = new byte[data.Length + newData.Length];
                    data.CopyTo(tempData, 0);
                    newData.CopyTo(tempData, data.Length);
                    data = tempData;
                }
                else
                {
                    data = line.Slice(line.IndexOf((byte)' ') + 1).ToArray();
                }

                start = buffer.Move(found, 2);
            }
            message = new Message(data, messageType);
            return ParsePhase.Completed;
        }

        public void Reset()
        {
            _firstLine = true;
            data = null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private  Span<byte> ToSpan(ReadableBuffer buffer)
        {
            if (buffer.IsSingleSpan)
            {
                return buffer.First.Span;
            }
            return buffer.ToArray();
        }

        private MessageType GetMessageFormat(ReadOnlySpan<byte> line)
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
