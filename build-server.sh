echo "Enter Version (x.y.z)"
read version

echo Building version $version


dotnet publish LogicReinc.BlendFarm.Server -f net6.0 -c Release -r win-x64 -p:PublishSingleFile=true --self-contained true -o Deploy/LogicReinc.BlendFarm.Server/_Build/win-x64
dotnet publish LogicReinc.BlendFarm.Server -f net6.0 -c Release -r linux-x64 -p:PublishSingleFile=true --self-contained true -o Deploy/LogicReinc.BlendFarm.Server/_Build/linux-x64
dotnet publish LogicReinc.BlendFarm.Server -f net6.0 -c Release -r osx-x64 -p:PublishSingleFile=true --self-contained true -o Deploy/LogicReinc.BlendFarm.Server/_Build/osx-x64


#Preparing BlendFarm.Server
cd Deploy/LogicReinc.BlendFarm.Server
echo Packaging BlendFarm.Server


echo Preparing Server Windows Build
rm -R "BlendFarm.Server-$version-Win64"
rm "BlendFarm.Server-$version-Win64.zip"
mkdir "BlendFarm.Server-$version-Win64"
cp "_Build/win-x64/LogicReinc.BlendFarm.Server.exe" "BlendFarm.Server-$version-Win64/LogicReinc.BlendFarm.Server.exe"
zip -r "BlendFarm.Server-$version-Win64.zip" "BlendFarm.Server-$version-Win64"

echo Preparing Server Linux Build
rm -R "BlendFarm.Server-$version-Linux64"
rm "BlendFarm.Server-$version-Linux64.zip"
mkdir "BlendFarm.Server-$version-Linux64"
cp "_Build/linux-x64/LogicReinc.BlendFarm.Server" "BlendFarm.Server-$version-Linux64/LogicReinc.BlendFarm.Server"
zip -r "BlendFarm.Server-$version-Linux64.zip" "BlendFarm.Server-$version-Linux64"


echo Preparing OSX Build
rm -R "BlendFarm.Server-$version-OSX64"
rm "BlendFarm.Server-$version-OSX64.zip"
mkdir "BlendFarm.Server-$version-OSX64"
cp "_Build/osx-x64/LogicReinc.BlendFarm.Server" "BlendFarm.Server-$version-OSX64/LogicReinc.BlendFarm.Server"
zip -r "BlendFarm.Server-$version-OSX64.zip" "BlendFarm.Server-$version-OSX64"

cd ../../