// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Reflection.TypeLoading
{
    /// <summary>
    /// Base class for all Type and TypeInfo objects created by a MetadataLoadContext.
    /// </summary>
    internal partial class RoType
    {
        public sealed override Type? GetInterface(string name, bool ignoreCase)
        {
            ArgumentNullException.ThrowIfNull(name);

            name.SplitTypeName(out string ns, out string simpleName);

            Type? match = null;
            foreach (Type ifc in ImplementedInterfaces)
            {
                string ifcSimpleName = ifc.Name;
                bool simpleNameMatches = ignoreCase
                    ? simpleName.Equals(ifcSimpleName, StringComparison.OrdinalIgnoreCase)
                    : simpleName.Equals(ifcSimpleName);
                if (!simpleNameMatches)
                    continue;

                // This check exists for .NET Framework compat:
                //   (1) caller can optionally omit namespace part of name in pattern- we'll still match.
                //   (2) ignoreCase:true does not apply to the namespace portion.
                if (ns.Length != 0 && !ns.Equals(ifc.Namespace))
                    continue;
                if (match != null)
                    throw ThrowHelper.GetAmbiguousMatchException(match);
                match = ifc;
            }
            return match;
        }
    }
}
