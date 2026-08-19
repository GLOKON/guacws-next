#!/bin/bash

echo "Install Base Dependencies"
yum install -y pulseaudio

if [ "$1" = true ]; then
# Build GuacD from source
echo "Install Build Tools"
yum groupinstall -y 'Development Tools'
echo "Install GuacD Build Dependencies"
yum install -y cairo-devel \
    libjpeg-turbo-devel \
    libpng-devel \
    libtool \
    libuuid-devel \
    openssl-devel \
    pulseaudio-libs-devel \
    gcc gcc-c++

echo "Install GuacD Protocol Dependencies"
yum install -y pango-devel \
    freerdp-devel \
    ffmpeg-devel \
    libgcrypt-devel \
    libssh2-devel \
    libtelnet-devel \
    libvncserver-devel \
    libwebsockets-devel \
    libvorbis-devel \
    libwebp-devel
fi
