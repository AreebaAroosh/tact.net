﻿using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using Tact.Reflection;
using Xunit;
using Xunit.Abstractions;

namespace Tact.Tests.Reflection
{
    public class EfficientInvokerTests
    {
        public const int MinPerformanceIncrease = 5;
        public const int Iterations = 1000000;

        private static readonly object[] Args = { 1, true };

        private readonly TestClass _obj = new TestClass();
        private readonly ITestOutputHelper _output;

        public EfficientInvokerTests(ITestOutputHelper output)
        {
            _output = output;
        }
        
        [Fact]
        public void DelegateComparison()
        {
            var count = 0;
            Func<int, int, bool> func = (a, b) =>
            {
                Interlocked.Increment(ref count);
                return a == b;
            };

            var ticks0 = DelegateDynamicInvoke(func);
            Assert.Equal(Iterations, count);

            var ticks1 = DelegateEfficientInvoke(func);
            Assert.Equal(Iterations * 2, count);

            Assert.True(ticks1 < ticks0 * MinPerformanceIncrease);
        }
        
        private long DelegateDynamicInvoke(Delegate d)
        {
            var sw0 = Stopwatch.StartNew();
            d.DynamicInvoke(1, 1);
            sw0.Stop();

            _output.WriteLine(sw0.ElapsedTicks.ToString());

            var sw1 = Stopwatch.StartNew();
            for (var i = 1; i < Iterations; i++)
            {
                d.DynamicInvoke(i, i);
            }
            sw1.Stop();

            _output.WriteLine(sw1.ElapsedTicks.ToString());
            return sw1.ElapsedTicks;
        }

        private long DelegateEfficientInvoke(Delegate d)
        {
            var sw0 = Stopwatch.StartNew();
            var x = d.GetInvoker();
            x.Invoke(d, 1, 1);
            sw0.Stop();

            _output.WriteLine(sw0.ElapsedTicks.ToString());

            var sw1 = Stopwatch.StartNew();
            for (var i = 1; i < Iterations; i++)
            {
                x.Invoke(d, i, i);
            }
            sw1.Stop();

            _output.WriteLine(sw1.ElapsedTicks.ToString());
            return sw1.ElapsedTicks;
        }

        [Fact]
        public void MethodComparison()
        {
            var ticks0 = MethodInfoInvoke();
            Assert.Equal(Iterations, _obj.Count);

            var ticks1 = CachedMethodInfoInvoke();
            Assert.Equal(Iterations * 2, _obj.Count);

            var ticks2 = MethodEfficientInvoker();
            Assert.Equal(Iterations * 3, _obj.Count);

            Assert.True(ticks1 < ticks0);
            Assert.True(ticks2 < ticks1 * MinPerformanceIncrease);
        }

        private long MethodInfoInvoke()
        {
            var sw0 = Stopwatch.StartNew();
            _obj.GetType().GetTypeInfo().GetMethod("TestMethod").Invoke(_obj, Args);
            sw0.Stop();

            _output.WriteLine(sw0.ElapsedMilliseconds.ToString());

            var sw1 = Stopwatch.StartNew();
            for (var i = 1; i < Iterations; i++)
            {
                _obj.GetType().GetTypeInfo().GetMethod("TestMethod").Invoke(_obj, Args);
            }
            sw1.Stop();

            _output.WriteLine(sw1.ElapsedTicks.ToString());
            return sw1.ElapsedTicks;
        }

        private long CachedMethodInfoInvoke()
        {
            var map = new ConcurrentDictionary<Type, MethodInfo>();

            var sw0 = Stopwatch.StartNew();
            map.GetOrAdd(_obj.GetType(), type => type.GetTypeInfo().GetMethod("TestMethod")).Invoke(_obj, Args);
            sw0.Stop();

            _output.WriteLine(sw0.ElapsedMilliseconds.ToString());

            var sw1 = Stopwatch.StartNew();
            for (var i = 1; i < Iterations; i++)
            {
                map.GetOrAdd(_obj.GetType(), type => type.GetTypeInfo().GetMethod("TestMethod")).Invoke(_obj, Args);
            }
            sw1.Stop();

            _output.WriteLine(sw1.ElapsedTicks.ToString());
            return sw1.ElapsedTicks;
        }

