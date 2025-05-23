// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;

using Internal.Text;
using Internal.TypeSystem;

namespace ILCompiler.DependencyAnalysis
{
    public class RuntimeMethodHandleNode : DehydratableObjectNode, ISymbolDefinitionNode
    {
        private MethodDesc _targetMethod;

        public MethodDesc Method => _targetMethod;

        public RuntimeMethodHandleNode(MethodDesc targetMethod)
        {
            Debug.Assert(!targetMethod.IsSharedByGenericInstantiations);

            // IL is allowed to LDTOKEN an uninstantiated thing. Do not check IsRuntimeDetermined for the nonexact thing.
            Debug.Assert((targetMethod.HasInstantiation && targetMethod.IsMethodDefinition)
                || targetMethod.OwningType.IsGenericDefinition
                || !targetMethod.IsRuntimeDeterminedExactMethod);
            _targetMethod = targetMethod;
        }

        public void AppendMangledName(NameMangler nameMangler, Utf8StringBuilder sb)
        {
            sb.Append(nameMangler.CompilationUnitPrefix)
              .Append("__RuntimeMethodHandle_"u8)
              .Append(nameMangler.GetMangledMethodName(_targetMethod));
        }
        public int Offset => 0;
        protected override string GetName(NodeFactory factory) => this.GetMangledName(factory.NameMangler);
        public override bool IsShareable => false;
        public override bool StaticDependenciesAreComputed => true;

        protected override ObjectNodeSection GetDehydratedSection(NodeFactory factory)
        {
            if (factory.Target.IsWindows)
                return ObjectNodeSection.ReadOnlyDataSection;
            else
                return ObjectNodeSection.DataSection;
        }

        protected override DependencyList ComputeNonRelocationBasedDependencies(NodeFactory factory)
        {
            DependencyList dependencies = null;

            if (!_targetMethod.IsMethodDefinition && !_targetMethod.OwningType.IsGenericDefinition
                && _targetMethod.HasInstantiation && _targetMethod.IsVirtual)
            {
                dependencies ??= new DependencyList();
                MethodDesc canonMethod = _targetMethod.GetCanonMethodTarget(CanonicalFormKind.Specific);
                dependencies.Add(factory.GVMDependencies(canonMethod), "GVM dependencies for runtime method handle");

                // GVM analysis happens on canonical forms, but this is potentially injecting new genericness
                // into the system. Ensure reflection analysis can still see this.
                if (_targetMethod.IsAbstract)
                    factory.MetadataManager.GetDependenciesDueToMethodCodePresence(ref dependencies, factory, canonMethod, methodIL: null);
            }

            factory.MetadataManager.GetDependenciesDueToLdToken(ref dependencies, factory, _targetMethod);

            return dependencies;
        }

        protected override ObjectData GetDehydratableData(NodeFactory factory, bool relocsOnly = false)
        {
            ObjectDataBuilder objData = new ObjectDataBuilder(factory, relocsOnly);

            objData.RequireInitialPointerAlignment();
            objData.AddSymbol(this);

            int handle = relocsOnly ? 0 : factory.MetadataManager.GetMetadataHandleForMethod(factory, _targetMethod.GetTypicalMethodDefinition());

            objData.EmitPointerReloc(factory.MaximallyConstructableType(_targetMethod.OwningType));
            objData.EmitInt(handle);

            if (_targetMethod != _targetMethod.GetMethodDefinition())
            {
                objData.EmitInt(_targetMethod.Instantiation.Length);
                foreach (TypeDesc instParam in _targetMethod.Instantiation)
                    objData.EmitPointerReloc(factory.NecessaryTypeSymbol(instParam));
            }
            else
            {
                objData.EmitInt(0);
            }

            return objData.ToObjectData();
        }

        public override int ClassCode => -274400625;

        public override int CompareToImpl(ISortableNode other, CompilerComparer comparer)
        {
            return comparer.Compare(_targetMethod, ((RuntimeMethodHandleNode)other)._targetMethod);
        }
    }
}
