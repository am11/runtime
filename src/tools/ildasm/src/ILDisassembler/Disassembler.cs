// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;

using Internal.IL;
using Internal.TypeSystem;
using Internal.TypeSystem.Ecma;

namespace ILDisassembler
{
    /// <summary>
    /// Main entry point for IL disassembly.
    /// Uses the managed TypeSystem infrastructure for full token resolution.
    /// </summary>
    public sealed class Disassembler : IDisposable
    {
        private readonly PEReader _peReader;
        private readonly EcmaModule _module;
        private readonly ILDisasmTypeSystemContext _context;
        private readonly Options _options;
        private readonly bool _ownsReader;

        public Disassembler(string filePath, Options options = null)
        {
            _options = options ?? new Options();
            _ownsReader = true;

            var stream = File.OpenRead(filePath);
            _peReader = new PEReader(stream);

            if (!_peReader.HasMetadata)
            {
                throw new InvalidOperationException("PE file does not contain metadata");
            }

            string directory = Path.GetDirectoryName(filePath);
            _context = new ILDisasmTypeSystemContext(new SimpleAssemblyResolver(directory ?? "."));
            _module = _context.GetModuleForSimpleName(Path.GetFileNameWithoutExtension(filePath), _peReader);

            // Initialize system module for WellKnownType resolution
            _context.EnsureSystemModule();
        }

        public Disassembler(PEReader peReader, IAssemblyResolver resolver, Options options = null)
        {
            _options = options ?? new Options();
            _peReader = peReader;
            _ownsReader = false;

            if (!_peReader.HasMetadata)
            {
                throw new InvalidOperationException("PE file does not contain metadata");
            }

            _context = new ILDisasmTypeSystemContext(resolver);
            var reader = _peReader.GetMetadataReader();
            string name = reader.GetString(reader.IsAssembly
                ? reader.GetAssemblyDefinition().Name
                : reader.GetModuleDefinition().Name);
            _module = _context.GetModuleForSimpleName(Path.GetFileNameWithoutExtension(name), _peReader);

            // Initialize system module for WellKnownType resolution
            _context.EnsureSystemModule();
        }

        /// <summary>
        /// Disassembles the PE file to the specified output.
        /// </summary>
        public void Disassemble(TextWriter output)
        {
            // TODO: Implement --html option to wrap output in HTML format
            // TODO: Implement --rtf option to wrap output in RTF format

            var writer = new ILWriter(output);

            // Write header comment
            writer.WriteComment($".NET IL Disassembler.  Version {typeof(Disassembler).Assembly.GetName().Version}");
            writer.WriteLine();
            writer.WriteComment($"Metadata version: {_module.MetadataReader.MetadataVersion}");

            // TODO: Implement --headers option to output PE headers (.imagebase, .corflags, .subsystem, etc.)

            // Disassemble in order:
            // 1. Assembly extern references
            WriteAssemblyRefs(writer);

            // 2. Assembly definition
            WriteAssemblyDef(writer);

            // 3. Module definition
            WriteModuleDef(writer);

            // TODO: Implement --classlist option to output list of classes
            // TODO: Implement --typelist option to output full type list for round-trip ordering
            // TODO: Output manifest resources (.mresource)
            // TODO: Output file references (.file)
            // TODO: Output exported types (.class extern)
            // TODO: Output module references (.module extern)

            // 4. Type definitions
            WriteTypeDefs(writer);

            // TODO: Implement --stats option to output image statistics
        }

        private void WriteAssemblyRefs(ILWriter writer)
        {
            var reader = _module.MetadataReader;
            foreach (var handle in reader.AssemblyReferences)
            {
                var assemblyRef = reader.GetAssemblyReference(handle);
                var name = reader.GetString(assemblyRef.Name);
                var version = assemblyRef.Version;

                writer.WriteLine($".assembly extern {name}");
                writer.WriteLine("{");
                // TODO: Output .publickeytoken on assembly references
                // TODO: Output .culture on assembly references
                // TODO: Output .hash on assembly references
                writer.WriteLine($"  .ver {version.Major}:{version.Minor}:{version.Build}:{version.Revision}");
                writer.WriteLine("}");
            }
        }

