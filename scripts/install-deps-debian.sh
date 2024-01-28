#!/bin/sh

echo "Install Base Dependencies"
apt install -y pulseaudio \
    supervisor

if [ -z "$1" ]; then
echo "Instal GuacD Build Dependencies"
apt install -y cairo-devel \
    libjpeg-turbo-devel \
    libjpeg-devel \
    libpng-devel \
    libtool \
    libuuid-devel \
    pulseaudio-libs-devel \
    uuid-devel \
    pulseaudio \
    supervisor

echo "Install GuacD Protocol Dependencies"
apt install -y pango-devel \
    freerdp-devel \
    ffmpeg-devel \
    libgcrypt-devel \
    libssh2-devel \
    libtelnet-devel \
    libvncserver-devel \
    libwebsockets-devel \
    openssl-devel \
    libvorbis-devel \
    libwebp-devel
fi
