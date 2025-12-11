# fsx

This is a project to run a script file with better performance than fsharp default fsi.

The concept is very simple:

- use the specified script as the entry
- build dependencies
- if dependency script files are modified then create the project
- if dependency script files are not modified then use dotnet run without restore and build flags


## TODOs

- [ ] Dry run to build dependency for lock content