        private void WriteAssemblyDef(ILWriter writer)
        {
            var reader = _module.MetadataReader;
            if (!reader.IsAssembly)
            {
                return;
            }

            var assemblyDef = reader.GetAssemblyDefinition();
            var name = reader.GetString(assemblyDef.Name);
            var version = assemblyDef.Version;

            writer.WriteLine($".assembly {name}");
            writer.WriteLine("{");
            // TODO: Output .publickey on assembly definition
            // TODO: Output .culture on assembly definition
            // TODO: Output .hash algorithm on assembly definition
            // TODO: Output custom attributes on assembly
            writer.WriteLine($"  .ver {version.Major}:{version.Minor}:{version.Build}:{version.Revision}");
            writer.WriteLine("}");
        }

        private void WriteModuleDef(ILWriter writer)
        {
            var reader = _module.MetadataReader;
            var moduleDef = reader.GetModuleDefinition();
            var name = reader.GetString(moduleDef.Name);

            writer.WriteLine($".module {name}");
            writer.WriteComment($"MVID: {{{reader.GetGuid(moduleDef.Mvid)}}}");
            // TODO: Output custom attributes on module
        }

        private void WriteTypeDefs(ILWriter writer)
        {
            // TODO: Implement --visibility/--pubonly options to filter types by visibility
            // TODO: Implement --item option to disassemble specific type/method only
            foreach (var type in _module.GetAllTypes())
            {
                WriteType(writer, type);
            }
        }

        private void WriteType(ILWriter writer, MetadataType type)
        {
            // Skip <Module>
            // TODO: Handle global functions in <Module> type
            string typeName = type.GetName();
            if (typeName == "<Module>")
            {
                return;
            }

            writer.WriteLine();

            // Build full type name
            string ns = type.GetNamespace();
            string fullName = string.IsNullOrEmpty(ns) ? typeName : $"{ns}.{typeName}";

            // Get type attributes
            var ecmaType = type as EcmaType;
            if (ecmaType == null)
            {
                return;
            }

            // TODO: Handle nested types properly (currently flattened)
            // TODO: Implement --forward option for forward class declarations

            var reader = ecmaType.Module.MetadataReader;
            var typeDef = reader.GetTypeDefinition(ecmaType.Handle);
            var attributes = typeDef.Attributes;

            string visibility = GetTypeVisibility(attributes);
            string modifiers = GetTypeModifiers(attributes);
            string typeKeyword = GetTypeKeyword(attributes);

            // TODO: Output generic type parameters and constraints

            // Write class header
            writer.WriteLine($".class {visibility}{modifiers}{typeKeyword}{fullName}");

            // Write extends clause - use try/catch in case base type can't be resolved
            try
            {
                if (type.BaseType != null && !type.IsInterface)
                {
                    writer.WriteLine($"       extends {FormatType(type.BaseType)}");
                }
            }
            catch (TypeSystemException)
            {
                // Base type couldn't be resolved - just skip it
            }

            // TODO: Output implements clause for interfaces

            writer.WriteLine("{");
            writer.Indent();

            // TODO: Output .pack and .size for explicit layout types
            // TODO: Output security declarations (.permissionset)
            // TODO: Output custom attributes on type (unless --noca)

            // Write fields
            foreach (var field in type.GetFields())
            {
                try
                {
                    WriteField(writer, field);
                }
                catch (TypeSystemException)
                {
                    // Skip fields that can't be resolved
                }
            }

            // TODO: Output .property declarations with .get/.set accessors
            // TODO: Output .event declarations with .addon/.removeon/.fire accessors

            // Write methods
            foreach (var method in type.GetMethods())
            {
                try
                {
                    WriteMethod(writer, method);
                }
                catch (TypeSystemException)
                {
                    // Skip methods that can't be resolved
                }
            }

            writer.Dedent();
            writer.WriteLine($"}} // end of class {fullName}");
        }

        private void WriteField(ILWriter writer, FieldDesc field)
        {
            var ecmaField = field as EcmaField;
            if (ecmaField == null)
            {
                return;
            }

            var reader = ecmaField.Module.MetadataReader;
            var fieldDef = reader.GetFieldDefinition(ecmaField.Handle);
            var attributes = fieldDef.Attributes;

            string visibility = GetFieldVisibility(attributes);
            string modifiers = GetFieldModifiers(attributes);
            string fieldType = FormatType(field.FieldType);
            string fieldName = field.GetName();

            // TODO: Implement --tokens option to show field token
            // TODO: Output field offset for explicit layout
            // TODO: Output field RVA for mapped fields (.data)
            // TODO: Output field initial value for literals
            // TODO: Output field marshaling info
            // TODO: Output custom attributes on field (unless --noca)
            writer.WriteLine($".field {visibility}{modifiers}{fieldType} {fieldName}");
        }

