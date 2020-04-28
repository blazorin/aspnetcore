// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers.Binary;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Infrastructure;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http
{
    internal abstract class HttpHeaders : IHeaderDictionary
    {
        protected long _bits = 0;
        protected long? _contentLength;
        protected bool _isReadOnly;
        protected Dictionary<string, StringValues> MaybeUnknown;
        protected Dictionary<string, StringValues> Unknown => MaybeUnknown ?? (MaybeUnknown = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase));

        public long? ContentLength
        {
            get { return _contentLength; }
            set
            {
                if (_isReadOnly)
                {
                    ThrowHeadersReadOnlyException();
                }
                if (value.HasValue && value.Value < 0)
                {
                    ThrowInvalidContentLengthException(value.Value);
                }
                _contentLength = value;
            }
        }

        StringValues IHeaderDictionary.this[string key]
        {
            get
            {
                TryGetValueFast(key, out var value);
                return value;
            }
            set
            {
                if (_isReadOnly)
                {
                    ThrowHeadersReadOnlyException();
                }
                if (string.IsNullOrEmpty(key))
                {
                    ThrowInvalidEmptyHeaderName();
                }
                if (value.Count == 0)
                {
                    RemoveFast(key);
                }
                else
                {
                    SetValueFast(key, value);
                }
            }
        }

        StringValues IDictionary<string, StringValues>.this[string key]
        {
            get
            {
                // Unlike the IHeaderDictionary version, this getter will throw a KeyNotFoundException.
                if (!TryGetValueFast(key, out var value))
                {
                    ThrowKeyNotFoundException();
                }
                return value;
            }
            set
            {
                ((IHeaderDictionary)this)[key] = value;
            }
        }

        protected static void ThrowHeadersReadOnlyException()
        {
            throw new InvalidOperationException(CoreStrings.HeadersAreReadOnly);
        }

        protected static void ThrowArgumentException()
        {
            throw new ArgumentException();
        }

        protected static void ThrowKeyNotFoundException()
        {
            throw new KeyNotFoundException();
        }

        protected static void ThrowDuplicateKeyException()
        {
            throw new ArgumentException(CoreStrings.KeyAlreadyExists);
        }

        public int Count => GetCountFast();

        bool ICollection<KeyValuePair<string, StringValues>>.IsReadOnly => _isReadOnly;

        ICollection<string> IDictionary<string, StringValues>.Keys => ((IDictionary<string, StringValues>)this).Select(pair => pair.Key).ToList();

        ICollection<StringValues> IDictionary<string, StringValues>.Values => ((IDictionary<string, StringValues>)this).Select(pair => pair.Value).ToList();

        public void SetReadOnly()
        {
            _isReadOnly = true;
        }

        // Inline to allow ClearFast to devirtualize in caller
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Reset()
        {
            _isReadOnly = false;
            ClearFast();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        protected static StringValues AppendValue(StringValues existing, string append)
        {
            return StringValues.Concat(existing, append);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        protected bool TryGetUnknown(string key, ref StringValues value) => MaybeUnknown?.TryGetValue(key, out value) ?? false;

        [MethodImpl(MethodImplOptions.NoInlining)]
        protected bool RemoveUnknown(string key) => MaybeUnknown?.Remove(key) ?? false;

        protected virtual int GetCountFast()
        { throw new NotImplementedException(); }

        protected virtual bool TryGetValueFast(string key, out StringValues value)
        { throw new NotImplementedException(); }

        protected virtual void SetValueFast(string key, StringValues value)
        { throw new NotImplementedException(); }

        protected virtual bool AddValueFast(string key, StringValues value)
        { throw new NotImplementedException(); }

        protected virtual bool RemoveFast(string key)
        { throw new NotImplementedException(); }

        protected virtual void ClearFast()
        { throw new NotImplementedException(); }

        protected virtual bool CopyToFast(KeyValuePair<string, StringValues>[] array, int arrayIndex)
        { throw new NotImplementedException(); }

        protected virtual IEnumerator<KeyValuePair<string, StringValues>> GetEnumeratorFast()
        { throw new NotImplementedException(); }

        void ICollection<KeyValuePair<string, StringValues>>.Add(KeyValuePair<string, StringValues> item)
        {
            ((IDictionary<string, StringValues>)this).Add(item.Key, item.Value);
        }

        void IDictionary<string, StringValues>.Add(string key, StringValues value)
        {
            if (_isReadOnly)
            {
                ThrowHeadersReadOnlyException();
            }
            if (string.IsNullOrEmpty(key))
            {
                ThrowInvalidEmptyHeaderName();
            }

            if (value.Count > 0 && !AddValueFast(key, value))
            {
                ThrowDuplicateKeyException();
            }
        }

        void ICollection<KeyValuePair<string, StringValues>>.Clear()
        {
            if (_isReadOnly)
            {
                ThrowHeadersReadOnlyException();
            }
            ClearFast();
        }

        bool ICollection<KeyValuePair<string, StringValues>>.Contains(KeyValuePair<string, StringValues> item)
        {
            return
                TryGetValueFast(item.Key, out var value) &&
                value.Equals(item.Value);
        }

        bool IDictionary<string, StringValues>.ContainsKey(string key)
        {
            return TryGetValueFast(key, out _);
        }

        void ICollection<KeyValuePair<string, StringValues>>.CopyTo(KeyValuePair<string, StringValues>[] array, int arrayIndex)
        {
            if (!CopyToFast(array, arrayIndex))
            {
                ThrowArgumentException();
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumeratorFast();
        }

        IEnumerator<KeyValuePair<string, StringValues>> IEnumerable<KeyValuePair<string, StringValues>>.GetEnumerator()
        {
            return GetEnumeratorFast();
        }

        bool ICollection<KeyValuePair<string, StringValues>>.Remove(KeyValuePair<string, StringValues> item)
        {
            return
                TryGetValueFast(item.Key, out var value) &&
                value.Equals(item.Value) &&
                RemoveFast(item.Key);
        }

        bool IDictionary<string, StringValues>.Remove(string key)
        {
            if (_isReadOnly)
            {
                ThrowHeadersReadOnlyException();
            }
            return RemoveFast(key);
        }

        bool IDictionary<string, StringValues>.TryGetValue(string key, out StringValues value)
        {
            return TryGetValueFast(key, out value);
        }

        public static void ValidateHeaderValueCharacters(StringValues headerValues)
        {
            var count = headerValues.Count;
            for (var i = 0; i < count; i++)

            {
                ValidateHeaderValueCharacters(headerValues[i]);
            }
        }

        public static void ValidateHeaderValueCharacters(string headerCharacters)
        {
            if (headerCharacters != null)
            {
                var invalid = HttpCharacters.IndexOfInvalidFieldValueChar(headerCharacters);
                if (invalid >= 0)
                {
                    ThrowInvalidHeaderCharacter(headerCharacters[invalid]);
                }
            }
        }

        public static void ValidateHeaderNameCharacters(string headerCharacters)
        {
            var invalid = HttpCharacters.IndexOfInvalidTokenChar(headerCharacters);
            if (invalid >= 0)
            {
                ThrowInvalidHeaderCharacter(headerCharacters[invalid]);
            }
        }

        public static unsafe ConnectionOptions ParseConnection(StringValues connection)
        {
            var connectionOptions = ConnectionOptions.None;

            var connectionCount = connection.Count;
            for (var i = 0; i < connectionCount; i++)
            {
                var value = connection[i];
                fixed (char* ptr = value)
                {
                    var ch = ptr;
                    var tokenEnd = ch;
                    var end = ch + value.Length;

                    while (ch < end)
                    {
                        while (tokenEnd < end && *tokenEnd != ',')
                        {
                            tokenEnd++;
                        }

                        while (ch < tokenEnd && *ch == ' ')
                        {
                            ch++;
                        }

                        var tokenLength = tokenEnd - ch;

                        if (tokenLength >= 9 && (*ch | 0x20) == 'k')
                        {
                            if ((*++ch | 0x20) == 'e' &&
                                (*++ch | 0x20) == 'e' &&
                                (*++ch | 0x20) == 'p' &&
                                *++ch == '-' &&
                                (*++ch | 0x20) == 'a' &&
                                (*++ch | 0x20) == 'l' &&
                                (*++ch | 0x20) == 'i' &&
                                (*++ch | 0x20) == 'v' &&
                                (*++ch | 0x20) == 'e')
                            {
                                ch++;
                                while (ch < tokenEnd && *ch == ' ')
                                {
                                    ch++;
                                }

                                if (ch == tokenEnd)
                                {
                                    connectionOptions |= ConnectionOptions.KeepAlive;
                                }
                            }
                        }
                        else if (tokenLength >= 7 && (*ch | 0x20) == 'u')
                        {
                            if ((*++ch | 0x20) == 'p' &&
                                (*++ch | 0x20) == 'g' &&
                                (*++ch | 0x20) == 'r' &&
                                (*++ch | 0x20) == 'a' &&
                                (*++ch | 0x20) == 'd' &&
                                (*++ch | 0x20) == 'e')
                            {
                                ch++;
                                while (ch < tokenEnd && *ch == ' ')
                                {
                                    ch++;
                                }

                                if (ch == tokenEnd)
                                {
                                    connectionOptions |= ConnectionOptions.Upgrade;
                                }
                            }
                        }
                        else if (tokenLength >= 5 && (*ch | 0x20) == 'c')
                        {
                            if ((*++ch | 0x20) == 'l' &&
                                (*++ch | 0x20) == 'o' &&
                                (*++ch | 0x20) == 's' &&
                                (*++ch | 0x20) == 'e')
                            {
                                ch++;
                                while (ch < tokenEnd && *ch == ' ')
                                {
                                    ch++;
                                }

                                if (ch == tokenEnd)
                                {
                                    connectionOptions |= ConnectionOptions.Close;
                                }
                            }
                        }

                        tokenEnd++;
                        ch = tokenEnd;
                    }
                }
            }

            return connectionOptions;
        }

        private static char ToLowerCase(char value) => (char)(value | (char)0x0020);

        public static TransferCoding GetFinalTransferCoding(StringValues transferEncoding)
        {
            const ulong chunkedStart = 0x006b_006e_0075_0068; // 4 chars "hunk"
            const uint chunkedEnd = 0x0064_0065; // 2 chars "ed"

            var transferEncodingOptions = TransferCoding.None;

            var transferEncodingCount = transferEncoding.Count;
            for (var i = 0; i < transferEncodingCount; i++)
            {
                var values = transferEncoding[i].AsSpan();

                while (values.Length > 0)
                {
                    int offset;
                    char c = '\0';
                    // Skip any spaces and empty values.
                    for (offset = 0; offset < values.Length; offset++)
                    {
                        c = values[offset];
                        if (c != ' ' && c != ',')
                        {
                            break;
                        }
                    }

                    // Skip last read char.
                    offset++;
                    if ((uint)offset > (uint)values.Length)
                    {
                        // Consumed entire string, move to next.
                        break;
                    }

                    // Remove leading spaces or empty values.
                    values = values.Slice(offset);
                    offset = 0;

                    var byteValue = MemoryMarshal.AsBytes(values);

                    if (ToLowerCase(c) == 'c' &&
                        TryReadLowerCaseUInt64(byteValue, out var result64) && result64 == chunkedStart)
                    {
                        offset += sizeof(ulong) / 2;
                        byteValue = byteValue.Slice(sizeof(ulong));

                        if (TryReadLowerCaseUInt32(byteValue, out var result32) && result32 == chunkedEnd)
                        {
                            offset += sizeof(uint) / 2;
                            transferEncodingOptions = TransferCoding.Chunked;
                        }
                    }

                    if ((uint)offset >= (uint)values.Length)
                    {
                        // Consumed entire string, move to next string.
                        break;
                    }
                    else
                    {
                        values = values.Slice(offset);
                    }

                    for (offset = 0; offset < values.Length; offset++)
                    {
                        c = values[offset];
                        if (c == ',')
                        {
                            break;
                        }
                        else if (c != ' ')
                        {
                            // Value contains extra chars; Chunked is not the matched one.
                            transferEncodingOptions = TransferCoding.Other;
                        }
                    }

                    if ((uint)offset >= (uint)values.Length)
                    {
                        // Consumed entire string, move to next string.
                        break;
                    }
                    else if (c == ',')
                    {
                        // Consumed value, move to next value.
                        offset++;
                        if ((uint)offset >= (uint)values.Length)
                        {
                            // Consumed entire string, move to next string.
                            break;
                        }
                        else
                        {
                            values = values.Slice(offset);
                            continue;
                        }
                    }
                }
            }

            return transferEncodingOptions;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool TryReadLowerCaseUInt64(ReadOnlySpan<byte> byteValue, out ulong value)
        {
            if (BinaryPrimitives.TryReadUInt64LittleEndian(byteValue, out var result))
            {
                value = result | 0x0020_0020_0020_0020;
                return true;
            }

            value = 0;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool TryReadLowerCaseUInt32(ReadOnlySpan<byte> byteValue, out uint value)
        {
            if (BinaryPrimitives.TryReadUInt32LittleEndian(byteValue, out var result))
            {
                value = result | 0x0020_0020;
                return true;
            }

            value = 0;
            return false;
        }

        private static void ThrowInvalidContentLengthException(long value)
        {
            throw new ArgumentOutOfRangeException(CoreStrings.FormatInvalidContentLength_InvalidNumber(value));
        }

        private static void ThrowInvalidHeaderCharacter(char ch)
        {
            throw new InvalidOperationException(CoreStrings.FormatInvalidAsciiOrControlChar(string.Format("0x{0:X4}", (ushort)ch)));
        }

        private static void ThrowInvalidEmptyHeaderName()
        {
            throw new InvalidOperationException(CoreStrings.InvalidEmptyHeaderName);
        }
    }
}
