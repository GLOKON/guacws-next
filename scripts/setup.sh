#!/bin/bash

SCRIPT_ROOT="$( cd -- "$(dirname "$0")" >/dev/null 2>&1 ; pwd -P )"
DAEMON_USER="guacd"
GUAC_VERSION="1.5.3"
GS_VERSION="10.02.1"

if [ "$EUID" -ne 0 ]
  then echo "Please run as root"
  exit
fi

while true; do
    case "$1" in
        -b | --build-guac ) BUILD_GUAC=true; shift ;;
        -gs | --build-ghostscript ) BUILD_GS=true; shift ;;
        -u | --user ) DAEMON_USER="$2"; shift 2 ;;
        -vg | --version-guac ) GUAC_VERSION="$2"; shift 2 ;;
        -vgs | --version-ghostscript ) GS_VERSION="$2"; shift 2 ;;
        -i | --install-deps ) DEPENDENCIES_OS="$2"; shift 2 ;;
        -- ) shift; break ;;
        * ) break ;;
    esac
done

echo "Setting up GuacWS"
if [ -n ${DEPENDENCIES_OS} ]; then
    case "$DEPENDENCIES_OS" in
        "debian" ) ${SCRIPT_ROOT}/install-deps-debian.sh $BUILD_GUAC ;;
        "rhel" ) ${SCRIPT_ROOT}/install-deps-rhel.sh $BUILD_GUAC ;;
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

${SCRIPT_ROOT}/set-permissions.sh ${DAEMON_USER}

if [ "$BUILD_GUAC" = true ]; then
    ${SCRIPT_ROOT}/install-guacd-from-source.sh ${GUAC_VERSION}
fi

if [ "$BUILD_GS" = true ]; then
    ${SCRIPT_ROOT}/install-ghostscript-from-source.sh ${GS_VERSION}
fi

echo "GuacWS has been Setup"
echo "Please place the correct supervisord config files, then run `supervisorctl update`"
