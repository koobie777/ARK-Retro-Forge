using System.Text;
using System.Text.RegularExpressions;

namespace ARK.Core.Systems.PSX;

public enum PsxCueOperationType
{
    Create,
    Update,
    Scrape
}

public record PsxCueOperation(
    PsxCueOperationType Type,
    string TargetCuePath,
    string NewContent,
    string Details
);

public class PsxCuePlanner
{
    private readonly string _root;
    private readonly bool _recursive;
    private readonly HashSet<string> _binExtensions = new(StringComparer.OrdinalIgnoreCase) { ".bin", ".img", ".iso" };

    public PsxCuePlanner(string root, bool recursive)
    {
        _root = root;
        _recursive = recursive;
    }

    public Task<IEnumerable<PsxCueOperation>> PlanCueCreationsAsync(bool force)
    {
        var ops = new List<PsxCueOperation>();
        var searchOption = _recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        
        var bins = Directory.EnumerateFiles(_root, "*.*", searchOption)
            .Where(f => _binExtensions.Contains(Path.GetExtension(f)))
            .ToList();

        foreach (var bin in bins)
        {
            var cuePath = Path.ChangeExtension(bin, ".cue");
            if (File.Exists(cuePath) && !force)
            {
                continue;
            }

            var binName = Path.GetFileName(bin);
            var sb = new StringBuilder();
            sb.AppendLine($"FILE \"{binName}\" BINARY");
            sb.AppendLine("  TRACK 01 MODE2/2352");
            sb.AppendLine("    INDEX 01 00:00:00");

            ops.Add(new PsxCueOperation(
                PsxCueOperationType.Create,
                cuePath,
                sb.ToString(),
                $"Generate CUE for {binName}"
            ));
        }

        return Task.FromResult<IEnumerable<PsxCueOperation>>(ops);
    }

    public async Task<IEnumerable<PsxCueOperation>> PlanCueUpdatesAsync()
    {
        var ops = new List<PsxCueOperation>();
        var searchOption = _recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        
        var cues = Directory.EnumerateFiles(_root, "*.cue", searchOption).ToList();

        foreach (var cuePath in cues)
        {
            var content = await File.ReadAllTextAsync(cuePath);
            var lines = File.ReadAllLines(cuePath);
            var dir = Path.GetDirectoryName(cuePath) ?? _root;
            var modified = false;
            var newLines = new List<string>();
            var details = new List<string>();

            foreach (var line in lines)
            {
                if (line.TrimStart().StartsWith("FILE"))
                {
                    // FILE "filename.bin" BINARY
                    var match = Regex.Match(line, "FILE \"(.*?)\"");
                    if (match.Success)
                    {
                        var binName = match.Groups[1].Value;
                        var binPath = Path.Combine(dir, binName);

                        if (!File.Exists(binPath))
                        {
                            // Try to find matching BIN in same folder
                            var candidate = Directory.EnumerateFiles(dir, "*.*")
                                .FirstOrDefault(f => 
                                    Path.GetFileNameWithoutExtension(f).Equals(Path.GetFileNameWithoutExtension(cuePath), StringComparison.OrdinalIgnoreCase) &&
                                    _binExtensions.Contains(Path.GetExtension(f)));

                            if (candidate != null)
                            {
                                var newBinName = Path.GetFileName(candidate);
                                var newLine = line.Replace(binName, newBinName);
                                newLines.Add(newLine);
                                modified = true;
                                details.Add($"Fixed ref: {binName} -> {newBinName}");
                                continue;
                            }
                        }
                    }
                }
                newLines.Add(line);
            }

            if (modified)
            {
                ops.Add(new PsxCueOperation(
                    PsxCueOperationType.Update,
                    cuePath,
                    string.Join(Environment.NewLine, newLines),
                    string.Join(", ", details)
                ));
            }
        }

        return ops;
    }
}
