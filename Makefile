export VERSION ?= 1.0.0.0
export GITHUB_REPO ?= jellystrm/source-manager
export PROJECT := Jellyfin.Plugin.SourceManager
export FRAMEWORK := net9.0
export FILE := source-manager-${VERSION}.zip
export CSPROJ := ${PROJECT}/${PROJECT}.csproj

.PHONY: print clean restore update-version build publish zip csum update-manifest release

print:
	@echo ${VERSION}

clean:
	dotnet clean ${PROJECT}.sln
	rm -rf dist publish

restore:
	dotnet restore ${PROJECT}.sln

update-version:
	@sed -i.bak 's|<Version>.*</Version>|<Version>${VERSION}</Version>|' ${CSPROJ} && rm -f ${CSPROJ}.bak
	@echo "Updated ${CSPROJ} to version ${VERSION}"

build:
	dotnet build ${PROJECT}.sln --configuration Release --no-restore

publish:
	dotnet publish ${CSPROJ} --configuration Release --framework ${FRAMEWORK} --no-restore --no-build --output ./publish/${VERSION}

zip: publish
	mkdir -p ./dist
	cd ./publish/${VERSION} && zip -r "../../dist/${FILE}" .

csum: zip
	md5sum "./dist/${FILE}"

update-manifest:
	node scripts/update-manifest.js

release: restore update-version build zip update-manifest
