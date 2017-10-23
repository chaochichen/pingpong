# pingpong
Dot Net processes fail-over prototype

The code works with Dotnet Core 2.0 - build/run successful in Windows, build successfully in Linux but behave differently in Linux. Dotnet core "Cross-Platform" is far from reality yet.

Two Nuget dependencies are required:
- NewtonSoft.Json version 10
- Stateless version 4.0.0

Once the code is built in VidualStudio 2015+ or VS Code, please run the executible in two command prompts (on the same machine or different machines):
dotnet PingPong.dll --force-active=[false|true] --my-port=10000 --peer-port=10000 --peer-host=10.100.125.21 --id=1
