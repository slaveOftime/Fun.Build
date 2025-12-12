namespace Fun.Fsx

type Package = { Name: string; Version: string }

type Property = { Name: string; Value: string }

type Project = {
    Sdk: string voption
    Target: string
    Properties: Property list
    ProjectFile: string
    EntryScript: string
    ScriptArgs: string list
    OtherScripts: string list
    Packages: Package list
    Projects: string list
    Assemblies: string list
}
