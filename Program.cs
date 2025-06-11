using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using System.Text.RegularExpressions;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.CSharp.ProjectDecompiler;
using ICSharpCode.Decompiler.DebugInfo;
using ICSharpCode.Decompiler.Metadata;
using ICSharpCode.Decompiler.TypeSystem;
using ICSharpCode.ILSpyX.PdbProvider;
using Microsoft.CodeAnalysis;

namespace AssemblyAnalyzer
{
    public class Program
    {
        private static int Main(string[] args)
        {
            if (!Settings.LoadSettings(args, out var settings))
            {
                return 1;
            }
            if (settings == null)
            {
                return 0; // --help
            }
            return AnalyzeAssembly(settings);
        }

        public static int AnalyzeAssembly(Settings settings)
        {
            string assemblyFileName = Path.GetFileNameWithoutExtension(settings.AssemblyPath);
            string projectPath = settings.OutputPath;
            using var stream = File.OpenRead(settings.AssemblyPath);
            using var peReader = new PEReader(stream);
            var metadataReader = peReader.GetMetadataReader();

            if (peReader.PEHeaders.PEHeader == null)
            {
                Console.Error.WriteLine("No PE header available");
                return 1;
            }

            var result = new
            {
                PEInformation = new
                {
                    FileSize = stream.Length,
                    ImageBase = peReader.PEHeaders.PEHeader.ImageBase,
                    EntryPointRVA = peReader.PEHeaders.PEHeader.AddressOfEntryPoint,
                    SectionAlignment = peReader.PEHeaders.PEHeader.SectionAlignment,
                    FileAlignment = peReader.PEHeaders.PEHeader.FileAlignment
                },
                Types = new List<object>(),
                ImportedFunctions = new List<object>(),
                ImportedTypes = new List<string>(),
                ExportedTypes = new List<string>(),
                References = new List<string>(),
                FullDecompilation = new List<string>()
            };

            try
            {
                Directory.CreateDirectory(projectPath);
                var assemblyFile = new PEFile(settings.AssemblyPath);
                var resolver = new UniversalAssemblyResolver(settings.AssemblyPath, false, assemblyFile.DetectTargetFrameworkId());
                var decompilerSettings = new DecompilerSettings(LanguageVersion.Latest)
                {
                    ThrowOnAssemblyResolveErrors = false,
                    RemoveDeadCode = settings.RemoveDeadCode,
                    RemoveDeadStores = settings.RemoveDeadStores,
                    UseSdkStyleProjectFormat = WholeProjectDecompiler.CanUseSdkStyleProjectFormat(assemblyFile),
                    UseNestedDirectoriesForNamespaces = settings.GenerateNestedDirectories,
                };
                if (settings.NoFormatting)
                {
                    decompilerSettings.CSharpFormattingOptions.IndentationString = "";
                }
                var typeSystem = new DecompilerTypeSystem(assemblyFile, resolver);
                var csharpDecompiler = new CSharpDecompiler(assemblyFile, resolver, decompilerSettings);

                // Types and Methods
                foreach (var typeDefHandle in metadataReader.TypeDefinitions)
                {
                    var typeDef = metadataReader.GetTypeDefinition(typeDefHandle);
                    var typeName = metadataReader.GetString(typeDef.Name);
                    var namespaceName = metadataReader.GetString(typeDef.Namespace);
                    var fullTypeName = string.IsNullOrEmpty(namespaceName) ? typeName : $"{namespaceName}.{typeName}";

                    var typeObj = new
                    {
                        Name = fullTypeName,
                        Kind = typeDef.Attributes.HasFlag(TypeAttributes.Interface) ? "Interface"
                            : typeDef.Attributes.HasFlag(TypeAttributes.Class) ? "Class"
                            : (typeDef.Attributes.HasFlag(TypeAttributes.Sealed) && typeDef.Attributes.HasFlag(TypeAttributes.SpecialName) ? "Enum" : "Unknown"),
                        Methods = new List<object>()
                    };

                    foreach (var methodHandle in typeDef.GetMethods())
                    {
                        var method = metadataReader.GetMethodDefinition(methodHandle);
                        if (method.IsCompilerGenerated(metadataReader) && settings.IgnoreCompilerGeneratedMethods)
                        {
                            continue;
                        }
                        var methodName = metadataReader.GetString(method.Name);
                        var context = new GenericContext(method.GetGenericParameters(), typeDef.GetGenericParameters(), metadataReader);
                        var signature = method.DecodeSignature<string, GenericContext>(new SignatureDecoder(), context);
                        var returnType = signature.ReturnType;
                        var parameters = signature.ParameterTypes.Select((t, i) => new { Type = t, Name = $"param{i + 1}" }).ToList();
                        var methodSize = 0;
                        string ilBytesStr = string.Empty;

                        if (method.RelativeVirtualAddress != 0)
                        {
                            var methodBody = peReader.GetMethodBody(method.RelativeVirtualAddress);
                            if (methodBody != null)
                            {
                                var ilBytes = methodBody.GetILBytes();
                                if (ilBytes != null)
                                {
                                    methodSize = ilBytes.Length;
                                }
                                ilBytesStr = (ilBytes != null && ilBytes.Length > 0)
                                        ? BitConverter.ToString(ilBytes).Replace("-", " ")
                                        : "<empty>";
                            }
                            else
                            {
                                ilBytesStr = "<none>";
                            }
                        }
                        else
                        {
                            ilBytesStr = "<abstract or external>";
                        }

                        string sourceText = string.Empty;
                        var stringLiterals = new List<string>();
                        try
                        {
                            var decompiledNode = csharpDecompiler.Decompile(methodHandle);
                            sourceText = decompiledNode.ToString();
                            if (!string.IsNullOrEmpty(sourceText))
                            {
                                sourceText = Regex.Unescape(sourceText.Replace(Environment.NewLine, " ").Replace("\t",""));
                                decompiledNode.AcceptVisitor(new StringLiteralVisitor(stringLiterals));
                            }
                        }
                        catch
                        {
                            sourceText = "    <decompilation failed>";
                        }
                        ((List<object>)typeObj.Methods).Add(new
                        {
                            Name = methodName,
                            MethodSize = methodSize,
                            Parameters = parameters,
                            ReturnType = returnType,
                            RVA = $"0x{method.RelativeVirtualAddress:X}",
                            ILBytes = ilBytesStr,
                            DecompiledSource = sourceText,
                            StringLiterals = stringLiterals
                        });
                    }

                    ((List<object>)result.Types).Add(typeObj);
                }

                // Imported Methods
                foreach (var memberRefHandle in metadataReader.MemberReferences)
                {
                    var memberRef = metadataReader.GetMemberReference(memberRefHandle);
                    if (memberRef.Parent.Kind == HandleKind.TypeReference)
                    {
                        var typeRefHandle = (TypeReferenceHandle)memberRef.Parent;
                        var typeRef = metadataReader.GetTypeReference(typeRefHandle);
                        var typeNamespace = metadataReader.GetString(typeRef.Namespace);
                        var typeName = metadataReader.GetString(typeRef.Name);
                        var fullTypeName = string.IsNullOrEmpty(typeNamespace) ? typeName : $"{typeNamespace}.{typeName}";

                        if (memberRef.GetKind() == MemberReferenceKind.Method)
                        {
                            var methodName = metadataReader.GetString(memberRef.Name);
                            string fullMethodName = $"{fullTypeName}.{methodName}";
                            if (methodName.StartsWith("."))
                            {
                                fullMethodName = $"{fullTypeName}{methodName}";
                            }

                            ((List<object>)result.ImportedFunctions).Add(new
                            {
                                FullTypeName = fullMethodName,
                            });
                        }
                    }
                }

                foreach (var importedType in metadataReader.TypeReferences)
                {
                    var typeRef = metadataReader.GetTypeReference(importedType);
                    var typeRefName = metadataReader.GetString(typeRef.Name);
                    var typeRefNamespace = metadataReader.GetString(typeRef.Namespace);
                    result.ImportedTypes.Add($"{typeRefNamespace}.{typeRefName}");
                }

                // Exported Types
                foreach (var exportedTypeHandle in metadataReader.ExportedTypes)
                {
                    var exportedType = metadataReader.GetExportedType(exportedTypeHandle);
                    var exportedTypeName = metadataReader.GetString(exportedType.Name);
                    var exportedTypeNamespace = metadataReader.GetString(exportedType.Namespace);
                    result.ExportedTypes.Add($"{exportedTypeNamespace}.{exportedTypeName}");
                }

                // Full Decompilation
                if (settings.IncludeFullProjectDecompilation)
                {
                    IDebugInfoProvider? debugInfoProvider = null;
                    if (settings.AttemptSymbolLoad)
                    {
                        if (!string.IsNullOrEmpty(settings.PdbFilePath))
                        {
                            debugInfoProvider = DebugInfoUtils.FromFile(assemblyFile, settings.PdbFilePath);
                        }
                        else
                        {
                            debugInfoProvider = DebugInfoUtils.LoadSymbols(assemblyFile);
                        }
                    }

                    var projectFilePath = Path.Combine(projectPath, $"{assemblyFileName}.csproj");
                    var decompiler = new WholeProjectDecompiler(decompilerSettings, resolver, null, resolver, debugInfoProvider);
                    using (var projectFileWriter = new StreamWriter(File.OpenWrite(projectFilePath)))
                    {
                        decompiler.DecompileProject(assemblyFile, Path.GetDirectoryName(projectFilePath), projectFileWriter);
                    }
                    result.FullDecompilation.Add($"Decompiled project written to: {projectFilePath}");
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error during assembly analysis: {ex.Message}");
                return 1;
            }

            try
            {
                var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
                var outputFilePath = Path.Combine(projectPath, "assembly_analysis.json");
                File.WriteAllText(outputFilePath, json);
                Console.WriteLine($"Analysis result written to: {outputFilePath}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error serializing result to JSON: {ex.Message}");
                return 1;
            }
            
            return 0;
        }
    }
}