open Fun.Build
open System.Net.Http

pipeline "demo" {
    stage "run something" {
        echo "hi"
        stage "fetch bing" {
            whenCmdArg "--bing" "" "fetch bing home page"
            run (fun _ -> task {
                use client = new HttpClient()
                let! result = client.GetAsync("https://www.bing.com")
                if result.IsSuccessStatusCode then
                    let! content = result.Content.ReadAsStringAsync()
                    printfn $"Fetch content {content.Length} chars"
                    return Ok()
                else
                    return Error $"Failed with code {result.StatusCode}"
            })
        }
    }
    runIfOnlySpecified false
}

pipeline "demo2" {
    stage "" { echo "cool" }
    runIfOnlySpecified
}


tryPrintPipelineCommandHelp ()
