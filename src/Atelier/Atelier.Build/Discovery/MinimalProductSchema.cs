using YamlDotNet.Serialization;

namespace Atelier.Build.Discovery;

public class MinimalProductSchema
{
        [YamlMember(Alias = "name")]
    public string Name { get; set; } = string.Empty;

        [YamlMember(Alias = "version")]
    public string Version { get; set; } = "1.0.0";

        [YamlMember(Alias = "description")]
    public string? Description { get; set; }

        [YamlMember(Alias = "dependencies")]
    public List<string>? Dependencies { get; set; }

        [YamlMember(Alias = "build")]
    public ProductBuildSchema? Build { get; set; }
}

public class ProductBuildSchema
{
        [YamlMember(Alias = "configuration")]
    public string Configuration { get; set; } = "Release";

        [YamlMember(Alias = "treat_warnings_as_errors")]
    public bool TreatWarningsAsErrors { get; set; } = false;

        [YamlMember(Alias = "msbuild_args")]
    public List<string>? MsBuildArgs { get; set; }
}
