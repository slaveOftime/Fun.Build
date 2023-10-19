namespace Fun.Build

open Fun.Result

module Windows =
    let private deployToIISOptions = {|
        host = EnvArg.Create("IIS_SERVER_COMPUTER_NAME")
        user = EnvArg.Create("IIS_SERVER_USERNAME")
        pwd = EnvArg.Create("IIS_SERVER_PASSWORD")
        appName = EnvArg.Create("RECYCLE_APP_NAME")
        siteName = EnvArg.Create("IIS_WEBSITE_NAME")
        targetDir = EnvArg.Create("WEBSITE_CONTENT_PATH")
    |}

    let stage_deployToIIS (zippedPackageFile: string) =
        stage "deploy to iis" {
            whenEnvVar deployToIISOptions.host
            whenEnvVar deployToIISOptions.user
            whenEnvVar deployToIISOptions.pwd
            whenEnvVar deployToIISOptions.appName
            whenEnvVar deployToIISOptions.siteName
            whenEnvVar deployToIISOptions.targetDir
            run (fun ctx -> asyncResult {
                let host = ctx.GetEnvVar deployToIISOptions.host.Name
                let user = ctx.GetEnvVar deployToIISOptions.user.Name
                let pwd = ctx.GetEnvVar deployToIISOptions.pwd.Name
                let appName = ctx.GetEnvVar deployToIISOptions.appName.Name
                let siteName = ctx.GetEnvVar deployToIISOptions.siteName.Name
                let targetDir = ctx.GetEnvVar deployToIISOptions.targetDir.Name

                let msdeploy (x: string) = ctx.RunSensitiveCommand($"'C:/Program Files (x86)/IIS/Microsoft Web Deploy V3/msdeploy.exe' {x}")

                do!
                    msdeploy
                        $"-verb:sync -allowUntrusted -source:recycleApp -dest:recycleApp=\"{appName}\",recycleMode=\"StopAppPool\",computerName=\"{host}/msdeploy.axd?site={siteName}\",username=\"{user}\",password=\"{pwd}\",AuthType=\"Basic\""

                do!
                    msdeploy
                        $"-verb:sync -allowUntrusted -source:package=\"{zippedPackageFile}\" -dest:contentPath=\"{targetDir}\",computerName=\"{host}/msdeploy.axd?site={siteName}\",username=\"{user}\",password=\"{pwd}\",AuthType=\"Basic\" -enableRule:DoNotDeleteRule"
                do!
                    msdeploy
                        $"-verb:sync -allowUntrusted -source:recycleApp -dest:recycleApp=\"{appName}\",recycleMode=\"StartAppPool\",computerName=\"{host}/msdeploy.axd?site={siteName}\",username=\"{user}\",password=\"{pwd}\",AuthType=\"Basic\""
            })
        }
