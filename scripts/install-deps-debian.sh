#!/bin/bash

echo "Install Base Dependencies"
apt install -y pulseaudio \
    supervisor

if [ "$1" = true ]; then
# Build GuacD from source
echo "Install GuacD Build Dependencies"
apt install -y build-essential \
    libcairo2-dev \
    libjpeg-turbo8-dev \
    libpng-dev \
    libtool-bin \
    libpulse-dev \
    libssl-dev \
    uuid-dev \
    gcc gcc-c++

echo "Install GuacD Protocol Dependencies"
apt install -y libavcodec-dev \
    libavformat-dev \
    libavutil-dev \
    libswscale-dev \
    freerdp2-dev \
    libpango1.0-dev \
    libssh2-1-dev \
    libtelnet-dev \
    libvncserver-dev \
    libwebsockets-dev \
    libvorbis-dev \
    libwebp-dev
fi
