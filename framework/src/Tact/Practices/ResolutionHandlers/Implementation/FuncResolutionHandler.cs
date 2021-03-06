﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Tact.Practices.ResolutionHandlers.Implementation
{
    public class FuncResolutionHandler : IResolutionHandler
    {
        private static readonly string FuncPrefix;
        private static readonly MethodInfo CreateFuncMethodInfo;

        static FuncResolutionHandler()
        {
            FuncPrefix = typeof(Func<>).FullName;

            CreateFuncMethodInfo = typeof(FuncResolutionHandler)
                .GetTypeInfo()
                .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
                .Single(m => m.Name == nameof(CreateFunc) && m.IsGenericMethod);
        }

        public bool CanResolve(IContainer container, Stack<Type> stack, Type type, string key)
        {
            return TryResolve(
                out object result,
                container,
                stack,
                type,
                key,
                false,
                true);
        }

        public bool TryResolve(
            out object result,
            IContainer container,
            Stack<Type> stack,
            Type type,
            string key,
            bool canThrow)
        {
            return TryResolve(
                out result,
                container,
                stack,
                type,
                key,
                canThrow,
                false);
        }

        private bool TryResolve(
            out object result,
            IContainer container,
            Stack<Type> stack,
            Type type,
            string key,
            bool canThrow,
            bool returnNull)
        {
            if (!type.FullName.StartsWith(FuncPrefix))
            {
                result = null;
                return false;
            }

            if (returnNull)
            {
                result = null;
                return true;
            }

            var innerType = type.GenericTypeArguments[0];
            var method = CreateFuncMethodInfo.MakeGenericMethod(innerType);
            result = method.Invoke(this, new object[] { container, key });
            return true;
        }

        // ReSharper disable once UnusedMember.Local
        private Func<T> CreateFunc<T>(IContainer container, string key)
        {
            var type = typeof(T);
            return () => (T)container.Resolve(type, key);
        }
    }
}