        private void WriteMethod(ILWriter writer, MethodDesc method)
        {
            var ecmaMethod = method as EcmaMethod;
            if (ecmaMethod == null)
            {
                return;
            }

            var reader = ecmaMethod.Module.MetadataReader;
            var methodDef = reader.GetMethodDefinition(ecmaMethod.Handle);
            var attributes = methodDef.Attributes;

            string visibility = GetMethodVisibility(attributes);
            string modifiers = GetMethodModifiers(attributes);
            string returnType = FormatType(method.Signature.ReturnType);
            string methodName = method.GetName();

            // TODO: Implement --tokens option to show method token
            // TODO: Output generic method parameters and constraints
            // TODO: Output calling convention (vararg, etc.)
            // TODO: Output P/Invoke info (.pinvokeimpl)

            // Build parameter list
            // TODO: Output parameter names from metadata
            // TODO: Output parameter attributes (in, out, optional)
            // TODO: Output default parameter values
            var sb = new StringBuilder();
            sb.Append('(');
            for (int i = 0; i < method.Signature.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append(", ");
                }
                sb.Append(FormatType(method.Signature[i]));
            }
            sb.Append(')');
            string parameters = sb.ToString();

            writer.WriteLine();
            writer.WriteLine($".method {visibility}{modifiers}{returnType} {methodName}{parameters} cil managed");
            writer.WriteLine("{");
            writer.Indent();

            // TODO: Output custom attributes on method and parameters (unless --noca)
            // TODO: Output .override for explicit interface implementations

            // Write IL body if present
            var methodIL = EcmaMethodIL.Create(ecmaMethod);
            if (methodIL != null)
            {
                WriteMethodBody(writer, methodIL);
            }

            writer.Dedent();
            writer.WriteLine($"}} // end of method {methodName}");
        }

        private void WriteMethodBody(ILWriter writer, MethodIL methodIL)
        {
            // TODO: Implement --bytes option to show IL bytes as hex comments
            // TODO: Implement --source/--linenum options for PDB source line info
            writer.WriteLine($"// Code size: {methodIL.GetILBytes().Length}");
            writer.WriteLine($".maxstack {methodIL.MaxStack}");

            // TODO: Output .entrypoint for entry point method

            // Write locals
            // TODO: Output local variable names from PDB
            var locals = methodIL.GetLocals();
            if (locals != null && locals.Length > 0)
            {
                writer.Write(".locals ");
                if (methodIL.IsInitLocals)
                {
                    writer.Write("init ");
                }
                writer.Write("(");
                for (int i = 0; i < locals.Length; i++)
                {
                    if (i > 0)
                    {
                        writer.Write(", ");
                    }
                    writer.Write($"[{i}] {FormatType(locals[i].Type)}");
                }
                writer.WriteLine(")");
            }

            // TODO: Output exception handling regions (.try/.catch/.finally/.fault/.filter)

            // Use Internal.IL.ILDisassembler for instruction disassembly
            var disasm = new Internal.IL.ILDisassembler(methodIL);
            while (disasm.HasNextInstruction)
            {
                writer.WriteLine(disasm.GetNextInstruction());
            }
        }

        private string FormatType(TypeDesc type)
        {
            if (type == null)
            {
                return "void";
            }

            // Handle array types first (before checking category)
            if (type is ArrayType arrayType)
            {
                return FormatType(arrayType.ElementType) + "[]";
            }

            if (type is ByRefType byRefType)
            {
                return FormatType(byRefType.ParameterType) + "&";
            }

            if (type is PointerType pointerType)
            {
                return FormatType(pointerType.ParameterType) + "*";
            }

            // For MetadataType, format by name to avoid WellKnownType resolution issues
            if (type is MetadataType mdType)
            {
                string ns = mdType.GetNamespace();
                string name = mdType.GetName();

                // Check for well-known type names
                if (ns == "System")
                {
                    switch (name)
                    {
                        case "Void": return "void";
                        case "Boolean": return "bool";
                        case "Char": return "char";
                        case "SByte": return "int8";
                        case "Byte": return "uint8";
                        case "Int16": return "int16";
                        case "UInt16": return "uint16";
                        case "Int32": return "int32";
                        case "UInt32": return "uint32";
                        case "Int64": return "int64";
                        case "UInt64": return "uint64";
                        case "IntPtr": return "native int";
                        case "UIntPtr": return "native uint";
                        case "Single": return "float32";
                        case "Double": return "float64";
                        case "String": return "string";
                        case "Object": return "object";
                    }
                }

                // Check if it's from a different assembly
                if (mdType.Module != _module && mdType.Module is IAssemblyDesc assemblyDesc)
                {
                    string assemblyName = assemblyDesc.GetName().Name;
                    if (!string.IsNullOrEmpty(ns))
                    {
                        return $"[{assemblyName}]{ns}.{name}";
                    }
                    return $"[{assemblyName}]{name}";
                }

                if (!string.IsNullOrEmpty(ns))
                {
                    return $"{ns}.{name}";
                }
                return name;
            }

            return type.ToString();
        }

