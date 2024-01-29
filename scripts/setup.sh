#!/bin/bash

GUAC_VERSION="1.5.3"
DAEMON_USER="guacd"

if [ "$EUID" -ne 0 ]
  then echo "Please run as root"
  exit
fi

while true; do
    case "$1" in
        -b | --build-guac ) BUILD_GUAC=true; shift ;;
        -u | --user ) DAEMON_USER="$2"; shift 2 ;;
        -g | --version-guac ) GUAC_VERSION="$2"; shift 2 ;;
        -i | --install-deps ) DEPENDENCIES_OS="$2"; shift 2 ;;
        -- ) shift; break ;;
        * ) break ;;
    esac
done

echo "Setting up GuacWS"
if [ -n ${DEPENDENCIES_OS} ]; then
    case "$DEPENDENCIES_OS" in
        "debian" ) ./install-deps-debian.sh $BUILD_GUAC ;;
        "rhel" ) ./install-deps-rhel.sh $BUILD_GUAC ;;
    esac
fi

echo "Enabling Audio Pipe"
sed -i \
    -e 's|#load-module module-native-protocol-tcp|load-module module-native-protocol-tcp auth-anonymous=1|g' \
    /etc/pulse/default.pa

echo "Prepare GuacWS"
groupadd ${DAEMON_USER} || true
adduser ${DAEMON_USER} --system || true
usermod -a -G ${DAEMON_USER} ${DAEMON_USER}

./set-permissions.sh ${DAEMON_USER}

if [ "$BUILD_GUAC" = true ]; then
    ./install-guacd-from-source.sh ${GUAC_VERSION}
fi

supervisorctl update

echo "GuacWS has been Setup"
