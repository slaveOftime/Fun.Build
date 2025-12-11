namespace Fsx

type Package = { Name: string; Version: string }

type Property = { Name: string; Value: string }

type FsEntry = {
    Sdk: string voption
    Packages: Package list
    Projects: string list
    Assemblies: string list
    Properties: Property list
    OtherFs: string list
}