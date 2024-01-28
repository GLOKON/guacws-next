#!/bin/sh

if [ -z "$1" ]
  then
    echo "Please specify the version"
fi

GUAC_VERSION="$1"

echo "Install GuacD from source"
export LDFLAGS="-lrt"
rm -rf /tmp/guac-build*
mkdir /tmp/guac-build
wget -O /tmp/guac-build.tar.gz https://dlcdn.apache.org/guacamole/${GUAC_VERSION}/source/guacamole-server-${GUAC_VERSION}.tar.gz
tar -xzf /tmp/guac-build.tar.gz --directory /tmp/guac-build --strip-components 1
cd /tmp/guac-build/
make distclean || true && autoreconf -fi
./configure --with-guacd-conf=/etc/guacamole/guacd.conf
make && make install
ldconfig
rm -rf /tmp/guac-build*
sudo supervisorctl update