
using System.Reflection.Metadata.Ecma335;
using System.Reflection.Metadata;
using ICSharpCode.Decompiler.TypeSystem;

namespace AssemblyAnalyzer
{
    internal static class UniqueTypeNaming
    {
        // For types defined in the assembly under analysis
        public static string Get(MetadataReader reader, TypeDefinitionHandle typeDefHandle)
        {
            var typeDef = reader.GetTypeDefinition(typeDefHandle);

            // 1. Namespace
            string ns = reader.GetString(typeDef.Namespace);

            // 2. Type name
            string typeName = reader.GetString(typeDef.Name);

            // 3. Handle nested types
            string fullTypeName = typeName;
            EntityHandle declaringTypeHandle = typeDef.GetDeclaringType();
            while (!declaringTypeHandle.IsNil)
            {
                var declaringTypeDef = reader.GetTypeDefinition((TypeDefinitionHandle)declaringTypeHandle);
                string declaringTypeName = reader.GetString(declaringTypeDef.Name);
                fullTypeName = declaringTypeName + "+" + fullTypeName;
                declaringTypeHandle = declaringTypeDef.GetDeclaringType();
            }
            if (!string.IsNullOrEmpty(ns))
                fullTypeName = ns + "." + fullTypeName;

            // 4. Generic arity
            int genericArity = typeDef.GetGenericParameters().Count;

            // 5. Metadata token
            int metadataToken = MetadataTokens.GetToken(typeDefHandle);

            // Compose the key
            return $"{fullTypeName}`{genericArity}|0x{metadataToken:X8}";
        }

        // For imported types
        public static string Get(MetadataReader reader, TypeReferenceHandle typeRefHandle)
        {
            var typeRef = reader.GetTypeReference(typeRefHandle);

            // 1. Namespace
            string ns = reader.GetString(typeRef.Namespace);

            // 2. Type name
            string typeName = reader.GetString(typeRef.Name);

            // 3. Compose full type name
            string fullTypeName = string.IsNullOrEmpty(ns) ? typeName : $"{ns}.{typeName}";

            // 4. Metadata token
            int metadataToken = MetadataTokens.GetToken(typeRefHandle);

            // TypeReference does not have generic arity or nesting info in metadata
            return $"{fullTypeName}|0x{metadataToken:X8}";
        }

        // For exported types
        public static string Get(MetadataReader reader, ExportedTypeHandle exportedTypeHandle)
        {
            var exportedType = reader.GetExportedType(exportedTypeHandle);

            // 1. Namespace
            string ns = reader.GetString(exportedType.Namespace);

            // 2. Type name
            string typeName = reader.GetString(exportedType.Name);

            // 3. Compose full type name
            string fullTypeName = string.IsNullOrEmpty(ns) ? typeName : $"{ns}.{typeName}";

            // 4. Metadata token
            int metadataToken = MetadataTokens.GetToken(exportedTypeHandle);

            // ExportedType does not have generic arity or nesting info in metadata
            return $"{fullTypeName}|0x{metadataToken:X8}";
        }
    }

    internal static class UniqueMethodNaming
    {
        // For methods internal to the assembly under analysis
        public static string Get(MetadataReader reader, TypeDefinitionHandle typeDefHandle, MethodDefinitionHandle methodHandle)
        {
            var typeDef = reader.GetTypeDefinition(typeDefHandle);
            var method = reader.GetMethodDefinition(methodHandle);

            // 1. Declaring type
            string fullTypeName = UniqueTypeNaming.Get(reader, typeDefHandle);

            // 2. Method name
            string methodName = reader.GetString(method.Name);
            if (methodName.StartsWith("."))
                methodName = methodName.Substring(1);

            // 3. Generic arity
            int genericArity = method.GetGenericParameters().Count;

            // 4. Parameter types
            var paramTypes = new List<string>();
            foreach (var paramHandle in method.GetParameters())
            {
                var param = reader.GetParameter(paramHandle);
                if (param.SequenceNumber > 0)
                {
                    paramTypes.Add(reader.GetString(param.Name));
                }
            }
            string paramTypesKey = string.Join(",", paramTypes);

            // 5. Method attributes
            string attrKey = method.Attributes.ToString();

            // 6. Metadata token
            int metadataToken = MetadataTokens.GetToken(methodHandle);

            return $"{fullTypeName}.{methodName}`{genericArity}({paramTypesKey})|{attrKey}|0x{metadataToken:X8}";
        }

        // For imported functions - params not available without symbol info
        public static string Get(MetadataReader reader, MemberReferenceHandle memberRefHandle)
        {
            var memberRef = reader.GetMemberReference(memberRefHandle);

            // 1. Declaring type
            string declaringTypeName = memberRef.Parent.Kind switch
            {
                HandleKind.TypeReference => UniqueTypeNaming.Get(reader, (TypeReferenceHandle)memberRef.Parent),
                HandleKind.TypeDefinition => UniqueTypeNaming.Get(reader, (TypeDefinitionHandle)memberRef.Parent),
                _ => memberRef.Parent.Kind.ToString()
            };

            // 2. Method name
            string methodName = reader.GetString(memberRef.Name);
            if (methodName.StartsWith("."))
                methodName = methodName.Substring(1);

            // 3. Metadata token
            int metadataToken = MetadataTokens.GetToken(memberRefHandle);

            // 4. (Optional) Signature blob for best-effort uniqueness (parameter types)
            string signatureKey = "";
            if (!memberRef.Signature.IsNil)
            {
                var blobReader = reader.GetBlobReader(memberRef.Signature);
                signatureKey = $"|sig:0x{memberRef.Signature.GetHashCode():X8}";
            }

            return $"{declaringTypeName}.{methodName}{signatureKey}|0x{metadataToken:X8}";
        }

        // For called methods reconstructed using IL analysis - when we can't resolve the
        // called method to an exact method definition because the function is in another
        // assembly or the method is not defined in the metadata.
        public static string Get(IMethod method)
        {
            // 1. Declaring type
            var declaringType = method.DeclaringType;
            string fullTypeName = declaringType.FullName; // or use .FullName for a more readable name

            // 2. Method name
            string methodName = method.Name;

            // 3. Generic arity
            int genericArity = method.TypeParameters.Count;

            // 4. Parameter types
            var paramTypes = method.Parameters.Select(p => p.Type.FullName).ToList();
            string paramTypesKey = string.Join(",", paramTypes);

            // 5. Method attributes (visibility, static, etc.)
            string attrKey = method.Accessibility.ToString();
            if (method.IsStatic) attrKey += "|static";
            if (method.IsAbstract) attrKey += "|abstract";
            if (method.IsVirtual) attrKey += "|virtual";

            // 6. (Optional) Metadata token
            int metadataToken = MetadataTokens.GetToken(method.MetadataToken);

            // Compose the key
            return $"{fullTypeName}.{methodName}`{genericArity}({paramTypesKey})|{attrKey}|0x{metadataToken:X8}";
        }
    }
}
