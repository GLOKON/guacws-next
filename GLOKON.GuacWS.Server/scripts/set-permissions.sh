#!/bin/bash

SCRIPT_ROOT="$( cd -- "$(dirname "$0")" >/dev/null 2>&1 ; pwd -P )"
INSTALL_DIR=$(dirname "${SCRIPT_ROOT}")
DAEMON_USER="guacd"

if [ -n "$1" ]; then
    DAEMON_USER="$1"
fi

echo "Setting GuacWS permissions to ${DAEMON_USER}"
chmod a+x ${INSTALL_DIR}/GLOKON.GuacWS.Server
chown -R ${DAEMON_USER}:${DAEMON_USER} ${INSTALL_DIR}
setcap CAP_NET_BIND_SERVICE=+eip ${INSTALL_DIR}/GLOKON.GuacWS.Server
