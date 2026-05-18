export VERSION ?= 1.0.0.0
export GITHUB_REPO ?= jellystrm/source-manager
export PROJECT := Jellyfin.Plugin.Jellystrm
export FRAMEWORK := net9.0
export FILE := jellystrm-${VERSION}.zip

.PHONY: print clean restore build publish zip csum update-manifest release

print:
	@echo ${VERSION}

clean:
	dotnet clean ${PROJECT}.sln
	rm -rf dist

restore:
	dotnet restore ${PROJECT}.sln

build:
	dotnet build ${PROJECT}.sln --configuration Release --no-restore

publish:
	dotnet publish ${PROJECT}/${PROJECT}.csproj --configuration Release --framework ${FRAMEWORK} --no-restore --output ./publish/${VERSION}

zip: publish
	mkdir -p ./dist
	cd ./publish/${VERSION} && zip -r "../../dist/${FILE}" .

csum: zip
	md5sum "./dist/${FILE}"

update-manifest:
	node scripts/update-manifest.js

release: restore build zip update-manifest
