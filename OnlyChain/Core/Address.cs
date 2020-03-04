﻿using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;

namespace OnlyChain.Core {
    unsafe public readonly struct Address : IEquatable<Address>, IComparable<Address> {
        public static readonly int Size = sizeof(Address);

        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        private readonly Hash<Size160> hash;

        public Address(in Hash<Size160> hash) => this.hash = hash;
        public Address(ReadOnlySpan<byte> address) => hash = new Hash<Size160>(address);
        public Address(ReadOnlySpan<char> address) => hash = new Hash<Size160>(address);

        /// <summary>
        /// 栈上的<see cref="Address"/>对象使用此属性才是安全的。
        /// </summary>
        public readonly ReadOnlySpan<byte> Span {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => hash.Span;
        }

        public static ref Address FromSpan(Span<byte> span) {
            if (span.Length < sizeof(Address)) throw new ArgumentException($"必须大于等于{sizeof(Address)}字节", nameof(span));
            return ref Unsafe.As<byte, Address>(ref MemoryMarshal.GetReference(span));
        }

        public static Address Random() => Hash<Size160>.Random();

        public readonly override string ToString() => Hex.ToString(hash);

        public readonly override int GetHashCode() => hash.GetHashCode();
        public readonly override bool Equals(object obj) => obj is Address other && Equals(other);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool Equals(Address other) {
            fixed (Address* @this = &this) {
                if (((ulong*)@this)[0] != ((ulong*)&other)[0]) return false;
                if (((ulong*)@this)[1] != ((ulong*)&other)[1]) return false;
                return ((uint*)@this)[4] == ((uint*)&other)[4];
            }
        }

        public static bool operator ==(Address left, Address right) => left.Equals(right);
        public static bool operator !=(Address left, Address right) => !(left == right);

        public static implicit operator Address(in Hash<Size160> hash) => new Address(hash);
        public static implicit operator Hash<Size160>(in Address address) => address.hash;
        public static implicit operator Address(string address) => new Address(address);

        public readonly int CompareTo(Address other) {
            fixed (Address* @this = &this) {
                if (BitConverter.IsLittleEndian) {
                    ulong v1 = BinaryPrimitives.ReverseEndianness(((ulong*)@this)[0]);
                    ulong v2 = BinaryPrimitives.ReverseEndianness(((ulong*)&other)[0]);
                    if (v1 < v2) return -1; else if (v1 > v2) return 1;
                    v1 = BinaryPrimitives.ReverseEndianness(((ulong*)@this)[1]);
                    v2 = BinaryPrimitives.ReverseEndianness(((ulong*)&other)[1]);
                    if (v1 < v2) return -1; else if (v1 > v2) return 1;
                    v1 = BinaryPrimitives.ReverseEndianness(((uint*)@this)[4]);
                    v2 = BinaryPrimitives.ReverseEndianness(((uint*)&other)[4]);
                    return v1.CompareTo(v2);
                } else {
                    ulong v1 = ((ulong*)@this)[0];
                    ulong v2 = ((ulong*)&other)[0];
                    if (v1 < v2) return -1; else if (v1 > v2) return 1;
                    v1 = ((ulong*)@this)[1];
                    v2 = ((ulong*)&other)[1];
                    if (v1 < v2) return -1; else if (v1 > v2) return 1;
                    v1 = ((uint*)@this)[4];
                    v2 = ((uint*)&other)[4];
                    return v1.CompareTo(v2);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Address operator ^(Address left, Address right) {
            Address r = default;
            ((ulong*)&r)[0] = ((ulong*)&left)[0] ^ ((ulong*)&right)[0];
            ((ulong*)&r)[1] = ((ulong*)&left)[1] ^ ((ulong*)&right)[1];
            ((uint*)&r)[4] = ((uint*)&left)[4] ^ ((uint*)&right)[4];
            return r;
        }

        public static bool operator <(in Address left, in Address right) => left.CompareTo(right) < 0;
        public static bool operator >(in Address left, in Address right) => left.CompareTo(right) > 0;
        public static bool operator <=(in Address left, in Address right) => left.CompareTo(right) <= 0;
        public static bool operator >=(in Address left, in Address right) => left.CompareTo(right) >= 0;

        /// <summary>
        /// 把<see cref="Address"/>当成大整数，求log2向下取整的结果。
        /// <para>特别的，全0地址的log2结果等于-1。</para>
        /// </summary>
        public readonly int Log2 {
            get {
                fixed (Address* p = &this) {
                    if (BitConverter.IsLittleEndian) { // 大部分CPU应该都支持lzcnt指令
                        var r = BitOperations.LeadingZeroCount(BinaryPrimitives.ReverseEndianness(((ulong*)p)[0]));
                        if (r < 64) return 159 - r;
                        r = BitOperations.LeadingZeroCount(BinaryPrimitives.ReverseEndianness(((ulong*)p)[1]));
                        if (r < 64) return 95 - r;
                        r = BitOperations.LeadingZeroCount(BinaryPrimitives.ReverseEndianness(((uint*)p)[4]));
                        return 31 - r;
                    } else {
                        var r = BitOperations.LeadingZeroCount(((ulong*)p)[0]);
                        if (r < 64) return 159 - r;
                        r = BitOperations.LeadingZeroCount(((ulong*)p)[1]);
                        if (r < 64) return 95 - r;
                        r = BitOperations.LeadingZeroCount(((uint*)p)[4]);
                        return 31 - r;
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly void WriteToBytes(Span<byte> buffer) {
            fixed (Address* p = &this) new ReadOnlySpan<byte>(p, sizeof(Address)).CopyTo(buffer);
        }

        /// <summary>
        /// 检测对应下标的二进制位是否为1。
        /// </summary>
        /// <param name="bitIndex">从低位到高位（从右到左）的索引，从0开始计数。</param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool Bit(int bitIndex) {
            if (bitIndex < 0 || bitIndex >= sizeof(Address) * 8) throw new ArgumentOutOfRangeException(nameof(bitIndex));
            fixed (Address* p = &this) return (((byte*)p)[Size - 1 - (bitIndex >> 3)] & (1 << (bitIndex & 7))) != 0;
        }
    }
}