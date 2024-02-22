#!/bin/bash

if [ -z "$1" ]; then
    echo "Please specify the version of GhostScript to build"
    exit
fi

GS_VERSION="$1"

echo "Install GhostScript from source"
rm -rf /tmp/ghostscript-build*
mkdir /tmp/ghostscript-build
wget -O /tmp/ghostscript-build.tar.gz https://github.com/ArtifexSoftware/ghostpdl-downloads/releases/download/${GS_VERSION//./}/ghostscript-${GS_VERSION}.tar.gz
tar -zxvf /tmp/ghostscript-build.tar.gz --directory /tmp/ghostscript-build --strip-components 1
cd /tmp/ghostscript-build/
./configure --prefix=/usr --enable-dynamic --disable-compile-inits --with-system-libtiff
make
make so
sudo make install
sudo chmod go+w /usr/include/ghostscript/
sudo make soinstall && install -v -m644 base/*.h /usr/include/ghostscript && sudo ln -v -s ghostscript /usr/include/ps

cd ..
wget http://sourceforge.net/projects/gs-fonts/files/latest/download?source=files --output-document=ghostscript-fonts-std-8.11.tar.gz
sudo tar -xvf ghostscript-fonts-std-8.11.tar.gz -C /usr/share/ghostscript
fc-cache -v /usr/share/ghostscript/fonts/
sudo mkdir /usr/include/ghostscript/
sudo chmod go-w /usr/include/ghostscript/

rm -f /tmp/ghostscript-fonts-std-8.11.tar.gz
rm -rf /tmp/ghostscript-build*
