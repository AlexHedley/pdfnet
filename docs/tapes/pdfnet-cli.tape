Output docs/screenshots/pdfnet-cli.gif
Set FontSize 14
Set Width 1200
Set Height 640
Hide
Type "mkdir -p /tmp/pdfnet-demo && cp docs/examples/sample.pdf /tmp/pdfnet-demo/first.pdf && cp docs/examples/sample.pdf /tmp/pdfnet-demo/second.pdf && dotnet build src/PdfNet.Tui --verbosity quiet && clear"
Enter
Sleep 3s
Show
Type "dotnet run --project src/PdfNet.Tui --no-build -- --help"
Enter
Sleep 3s
Type "dotnet run --project src/PdfNet.Tui --no-build -- -o /tmp/pdfnet-demo /tmp/pdfnet-demo/second.pdf /tmp/pdfnet-demo/first.pdf"
Enter
Sleep 4s
