using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;

namespace triaxis.Runtime
{
    /// <summary>
    /// Represents a dynamic dispatch lookup table for the specified visitor type
    /// and node root type
    /// </summary>
    /// <typeparam name="TVisitor">Type of the visitor</typeparam>
    /// <typeparam name="TNodeBase">Base type of all visited nodes</typeparam>
    public sealed class DynamicDispatch<TVisitor, TNodeBase>
    {
        string methodName;
        Dictionary<Type, Action<TVisitor, TNodeBase>> map;

        /// <summary>
        /// Creates a dynamic dispatch lookup table using the specified method
        /// name in the visitor class
        /// </summary>
        public DynamicDispatch(string methodName)
        {
            this.methodName = methodName;
        }

        /// <summary>
        /// Dispatches the call to the best method overload
        /// in the <paramref name="visitor" />, given the actual type of the
        /// provided <paramref name="node" />
        /// </summary>
        public void Dispatch(TVisitor visitor, TNodeBase node)
            => GetMethod(node.GetType())(visitor, node);

        Action<TVisitor, TNodeBase> GetMethod(Type nodeType)
        {
            var map = this.map;
            if (map != null && map.TryGetValue(nodeType, out var result))
                return result;
            return BindMethod(nodeType);
        }

        Action<TVisitor, TNodeBase> BindMethod(Type nodeType)
        {
            for (;;)
            {
                var map = this.map;
                if (map != null && map.TryGetValue(nodeType, out var result))
                    return result;
                var res = CreateMethod(nodeType);
                var newMap = map == null ?
                    new Dictionary<Type, Action<TVisitor, TNodeBase>>() :
                    new Dictionary<Type, Action<TVisitor, TNodeBase>>(map);
                newMap[nodeType] = res;
                if (Interlocked.CompareExchange(ref this.map, newMap, map) == map)
                    return res;
            }
        }

        Action<TVisitor, TNodeBase> CreateMethod(Type nodeType)
        {
            var mth = typeof(TVisitor).GetMethod(
                methodName,
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new Type[] { nodeType },
                null);

            var mthWithLink = typeof(TVisitor).GetMethod(
                methodName,
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new Type[] { nodeType, typeof(Action<TNodeBase>) },
                null);

            if (mth == null && mthWithLink == null)
                return (visitor, node) => throw new NotSupportedException($"Visitor {visitor} cannot handle {node} using {methodName}");

            if (mth != null && mthWithLink != null && !IsMoreSpecific(mth, mthWithLink))
                mth = null;

            if (mth != null)
            {
                if (nodeType == typeof(TNodeBase))
                {
                    return (Action<TVisitor, TNodeBase>)Delegate.CreateDelegate(typeof(Action<TVisitor, TNodeBase>), mth, true);
                }
                else
                {
                    var adapterType = typeof(Adapter<>).MakeGenericType(typeof(TVisitor), typeof(TNodeBase), nodeType);
                    var adapter = Activator.CreateInstance(adapterType, mth);
                    return (Action<TVisitor, TNodeBase>)Delegate.CreateDelegate(typeof(Action<TVisitor, TNodeBase>), adapter, "Dispatch", false, true);
                }
            }
            else
            {
                var adapterType = typeof(LinkAdapter<>).MakeGenericType(typeof(TVisitor), typeof(TNodeBase), nodeType);
                var adapter = Activator.CreateInstance(adapterType, mthWithLink, this);
                return (Action<TVisitor, TNodeBase>)Delegate.CreateDelegate(typeof(Action<TVisitor, TNodeBase>), adapter, "Dispatch", false, true);
            }
        }

        static bool IsMoreSpecific(MethodInfo m1, MethodInfo m2)
        {
            var t1 = m1.GetParameters()[0].ParameterType;
            var t2 = m2.GetParameters()[0].ParameterType;

            // interface implementation is always less specific than type implementation
            return IsMoreSpecific(t1, t2) ?? IsMoreSpecific(m1.DeclaringType, m2.DeclaringType) ?? true;
        }

        static bool? IsMoreSpecific(Type t1, Type t2)
        {
            bool if1 = t1.IsInterface, if2 = t2.IsInterface;
            if (if1 != if2)
                return if2;

            bool a1 = t1.IsAssignableFrom(t2), a2 = t2.IsAssignableFrom(t1);
            if (a1 != a2)
                return a2;

            return null;
        }

        class Adapter<TActualElement> where TActualElement : TNodeBase
        {
            Action<TVisitor,TActualElement> action;

            public Adapter(MethodInfo info)
            {
                action = (Action<TVisitor,TActualElement>)Delegate.CreateDelegate(typeof(Action<TVisitor,TActualElement>), info, true);
            }

            public void Dispatch(TVisitor visitor, TNodeBase element)
                => action(visitor, (TActualElement)element);
        }

        class LinkAdapter<TNode> where TNode : TNodeBase
        {
            Action<TVisitor,TNode,Action<TNodeBase>> action;
            DynamicDispatch<TVisitor, TNodeBase> owner;

            public LinkAdapter(MethodInfo method, DynamicDispatch<TVisitor, TNodeBase> owner)
            {
                action = (Action<TVisitor,TNode,Action<TNodeBase>>)Delegate.CreateDelegate(
                    typeof(Action<TVisitor,TNode,Action<TNodeBase>>), method, true);
                this.owner = owner;
            }

            public void Dispatch(TVisitor visitor, TNodeBase element)
                => action(visitor, (TNode)element, e => owner.Dispatch(visitor, e));
        }
    }
}
