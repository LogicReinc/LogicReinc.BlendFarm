echo "Enter Version (x.y.z)"

read version

echo Building version $version

echo Publishing BlendFarm OSX-ARM64
dotnet publish LogicReinc.BlendFarm -f net6.0 -c Release -r osx-arm64 -p:PublishSingleFile=true -p:PublishReadyToRunShowWarnings=false --self-contained true -o Deploy/LogicReinc.BlendFarm/_Build/osx-arm64

#Preparing BlendFarm
cd Deploy/LogicReinc.BlendFarm
echo Packaging BlendFarm


echo Preparing OSX Build
rm -R "BlendFarm-$version-OSX-ARM64"
rm "BlendFarm-$version-OSX-ARM64.zip"
cp "_Resources/BlendFarm-___-OSX-ARM64" -R "BlendFarm-$version-OSX-ARM64"
cp "_Build/osx-arm64/LogicReinc.BlendFarm" "BlendFarm-$version-OSX-ARM64/LogicReinc.BlendFarm.app/Contents/MacOS/LogicReinc.BlendFarm"
sed -i "s/1.0.3/$version/" "BlendFarm-$version-OSX-ARM64/LogicReinc.BlendFarm.app/Contents/Info.plist"
find "_Build/osx-arm64/" -name \*.dylib -exec cp {}   "BlendFarm-$version-OSX-ARM64/LogicReinc.BlendFarm.app/Contents/MacOS/" \;
zip -r "BlendFarm-$version-OSX-ARM64.zip" "BlendFarm-$version-OSX-ARM64"

cd ../../