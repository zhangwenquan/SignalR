﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Text;

namespace Microsoft.AspNetCore.Sockets.Internal.Formatters
{
    public class ServerSentEventsMessageParser
    {
        private const byte ByteCR = (byte)'\r';
        private const byte ByteLF = (byte)'\n';
        private const byte ByteColon = (byte)':';

        private static byte[] _dataPrefix = Encoding.UTF8.GetBytes("data: ");
        private static byte[] _sseLineEnding = Encoding.UTF8.GetBytes("\r\n");
        private static byte[] _newLine = Encoding.UTF8.GetBytes(Environment.NewLine);

        private InternalParseState _internalParserState = InternalParseState.ReadMessagePayload;
        private List<byte[]> _data = new List<byte[]>();

        public ParseResult ParseMessage(ReadableBuffer buffer, out ReadCursor consumed, out ReadCursor examined, out byte[] message)
        {
            consumed = buffer.Start;
            examined = buffer.End;
            message = null;
            var reader = new ReadableBufferReader(buffer);

            var start = consumed;
            var end = examined;

            while (!reader.End)
            {
                if (ReadCursorOperations.Seek(start, end, out var lineEnd, ByteLF) == -1)
                {
                    // For the case of  data: Foo\r\n\r\<Anytine except \n>
                    if (_internalParserState == InternalParseState.ReadEndOfMessage)
                    {
                        if (ConvertBufferToSpan(buffer.Slice(start, buffer.End)).Length > 1)
                        {
                            throw new FormatException("Expected a \\r\\n frame ending");
                        }
                    }

                    // Partial message. We need to read more.
                    return ParseResult.Incomplete;
                }

                lineEnd = buffer.Move(lineEnd, 1);
                var line = ConvertBufferToSpan(buffer.Slice(start, lineEnd));
                reader.Skip(line.Length);

                if (line.Length <= 1)
                {
                    throw new FormatException("There was an error in the frame format");
                }

                // Skip comments
                if (line[0] == ByteColon)
                {
                    start = lineEnd;
                    consumed = lineEnd;
                    continue;
                }

                if (IsMessageEnd(line))
                {
                    _internalParserState = InternalParseState.ReadEndOfMessage;
                }

                // To ensure that the \n was preceded by a \r
                // since messages can't contain \n.
                // data: foo\n\bar should be encoded as
                // data: foo\r\n
                // data: bar\r\n
                else if (line[line.Length - _sseLineEnding.Length] != ByteCR)
                {
                    throw new FormatException("Unexpected '\n' in message. A '\n' character can only be used as part of the newline sequence '\r\n'");
                }
                else
                {
                    EnsureStartsWithDataPrefix(line);
                }

                var payload = Array.Empty<byte>();
                switch (_internalParserState)
                {
                    case InternalParseState.ReadMessagePayload:
                        EnsureStartsWithDataPrefix(line);

                        // Slice away the 'data: '
                        var payloadLength = line.Length - (_dataPrefix.Length + _sseLineEnding.Length);
                        var newData = line.Slice(_dataPrefix.Length, payloadLength).ToArray();
                        _data.Add(newData);

                        start = lineEnd;
                        consumed = lineEnd;
                        break;
                    case InternalParseState.ReadEndOfMessage:
                        if (_data.Count == 1)
                        {
                            payload = _data[0];
                        }
                        else if (_data.Count > 1)
                        {
                            // Find the final size of the payload
                            var payloadSize = 0;
                            foreach (var dataLine in _data)
                            {
                                payloadSize += dataLine.Length;
                            }

                            payloadSize += _newLine.Length * _data.Count;

                            // Allocate space in the payload buffer for the data and the new lines.
                            // Subtract newLine length because we don't want a trailing newline.
                            payload = new byte[payloadSize - _newLine.Length];

                            var offset = 0;
                            foreach (var dataLine in _data)
                            {
                                dataLine.CopyTo(payload, offset);
                                offset += dataLine.Length;
                                if (offset < payload.Length)
                                {
                                    _newLine.CopyTo(payload, offset);
                                    offset += _newLine.Length;
                                }
                            }
                        }

                        message = payload;
                        consumed = lineEnd;
                        examined = consumed;
                        return ParseResult.Completed;
                }

                if (reader.Peek() == ByteCR)
                {
                    _internalParserState = InternalParseState.ReadEndOfMessage;
                }
            }
            return ParseResult.Incomplete;
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
            _internalParserState = InternalParseState.ReadMessagePayload;
            _data.Clear();
        }

        private void EnsureStartsWithDataPrefix(ReadOnlySpan<byte> line)
        {
            if (!line.StartsWith(_dataPrefix))
            {
                throw new FormatException("Expected the message prefix 'data: '");
            }
        }

        private bool IsMessageEnd(ReadOnlySpan<byte> line)
        {
            return line.Length == _sseLineEnding.Length && line.SequenceEqual(_sseLineEnding);
        }

        public enum ParseResult
        {
            Completed,
            Incomplete,
        }

        private enum InternalParseState
        {
            ReadMessagePayload,
            ReadEndOfMessage,
            Error
        }
    }
}
