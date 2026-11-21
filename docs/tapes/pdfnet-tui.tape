Output docs/screenshots/pdfnet-tui.gif
Set FontSize 14
Set Width 1000
Set Height 640
Hide
Type "mkdir -p /tmp/pdfnet-demo && cp docs/examples/sample.pdf /tmp/pdfnet-demo/first.pdf && cp docs/examples/sample.pdf /tmp/pdfnet-demo/second.pdf && dotnet build src/PdfNet.Tui --verbosity quiet && clear"
Enter
Sleep 3s
Show
Type "dotnet run --project src/PdfNet.Tui --no-build"
Enter
Sleep 2s
Type "1"
Enter
Type "/tmp/pdfnet-demo/first.pdf"
Enter
Sleep 1s
Type "1"
Enter
Type "/tmp/pdfnet-demo/second.pdf"
Enter
Sleep 1s
Type "2"
Enter
Type "2"
Enter
Type "1"
Enter
Sleep 1s
Type "4"
Enter
Type "/tmp/pdfnet-demo"
Enter
Sleep 1s
Type "5"
Enter
Sleep 2s
Type "6"
Enter
Sleep 2s
