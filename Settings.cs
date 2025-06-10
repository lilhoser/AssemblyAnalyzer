
namespace AssemblyAnalyzer
{
    public class Settings
    {
        public Settings() { }

        public string AssemblyPath { get; set; } = string.Empty;
        public string OutputPath { get; set; } = string.Empty;
        public bool IncludeFullProjectDecompilation { get; set; } = false;
        public bool RemoveDeadCode { get; set; } = false;
        public bool RemoveDeadStores { get; set; } = false;
        public bool GenerateNestedDirectories { get; set; } = false;
        public bool AttemptSymbolLoad { get; set; } = false;
        public string? PdbFilePath { get; set; } = string.Empty;
        public bool NoFormatting { get; set; } = false;
        public bool IgnoreCompilerGeneratedMethods { get; set; } = false;

        private static void PrintHelp()
        {
            Console.WriteLine("AssemblyAnalyzer");
            Console.WriteLine("Produces comprehensive program information about a dotnet assembly, such as type information, method signatures, and program decompilation.");
            Console.WriteLine("");
            Console.WriteLine("Usage: AssemblyAnalyzer [options]");
            Console.WriteLine("  --assembly <path>                         Path to the assembly to analyze (required)");
            Console.WriteLine("  --output-path <path>                      Path to write analysis results (required)");
            Console.WriteLine("  --include-full-project-decompilation      Include full project decompilation (default: false)");
            Console.WriteLine("  --help, -h                                Show this help menu");
            Console.WriteLine("Additional options related to decompilation:");
            Console.WriteLine("     --remove-dead-code                     Remove dead code (default: false)");
            Console.WriteLine("     --remove-dead-stores                   Remove dead stores (default: false)");
            Console.WriteLine("     --ignore-compiler-generated            Ignore compiler generated methods (default: false)");
            Console.WriteLine("     --nested-directories                   Generate nested directories for namespaces (default: false)");
            Console.WriteLine("     --attempt-symbol-load                  Attempts to load assembly symbols, can be slow (default: false)");
            Console.WriteLine("     --use-pdb-file <path>                  Full path to a PDB file to use during symbol load");
            Console.WriteLine("     --no-formatting                        Strip all formatting characters from decompilation output");
        }

        public static bool LoadSettings(string[] args, out Settings? _Settings)
        {
            _Settings = null;

            if (args.Length == 0)
            {
                PrintHelp();
                return false;
            }

            _Settings = new Settings();

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--assembly":
                        if (i + 1 < args.Length)
                            _Settings.AssemblyPath = args[++i];
                        break;
                    case "--output-path":
                        if (i + 1 < args.Length)
                            _Settings.OutputPath = args[++i];
                        break;
                    case "--remove-dead-code":
                        _Settings.RemoveDeadCode = true;
                        break;
                    case "--remove-dead-stores":
                        _Settings.RemoveDeadStores = true;
                        break;
                    case "--nested-directories":
                        _Settings.GenerateNestedDirectories = true;
                        break;
                    case "--include-full-project-decompilation":
                        _Settings.IncludeFullProjectDecompilation = true;
                        break;
                    case "--attempt-symbol-load":
                        _Settings.AttemptSymbolLoad = true;
                        break;
                    case "--ignore-compiler-generated":
                        _Settings.IgnoreCompilerGeneratedMethods = true;
                        break;
                    case "--use-pdb-file":
                        if (i + 1 < args.Length)
                        {
                            _Settings.AttemptSymbolLoad = true;
                            _Settings.PdbFilePath = args[++i];
                        }
                        break;
                    case "--no-formatting":
                        _Settings.NoFormatting = true;
                        break;
                    case "--help":
                    case "-h":
                        PrintHelp();
                        _Settings = null;
                        return true;
                    default:
                        PrintHelp();
                        return false;
                }
            }

            if (string.IsNullOrEmpty(_Settings.AssemblyPath))
            {
                Console.WriteLine("You must specify --assembly <path>.");
                return false;
            }

            if (!File.Exists(_Settings.AssemblyPath))
            {
                Console.WriteLine($"Assembly not found: {_Settings.AssemblyPath}");
                return false;
            }

            if (string.IsNullOrEmpty(_Settings.OutputPath))
            {
                Console.WriteLine("You must specify --output-path <path>.");
                return false;
            }

            if (!string.IsNullOrEmpty(_Settings.PdbFilePath) && !File.Exists(_Settings.PdbFilePath))
            {
                Console.WriteLine($"Pdb file not found: {_Settings.PdbFilePath}");
                return false;
            }
            return true;
        }
    }
}
