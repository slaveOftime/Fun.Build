namespace Fsx

open System
open System.IO

type Project =

    static member Process(entryFs: string, projectFile: string) =
        let projectDir = Path.GetDirectoryName projectFile

        let mutable sdk = ValueOption<string>.None
        let packages = Collections.Generic.HashSet<Package>()
        let projects = Collections.Generic.HashSet<string>()
        let assemblies = Collections.Generic.HashSet<string>()
        let properties = Collections.Generic.HashSet<Property>()
        let otherfs = Collections.Generic.HashSet<string>()

        let rec processFs writeNamespace (entryFs: string) =
            let file = Path.Combine(projectDir, Path.GetFileName(entryFs))
            if File.Exists file then File.Delete file

            use fileStream = File.OpenWrite(file)
            use writer = new StreamWriter(fileStream)

            if writeNamespace then
                let name = Path.GetFileNameWithoutExtension(entryFs)
                writer.Write("module ``")
                writer.Write(Char.ToUpperInvariant(name[0]))
                writer.Write(name.AsSpan().Slice(1))
                writer.Write("``")
                writer.WriteLine()
                writer.WriteLine()
                writer.Flush()

            for line in File.ReadLines(entryFs) do
                if line.StartsWith("//sdk ") then
                    sdk <- line.Substring(6).Trim() |> ValueSome

                elif line.StartsWith("//property ") then
                    let parts = line.Substring(11).Split('=', 2)
                    if parts.Length = 2 then
                        properties.Add({ Name = parts[0].Trim(); Value = parts[1].Trim() }) |> ignore
                    else
                        failwithf "Invalid property format: %s" line

                elif line.StartsWith("#r \"nuget:") then
                    let line = line.Substring(10, line.LastIndexOf '"' - 10).Trim()
                    let parts = line.Split(',')
                    if parts.Length = 2 then
                        packages.Add { Name = parts[0].Trim(); Version = parts[1].Trim() } |> ignore
                    else
                        packages.Add { Name = parts[0].Trim(); Version = "*" } |> ignore

                elif line.StartsWith("#r ") then
                    let line = line.Substring(4).Trim().Trim('"')
                    if line.EndsWith ".dll" then
                        assemblies.Add(Path.GetFullPath line) |> ignore
                    else
                        failwithf "Unsupported reference type in #r: %s" line

                elif line.StartsWith("#load ") then
                    let startIndex = line.IndexOf('"') + 1
                    let endIndex = line.LastIndexOf('"')
                    let file = line.Substring(startIndex, endIndex - startIndex)
                    let filePath =
                        if Path.IsPathRooted(file) then
                            file
                        else
                            Path.Combine(Path.GetDirectoryName(entryFs), file)
                        |> Path.GetFullPath
                    if File.Exists(filePath) then
                        if filePath.EndsWith ".fs" || filePath.EndsWith ".fsx" then
                            otherfs.Add(filePath) |> ignore
                            processFs true filePath
                        else if filePath.EndsWith ".dll" then
                            assemblies.Add(filePath) |> ignore
                        else
                            failwithf "Unsupported file type in #load: %s" line
                    else
                        failwithf "The file '%s' does not exist." file

                else
                    writer.WriteLine(line)

        processFs false entryFs

        if File.Exists projectFile then File.Delete projectFile

        use projectStream = File.OpenWrite(projectFile)
        use projectWriter = new StreamWriter(projectStream)

        projectWriter.WriteLine("<Project Sdk=\"{0}\">", defaultValueArg sdk "Microsoft.NET.Sdk")

        projectWriter.WriteLine("  <PropertyGroup>")

        if properties.Count > 0 then
            for property in properties do
                projectWriter.WriteLine("    <{0}>{1}</{0}>", property.Name, property.Value)

        projectWriter.WriteLine("  </PropertyGroup>")

        projectWriter.WriteLine("  <ItemGroup>")
        for fs in otherfs do
            projectWriter.WriteLine("    <Compile Include=\"{0}\" />", Path.GetFileName(fs))
        projectWriter.WriteLine("    <Compile Include=\"{0}\" />", Path.GetFileName(entryFs))
        projectWriter.WriteLine("  </ItemGroup>")

        if packages.Count > 0 then
            projectWriter.WriteLine("  <ItemGroup>")
            for package in packages do
                projectWriter.WriteLine("    <PackageReference Include=\"{0}\" Version=\"{1}\" />", package.Name, package.Version)
            projectWriter.WriteLine("  </ItemGroup>")

        if assemblies.Count > 0 then
            projectWriter.WriteLine("  <ItemGroup>")
            for assembly in assemblies do
                projectWriter.WriteLine("    <Reference Include=\"{0}\">", Path.GetFileNameWithoutExtension(assembly))
                projectWriter.WriteLine("      <HintPath>{0}</HintPath>", assembly)
                projectWriter.WriteLine("    </Reference>")
            projectWriter.WriteLine("  </ItemGroup>")

        if projects.Count > 0 then
            projectWriter.WriteLine("  <ItemGroup>")
            for proj in projects do
                projectWriter.WriteLine("    <ProjectReference Include=\"{0}\" />", proj)
            projectWriter.WriteLine("  </ItemGroup>")

        projectWriter.WriteLine("</Project>")
