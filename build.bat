dotnet publish -c Release -r win-x64 -p:PublishSingleFile=true --self-contained true -p:DefineConstants=WIDNOWS
dotnet publish -c Release -r linux-x64 -p:PublishSingleFile=true --self-contained true -p:DefineConstants=LIUNUX
