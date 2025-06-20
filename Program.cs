using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.CSharp.ProjectDecompiler;
using ICSharpCode.Decompiler.DebugInfo;
using ICSharpCode.Decompiler.IL;
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

            var result = new DataModel()
            {
                PEInformation = new PEInformationModel
                {
                    FileSize = stream.Length,
                    ImageBase = peReader.PEHeaders.PEHeader.ImageBase,
                    EntryPointRVA = peReader.PEHeaders.PEHeader.AddressOfEntryPoint,
                    SectionAlignment = peReader.PEHeaders.PEHeader.SectionAlignment,
                    FileAlignment = peReader.PEHeaders.PEHeader.FileAlignment
                }
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
                var ilReader = new ILReader(typeSystem.MainModule);
                var csharpDecompiler = new CSharpDecompiler(assemblyFile, resolver, decompilerSettings);
                var metadataMethodLookup = new Dictionary<Handle, MethodDefinition>();

                // Types and Methods
                foreach (var typeDefHandle in metadataReader.TypeDefinitions)
                {
                    var typeDef = metadataReader.GetTypeDefinition(typeDefHandle);
                    var typeName = UniqueTypeNaming.Get(metadataReader, typeDefHandle);
                    var typeObj = new TypeModel()
                    {
                        Name = typeName,
                        Kind = typeDef.Attributes.HasFlag(TypeAttributes.Interface) ? "Interface"
                            : typeDef.Attributes.HasFlag(TypeAttributes.Class) ? "Class"
                            : (typeDef.Attributes.HasFlag(TypeAttributes.Sealed) && typeDef.Attributes.HasFlag(TypeAttributes.SpecialName) ? "Enum" : "Unknown"),
                        Methods = new List<MethodModel>()
                    };

                    foreach (var methodHandle in typeDef.GetMethods())
                    {
                        var method = metadataReader.GetMethodDefinition(methodHandle);
                        if (method.IsCompilerGenerated(metadataReader) && settings.IgnoreCompilerGeneratedMethods)
                        {
                            continue;
                        }

                        var methodName = UniqueMethodNaming.Get(metadataReader, typeDefHandle, methodHandle);
                        var context = new GenericContext(method.GetGenericParameters(), typeDef.GetGenericParameters(), metadataReader);
                        var signature = method.DecodeSignature<string, GenericContext>(new SignatureDecoder(), context);
                        var returnType = signature.ReturnType;
                        var parameters = signature.ParameterTypes
                            .Select((t, i) => new MethodParameterModel { Type = t, Name = $"param{i + 1}" })
                            .ToList();
                        var methodSize = 0;
                        string ilBytesStr = string.Empty;
                        var calledMethodHandles = new Dictionary<Handle, string>();

                        if (method.RelativeVirtualAddress != 0)
                        {
                            var methodBody = peReader.GetMethodBody(method.RelativeVirtualAddress);
                            if (methodBody != null)
                            {
                                var ilBytes = methodBody.GetILBytes();
                                if (ilBytes != null)
                                {
                                    methodSize = ilBytes.Length;
                                    var ilFunctionBody = assemblyFile.GetMethodBody(method.RelativeVirtualAddress);
                                    var il = ilReader.ReadIL(methodHandle, ilFunctionBody);
                                    foreach (var inst in il.Descendants.OfType<CallInstruction>())
                                    {
                                        var calledMethodHandle = inst.Method;
                                        if (calledMethodHandle == null)
                                        {
                                            continue;
                                        }
                                        if (!calledMethodHandles.ContainsKey(calledMethodHandle.MetadataToken))
                                        {
                                            var fallbackName = UniqueMethodNaming.Get(calledMethodHandle);
                                            calledMethodHandles.Add(calledMethodHandle.MetadataToken, fallbackName);
                                        }
                                    }
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

                        metadataMethodLookup.Add(methodHandle, method);
                        typeObj.Methods.Add(new MethodModel()
                        {
                            Name = methodName,
                            MethodSize = methodSize,
                            Parameters = parameters,
                            ReturnType = returnType,
                            RVA = method.RelativeVirtualAddress,
                            ILBytes = ilBytesStr,
                            DecompiledSource = sourceText,
                            StringLiterals = stringLiterals,
                            CalledMethodHandles = calledMethodHandles
                        });
                    }

                    result.Types.Add(typeObj);
                }

                // Fixup called functions now that all functions have been visited.
                // This involves resolving method token to method definition.
                foreach (var type in result.Types)
                {
                    foreach (var method in type.Methods)
                    {
                        foreach (var kvp in method.CalledMethodHandles)
                        {
                            var handle = kvp.Key;
                            var fallbackName = kvp.Value;
                            if (!metadataMethodLookup.TryGetValue(handle, out var calledMethodDef))
                            {
                                // any called function not in our assembly (ie an import) won't be
                                // resolved this way. without also analyzing those imported assemblies,
                                // or without symbol information, we won't have an RVA or complete
                                // method signature with which to form a unique method name
                                // (see UniqueNaming.cs), so such calls cannot be used in callgraph
                                // analysis. in this case we use a fallback name with no address.
                                // the fallback name is produced directly from the IMethod at the
                                // call site which is less precise than a full MethodDefinition.
                                method.CalledMethods.Add(new CalledMethodModel()
                                {
                                    Name = fallbackName,
                                    Address = 0
                                });
                            }
                            else
                            {
                                method.CalledMethods.Add(new CalledMethodModel()
                                {
                                    Name = method.Name,
                                    Address = method.RVA
                                });
                            }
                        }
                    }
                }

                // Imported Methods
                foreach (var memberRefHandle in metadataReader.MemberReferences)
                {
                    var memberRef = metadataReader.GetMemberReference(memberRefHandle);
                    if (memberRef.Parent.Kind == HandleKind.TypeReference &&
                        memberRef.GetKind() == MemberReferenceKind.Method)
                    {
                        var methodName = UniqueMethodNaming.Get(metadataReader, memberRefHandle);
                        if (!result.ImportedFunctions.Any(result => result.FullTypeName == methodName))
                        {
                            result.ImportedFunctions.Add(new ImportedFunctionModel()
                            {
                                FullTypeName = methodName,
                            });
                        }
                    }
                }

                // Imported Types
                foreach (var importedType in metadataReader.TypeReferences)
                {
                    var typeName = UniqueTypeNaming.Get(metadataReader, importedType);
                    result.ImportedTypes.Add(new ImportedTypeModel()
                    {
                        FullTypeName = typeName
                    });
                }

                // Exported Types
                foreach (var exportedTypeHandle in metadataReader.ExportedTypes)
                {
                    var typeName = UniqueTypeNaming.Get(metadataReader, exportedTypeHandle);
                    result.ExportedTypes.Add(new ExportedTypeModel()
                    {
                        FullTypeName = typeName
                    });
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
                    Console.WriteLine($"Decompiled project written to: {projectFilePath}");
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error during assembly analysis: {ex.Message}");
                return 1;
            }

            try
            {
                var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { 
                    WriteIndented = true,
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                });
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