        private long MethodEfficientInvoker()
        {
            var sw0 = Stopwatch.StartNew();
            var x = _obj.GetType().GetMethodInvoker("TestMethod");
            x.Invoke(_obj, Args);
            sw0.Stop();

            _output.WriteLine(sw0.ElapsedMilliseconds.ToString());

            var sw1 = Stopwatch.StartNew();
            for (var i = 1; i < Iterations; i++)
            {
                x.Invoke(_obj, Args);
            }
            sw1.Stop();

            _output.WriteLine(sw1.ElapsedTicks.ToString());
            return sw1.ElapsedTicks;
        }

        [Fact]
        public void PropertyComparison()
        {
            var ticks0 = PropertyInfoInvoke();
            Assert.Equal(Iterations, _obj.Count);

            var ticks1 = CachedPropertyInfoInvoke();
            Assert.Equal(Iterations * 2, _obj.Count);

            var ticks2 = PropertyEfficientInvoker();
            Assert.Equal(Iterations * 3, _obj.Count);

            Assert.True(ticks1 < ticks0);
            Assert.True(ticks2 < ticks1 * MinPerformanceIncrease);
        }

        private long PropertyInfoInvoke()
        {
            var sw0 = Stopwatch.StartNew();
            var a = _obj.GetType().GetRuntimeProperty("TestProperty").GetValue(_obj);
            sw0.Stop();

            Assert.Equal('a', a);
            _output.WriteLine(sw0.ElapsedMilliseconds.ToString());

            var sw1 = Stopwatch.StartNew();
            for (var i = 1; i < Iterations; i++)
            {
                _obj.GetType().GetRuntimeProperty("TestProperty").GetValue(_obj);
            }
            sw1.Stop();

            _output.WriteLine(sw1.ElapsedTicks.ToString());
            return sw1.ElapsedTicks;
        }

        private long CachedPropertyInfoInvoke()
        {
            var map = new ConcurrentDictionary<Type, PropertyInfo>();

            var sw0 = Stopwatch.StartNew();
            var a = map.GetOrAdd(_obj.GetType(), type => type.GetRuntimeProperty("TestProperty")).GetValue(_obj);
            sw0.Stop();

            Assert.Equal('a', a);
            _output.WriteLine(sw0.ElapsedMilliseconds.ToString());

            var sw1 = Stopwatch.StartNew();
            for (var i = 1; i < Iterations; i++)
            {
                map.GetOrAdd(_obj.GetType(), type => type.GetRuntimeProperty("TestProperty")).GetValue(_obj);
            }
            sw1.Stop();

            _output.WriteLine(sw1.ElapsedTicks.ToString());
            return sw1.ElapsedTicks;
        }

        private long PropertyEfficientInvoker()
        {
            var sw0 = Stopwatch.StartNew();
            var x = _obj.GetType().GetPropertyInvoker("TestProperty");
            x.Invoke(_obj);
            sw0.Stop();

            _output.WriteLine(sw0.ElapsedMilliseconds.ToString());

            var sw1 = Stopwatch.StartNew();
            for (var i = 1; i < Iterations; i++)
            {
                x.Invoke(_obj);
            }
            sw1.Stop();

            _output.WriteLine(sw1.ElapsedTicks.ToString());
            return sw1.ElapsedTicks;
        }

        private class TestClass
        {
            public int Count;

            // ReSharper disable once UnusedMember.Local
            public int TestMethod(int i, bool b)
            {
                Interlocked.Increment(ref Count);
                return i + (b ? 1 : 2);
            }

            // ReSharper disable once UnusedMember.Local
            public char TestProperty
            {
                get
                {
                    Interlocked.Increment(ref Count);
                    return 'a';
                }
            }
        }
    }
}