        private static string GetTypeVisibility(TypeAttributes attributes)
        {
            return (attributes & TypeAttributes.VisibilityMask) switch
            {
                TypeAttributes.Public => "public ",
                TypeAttributes.NotPublic => "private ",
                TypeAttributes.NestedPublic => "nested public ",
                TypeAttributes.NestedPrivate => "nested private ",
                TypeAttributes.NestedFamily => "nested family ",
                TypeAttributes.NestedAssembly => "nested assembly ",
                TypeAttributes.NestedFamANDAssem => "nested famandassem ",
                TypeAttributes.NestedFamORAssem => "nested famorassem ",
                _ => ""
            };
        }

        private static string GetTypeModifiers(TypeAttributes attributes)
        {
            var sb = new StringBuilder();

            if ((attributes & TypeAttributes.Abstract) != 0)
            {
                sb.Append("abstract ");
            }

            if ((attributes & TypeAttributes.Sealed) != 0)
            {
                sb.Append("sealed ");
            }

            if ((attributes & TypeAttributes.SequentialLayout) != 0)
            {
                sb.Append("sequential ");
            }

            if ((attributes & TypeAttributes.ExplicitLayout) != 0)
            {
                sb.Append("explicit ");
            }

            sb.Append("auto ansi ");

            return sb.ToString();
        }

        private static string GetTypeKeyword(TypeAttributes attributes)
        {
            if ((attributes & TypeAttributes.Interface) != 0)
            {
                return "interface ";
            }
            return "";
        }

        private static string GetFieldVisibility(FieldAttributes attributes)
        {
            return (attributes & FieldAttributes.FieldAccessMask) switch
            {
                FieldAttributes.Public => "public ",
                FieldAttributes.Private => "private ",
                FieldAttributes.Family => "family ",
                FieldAttributes.Assembly => "assembly ",
                FieldAttributes.FamANDAssem => "famandassem ",
                FieldAttributes.FamORAssem => "famorassem ",
                _ => ""
            };
        }

        private static string GetFieldModifiers(FieldAttributes attributes)
        {
            var sb = new StringBuilder();

            if ((attributes & FieldAttributes.Static) != 0)
            {
                sb.Append("static ");
            }

            if ((attributes & FieldAttributes.InitOnly) != 0)
            {
                sb.Append("initonly ");
            }

            if ((attributes & FieldAttributes.Literal) != 0)
            {
                sb.Append("literal ");
            }

            return sb.ToString();
        }

        private static string GetMethodVisibility(MethodAttributes attributes)
        {
            return (attributes & MethodAttributes.MemberAccessMask) switch
            {
                MethodAttributes.Public => "public ",
                MethodAttributes.Private => "private ",
                MethodAttributes.Family => "family ",
                MethodAttributes.Assembly => "assembly ",
                MethodAttributes.FamANDAssem => "famandassem ",
                MethodAttributes.FamORAssem => "famorassem ",
                _ => ""
            };
        }

        private static string GetMethodModifiers(MethodAttributes attributes)
        {
            var sb = new StringBuilder();

            if ((attributes & MethodAttributes.HideBySig) != 0)
            {
                sb.Append("hidebysig ");
            }

            if ((attributes & MethodAttributes.Static) != 0)
            {
                sb.Append("static ");
            }

            if ((attributes & MethodAttributes.Virtual) != 0)
            {
                sb.Append("virtual ");
            }

            if ((attributes & MethodAttributes.Final) != 0)
            {
                sb.Append("final ");
            }

            if ((attributes & MethodAttributes.NewSlot) != 0)
            {
                sb.Append("newslot ");
            }

            if ((attributes & MethodAttributes.Abstract) != 0)
            {
                sb.Append("abstract ");
            }

            if ((attributes & MethodAttributes.SpecialName) != 0)
            {
                sb.Append("specialname ");
            }

            if ((attributes & MethodAttributes.RTSpecialName) != 0)
            {
                sb.Append("rtspecialname ");
            }

            return sb.ToString();
        }

        public void Dispose()
        {
            if (_ownsReader)
            {
                _peReader.Dispose();
            }
        }
    }
}
