[<AutoOpen>]
module Fun.Fsx.ProjectExtensions

open System
open System.IO

type Project with

    static member private GetFsiFilePath(projectDir: string) = Path.Combine(projectDir, "Fsi.fs")

    member project.CreateOrUpdate() =
        let projectDir = Path.GetDirectoryName project.ProjectFile

        if File.Exists project.ProjectFile then File.Delete project.ProjectFile
        use projectWriter = new StreamWriter(project.ProjectFile)

        projectWriter.WriteLine("<Project Sdk=\"{0}\">", defaultValueArg project.Sdk "Microsoft.NET.Sdk")

        projectWriter.WriteLine("  <PropertyGroup>")

        projectWriter.Write("    <TargetFramework>")
        projectWriter.Write(project.Target)
        projectWriter.WriteLine("</TargetFramework>")

        let mutable hasOutputType = false
        if project.Properties.Length > 0 then
            for property in project.Properties do
                if property.Name = "OutputType" then
                    hasOutputType <- true
                projectWriter.WriteLine("    <{0}>{1}</{0}>", property.Name, property.Value)

        if not hasOutputType then
            projectWriter.WriteLine("    <OutputType>Exe</OutputType>")

        projectWriter.WriteLine("  </PropertyGroup>")

        projectWriter.WriteLine("  <ItemGroup>")
        
        Project.WriteMockFsi(projectDir)
        projectWriter.WriteLine("    <Compile Include=\"{0}\" />", Path.GetFileName(Project.GetFsiFilePath(projectDir)))

        for fs in project.OtherScripts do
            projectWriter.WriteLine("    <Compile Include=\"{0}\" />", Path.GetFileName(fs))
        projectWriter.WriteLine("    <Compile Include=\"{0}\" />", Path.GetFileName(project.EntryScript))
        projectWriter.WriteLine("  </ItemGroup>")

        if project.Packages.Length > 0 then
            projectWriter.WriteLine("  <ItemGroup>")
            for package in project.Packages do
                projectWriter.WriteLine("    <PackageReference Include=\"{0}\" Version=\"{1}\" />", package.Name, package.Version)
            projectWriter.WriteLine("  </ItemGroup>")

        if project.Assemblies.Length > 0 then
            projectWriter.WriteLine("  <ItemGroup>")
            for assembly in project.Assemblies do
                projectWriter.WriteLine("    <Reference Include=\"{0}\">", Path.GetFileNameWithoutExtension(assembly))
                projectWriter.WriteLine("      <HintPath>{0}</HintPath>", assembly)
                projectWriter.WriteLine("    </Reference>")
            projectWriter.WriteLine("  </ItemGroup>")

        if project.Projects.Length > 0 then
            projectWriter.WriteLine("  <ItemGroup>")
            for proj in project.Projects do
                projectWriter.WriteLine("    <ProjectReference Include=\"{0}\" />", proj)
            projectWriter.WriteLine("  </ItemGroup>")

        projectWriter.WriteLine("</Project>")


    static member Parse(entryScript: string, projectFile: string, scriptArgs: string list) =
        let projectDir = Path.GetDirectoryName projectFile

        let mutable sdk = ValueOption<string>.None
        let targets = Collections.Generic.HashSet<string>()
        let packages = Collections.Generic.HashSet<Package>()
        let projects = Collections.Generic.HashSet<string>()
        let assemblies = Collections.Generic.HashSet<string>()
        let properties = Collections.Generic.HashSet<Property>()
        let otherScripts = Collections.Generic.HashSet<string>()

        let rec processFs writeNamespace (filePath: string) =
            let file = Path.Combine(projectDir, Path.GetFileName(filePath))
            if File.Exists file then File.Delete file

            use fileStream = File.OpenWrite(file)
            use fileWriter = new StreamWriter(fileStream)

            fileWriter.WriteLine($"#line 0 @\"{filePath}\"")

            if writeNamespace then
                let name = Path.GetFileNameWithoutExtension(filePath)
                fileWriter.Write("module ``")
                fileWriter.Write(Char.ToUpperInvariant(name[0]))
                fileWriter.Write(name.AsSpan().Slice(1))
                fileWriter.Write("``")
                fileWriter.WriteLine()
                fileWriter.WriteLine()
                fileWriter.Flush()


            for line in File.ReadLines(filePath) do
                if line.StartsWith("//sdk ") && filePath = entryScript then
                    sdk <- line.Substring(6).Trim() |> ValueSome

                elif line.StartsWith("//property ") then
                    let parts = line.Substring(11).Split('=', 2)
                    if parts.Length = 2 then
                        if parts[0] = "TargetFramework" then
                            targets.Add(parts[1].Trim()) |> ignore
                        else if parts[0] = "TargetFrameworks" then
                            for t in parts[1].Split(';') do
                                targets.Add(t.Trim()) |> ignore
                        else
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
                            Path.Combine(Path.GetDirectoryName(filePath), file)
                        |> Path.GetFullPath
                    if File.Exists(filePath) then
                        if filePath.EndsWith ".fs" || filePath.EndsWith ".fsx" then
                            otherScripts.Add(filePath) |> ignore
                            processFs true filePath
                        else if filePath.EndsWith ".dll" then
                            assemblies.Add(filePath) |> ignore
                        else
                            failwithf "Unsupported file type in #load: %s" line
                    else
                        failwithf "The file '%s' does not exist." file

                else
                    fileWriter.WriteLine(line)

        processFs false entryScript

        if targets.Count = 0 then
            failwith "No TargetFramework specified. Please specify at least one TargetFramework using '//property TargetFramework=...' directive."

        {
            Sdk = sdk
            Target = targets |> Seq.head
            ProjectFile = projectFile
            EntryScript = entryScript
            ScriptArgs = scriptArgs
            Packages = packages |> Seq.sortBy _.Name |> Seq.toList
            Projects = projects |> Seq.sort |> Seq.toList
            Assemblies = assemblies |> Seq.toList
            Properties = properties |> Seq.toList
            OtherScripts = otherScripts |> Seq.toList
        }


    static member WriteMockFsi(projectDir: string) =
        File.WriteAllText(Project.GetFsiFilePath projectDir, $$"""[<AutoOpen>]
module FsiMock

[<Sealed>]
type InteractiveSession() =
  
  member _.AddPrintTransformer(fn: 'T -> obj): unit = ()
  
  member _.AddPrinter(fn: 'T -> string): unit = ()
  
  member val CommandLineArgs: string array = System.Environment.GetCommandLineArgs() |> Seq.skip 1 |> Seq.toArray with get, set
  
  // member EventLoop: IEventLoop with get, set
  
  member val FloatingPointFormat: string = "g10" with get, set
  
  member val FormatProvider: System.IFormatProvider = System.Globalization.CultureInfo.InvariantCulture with get, set
  
  member val PrintDepth: int = 100 with get, set
  
  member val PrintLength: int = 100 with get, set
  
  member val PrintSize: int = 10000 with get, set
  
  member val PrintWidth: int = 78 with get, set
  
  member val ShowDeclarationValues: bool = true with get, set
  
  member val ShowIEnumerable: bool = true with get, set
  
  member val ShowProperties: bool = true with get, set

let fsi = InteractiveSession()

"""     )