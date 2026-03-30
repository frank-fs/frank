using System.IO;
using System.Text.Json;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Frank.Cli.MSBuild
{
    public class WriteResolvedOptionsJson : Task
    {
        [Required]
        public string OutputPath { get; set; }

        [Required]
        public ITaskItem[] SourceFiles { get; set; }

        [Required]
        public ITaskItem[] ReferencePaths { get; set; }

        public string DefineConstants { get; set; }

        public string OtherFlags { get; set; }

        public override bool Execute()
        {
            // Ensure output directory exists
            var dir = Path.GetDirectoryName(OutputPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            using var stream = new FileStream(OutputPath, FileMode.Create, FileAccess.Write);
            using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });

            writer.WriteStartObject();

            // sourceFiles — use FullPath metadata for OS-native separators
            writer.WriteStartArray("sourceFiles");
            foreach (var item in SourceFiles)
                writer.WriteStringValue(item.GetMetadata("FullPath"));
            writer.WriteEndArray();

            // references — use Identity (already full path from ResolveAssemblyReferences)
            writer.WriteStartArray("references");
            foreach (var item in ReferencePaths)
                writer.WriteStringValue(item.ItemSpec);
            writer.WriteEndArray();

            // defines — split semicolons from $(DefineConstants)
            writer.WriteStartArray("defines");
            if (!string.IsNullOrEmpty(DefineConstants))
            {
                foreach (var define in DefineConstants.Split(';'))
                {
                    var trimmed = define.Trim();
                    if (trimmed.Length > 0)
                        writer.WriteStringValue(trimmed);
                }
            }
            writer.WriteEndArray();

            // otherFlags — naive whitespace split; corrupts flags with quoted paths
            // (e.g., --pathmap:"..."). Matches the F# side limitation in ProjectLoader.fs.
            writer.WriteStartArray("otherFlags");
            if (!string.IsNullOrEmpty(OtherFlags))
            {
                foreach (var flag in OtherFlags.Split(' ', '\t'))
                {
                    var trimmed = flag.Trim();
                    if (trimmed.Length > 0)
                        writer.WriteStringValue(trimmed);
                }
            }
            writer.WriteEndArray();

            writer.WriteEndObject();
            writer.Flush();

            Log.LogMessage(MessageImportance.Low, "Wrote resolved options to {0}", OutputPath);
            return true;
        }
    }
}
