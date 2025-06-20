using System.Reflection.Metadata;
using System.Text.Json.Serialization;

namespace AssemblyAnalyzer
{
    internal class DataModel
    {
        public List<TypeModel> Types { get; set; } = new List<TypeModel>();
        public List<ImportedFunctionModel> ImportedFunctions { get; set; } = new List<ImportedFunctionModel>();
        public List<ImportedTypeModel> ImportedTypes { get; set; } = new List<ImportedTypeModel>();
        public List<ExportedTypeModel> ExportedTypes { get; set; } = new List<ExportedTypeModel>();
        public PEInformationModel PEInformation { get; set; } = new PEInformationModel();
    }

    internal class PEInformationModel
    {
        public long FileSize { get; set; }
        public ulong ImageBase { get; set; }
        public int EntryPointRVA { get; set; }
        public long SectionAlignment { get; set; }
        public long FileAlignment { get; set; }
    }

    internal class ImportedFunctionModel
    {
        public string FullTypeName { get; set; } = string.Empty;
    }

    internal class ImportedTypeModel
    {
        public string FullTypeName { get; set; } = string.Empty;
    }

    internal class ExportedTypeModel
    {
        public string FullTypeName { get; set; } = string.Empty;
    }

    internal class TypeModel
    {
        public string Name { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;
        public List<MethodModel> Methods { get; set; } = new List<MethodModel>();
    }

    internal class MethodModel
    {
        public string Name { get; set; } = string.Empty;
        public int RVA { get; set; }
        public long MethodSize { get; set; }
        public List<MethodParameterModel> Parameters { get; set; } = new List<MethodParameterModel>();
        public string ReturnType { get; set; } = string.Empty;
        public List<string> StringLiterals { get; set; } = new List<string>();
        public string ILBytes { get; set; } = string.Empty;
        public string DecompiledSource { get; set; } = string.Empty;
        public List<CalledMethodModel> CalledMethods { get; set; } = new List<CalledMethodModel>();
        [JsonIgnore]
        public Dictionary<Handle, string> CalledMethodHandles { get; set; } = new Dictionary<Handle, string>();
    }

    internal class MethodParameterModel
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
    }

    internal class CalledMethodModel
    {
        public string Name { get; set; } = string.Empty;
        public long Address { get; set; }
    }
}