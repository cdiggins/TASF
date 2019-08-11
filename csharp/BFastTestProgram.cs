﻿using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using static Vim.BFast;

namespace Vim
{
    public static class Tests
    {
        static int[] bigArray = Enumerable.Range(0, 1000 * 1000).ToArray();

        [Test]
        public static void TestToStrings()
        {
            foreach (var s in new[] { "", "ababab", "a" + "\0" + "b" })
            {
                Assert.AreEqual(s, s.ToBuffer().GetString());
                Assert.AreEqual(s, s.ToBuffer().GetString());
            }
            var s1 = "Hello";
            var s2 = "world";
            var buffer1 = s1.ToBuffer();
            var buffer2 = s2.ToBuffer();
            Assert.AreEqual(s1, buffer1.GetString());
            Assert.AreEqual(s2, buffer2.GetString());
            var strings = new[] { s1, s2 };
            var bufferStrings = strings.ToBuffer();
            var tmp = bufferStrings.GetStrings();
            Assert.AreEqual(strings, tmp);
        }

        [Test]
        public static void TestByteCasts()
        {
            var ranges = new Range[3];
            var bytes = ranges.ToBytes();
            Assert.AreEqual(16, Range.Size);
            Assert.AreEqual(Range.Size * (ulong)ranges.Length, bytes.Length);
            var span = MemoryMarshal.Cast<byte, Range>(bytes);
            var newRanges = span.ToArray();
            Assert.AreEqual(ranges, newRanges);
        }

        [Test]
        public static void TestEmptyBFast()
        {
            var buffers = new INamedBuffer[0];
            var bfastBytes = buffers.Pack();
            var bfast = bfastBytes.Unpack();
            Assert.AreEqual(0, bfast.Length);           
        }

        public static byte[] BFastToMemoryStream(IList<INamedBuffer> buffers)
            => buffers.ToBFastData().Write(new System.IO.MemoryStream()).ToArray();        

        [Test]
        public static void TestPackAndUnpack()
        {
            var xs = new[] { 1, 2, 3 };
            var ys = new[] { 3.0, 4.0, 5.0 };
            var xbuff = xs.ToNamedBuffer("xs");
            var ybuff = ys.ToNamedBuffer("ys");
            var bytes = new[] { xbuff, ybuff }.Pack();
            var bfast = bytes.Unpack();
            Assert.AreEqual(2, bfast.Length);
            Assert.AreEqual("xs", bfast[0].Name);
            Assert.AreEqual("ys", bfast[1].Name);
        }

        const int PerformanceIterations = 100;

        public static byte[] PackBigArrayUsingMemoryStream()
        {
            var bufferSize = bigArray.Length * 4;
            var buffers = new List<INamedBuffer>();
            for (var i = 0; i < PerformanceIterations; ++i)
            {
                var buffer = bigArray.ToNamedBuffer(i.ToString());
                buffers.Add(buffer);
            }
            var bytes = BFastToMemoryStream(buffers);
            Assert.IsTrue(bytes.Length > PerformanceIterations * bufferSize);
            return bytes;
        }

        public static byte[] PackBigArrayWithoutBuffer()
        {
            var bufferSize = bigArray.Length * 4;
            var bytes = new byte[bufferSize * PerformanceIterations];
            for (var i=0; i < PerformanceIterations; ++i)
            {
                // This is slower, because we are converting to bytes, rather than using a span directly
                var buffer = bigArray.ToBytes();
                buffer.CopyTo(bytes, bufferSize * i);
            }
            return bytes;
        }

        public static byte[] PackBigArray()
        {
            var bufferSize = bigArray.Length * 4;
            var buffers = new List<INamedBuffer>();
            for (var i = 0; i < PerformanceIterations; ++i)
            {
                var buffer = bigArray.ToNamedBuffer(i.ToString());
                buffers.Add(buffer);
            }
            var bytes = buffers.Pack();
            Assert.IsTrue(bytes.Length > PerformanceIterations * bufferSize);
            return bytes;
        }

        // TODO: demonstrate the speed of creating a BFast as opposed to
        [Test]
        public static void PerformanceTest()
        {
            {
                var sw = new Stopwatch();
                sw.Start();
                var bytes = PackBigArray();
                Console.WriteLine($"Created {bytes.Length} bytes in {sw.ElapsedMilliseconds} msec");
            }


            {
                var sw = new Stopwatch();
                sw.Start();
                var bytes = PackBigArrayUsingMemoryStream();
                Console.WriteLine($"Created {bytes.Length} bytes in {sw.ElapsedMilliseconds} msec");
            }

            {
                var sw = new Stopwatch();
                sw.Start();
                var bytes = PackBigArrayWithoutBuffer();
                Console.WriteLine($"Created {bytes.Length} bytes in {sw.ElapsedMilliseconds} msec");
            }
        }

        [Test]
        public static void TestAlignment()
        {
            Assert.IsTrue(IsAligned(0));
            for (var i=1ul; i < 32; ++i)
                Assert.IsFalse(IsAligned(i));
            Assert.IsTrue(IsAligned(32));

            for (var i = 1ul; i < 32; ++i)
                Assert.AreEqual(32, ComputePadding(i) + i);
        }

        public static byte GetNthByte(ulong u, int n)
            => (byte)(u >> (n * 8));       

        public static ulong SwapEndianess(ulong u)
        {
            ulong r = 0;
            for (var i=0; i < 8; ++i)
            {
                var b = GetNthByte(u, i);
                var b2 = (ulong)b << (7 - i) * 8;
                r += b2;
            }
            return r;
        }

        [Test]
        public static void TestEndianess()
        {
            ulong magic = Constants.Magic;

            Assert.AreEqual(0xA5, GetNthByte(Constants.SameEndian, 0));
            Assert.AreEqual(0xBF, GetNthByte(Constants.SameEndian, 1));
            for (var i = 2; i < 8; ++i)
                Assert.AreEqual(0x00, GetNthByte(Constants.SameEndian, i));

            Assert.AreEqual(0xA5, GetNthByte(Constants.SwappedEndian, 7));
            Assert.AreEqual(0xBF, GetNthByte(Constants.SwappedEndian, 6));
            for (var i = 5; i >= 0; --i)
                Assert.AreEqual(0x00, GetNthByte(Constants.SwappedEndian, i));

            Assert.AreEqual(Constants.SwappedEndian, SwapEndianess(magic));
            Assert.AreEqual(Constants.SameEndian, SwapEndianess(SwapEndianess(magic)));
        }

        [Test]
        public static void TestHeaderCast()
        {
            var header = new Header();
            header.Magic = Constants.Magic;
            header.DataStart = 64;
            header.DataEnd = 1024;
            header.NumArrays = 12;
            var bytes = header.ToBytes();
            Assert.AreEqual(Header.Size, bytes.Length);
            var ulongs = MemoryMarshal.Cast<byte, ulong>(bytes).ToArray();
            Assert.AreEqual(4, ulongs.Length);
            Assert.AreEqual(header.Magic, ulongs[0]);
            Assert.AreEqual(header.DataStart, ulongs[1]);
            Assert.AreEqual(header.DataEnd, ulongs[2]);
            Assert.AreEqual(header.NumArrays, ulongs[3]);
        }
    }
}