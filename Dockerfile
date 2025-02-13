FROM debian:12 AS build

ARG GS_VERSION=10.02.1
ARG GUAC_VERSION=1.5.5
ARG PREFIX_DIR=/opt/guacamole

ENV LDFLAGS="-lrt"
ENV LC_ALL=C.UTF-8
ENV LD_LIBRARY_PATH=${PREFIX_DIR}/lib

RUN apt-get update && apt-get install -y --no-install-recommends \
    ca-certificates \
    build-essential \
    libcairo2-dev \
    libjpeg62-turbo-dev \
    libpng-dev \
    libtool-bin \
    libpulse-dev \
    libssl-dev \
    uuid-dev \
    build-essential \
    libavcodec-dev \
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
    libwebp-dev \
    wget \
    autoconf \
    automake \
    libtool \
    && rm -rf /var/lib/apt/lists/*

RUN mkdir /tmp/guac-build \
            && wget -O /tmp/guac-build.tar.gz https://dlcdn.apache.org/guacamole/${GUAC_VERSION}/source/guacamole-server-${GUAC_VERSION}.tar.gz \
            && tar -xzf /tmp/guac-build.tar.gz --directory /tmp/guac-build --strip-components 1 \
            && cd /tmp/guac-build/ \
            && autoreconf -fi && ./configure --prefix="${PREFIX_DIR}" --disable-guaclog \
            && make && make install \
            && ldconfig \
            && rm -rf /tmp/guac-build*

FROM debian:12

RUN apt-get update && apt-get install -y --no-install-recommends \
    ca-certificates \
    wget \
    pulseaudio \
    supervisor \
    ghostscript \
    libicu-dev \
    && rm -rf /var/lib/apt/lists/* \
    && sed -i \
        -e 's|#load-module module-native-protocol-tcp|load-module module-native-protocol-tcp auth-anonymous=1|g' \
        /etc/pulse/default.pa

RUN cd /tmp \
            && wget http://sourceforge.net/projects/gs-fonts/files/latest/download?source=files --output-document=ghostscript-fonts-std-8.11.tar.gz \
            && tar -xvf ghostscript-fonts-std-8.11.tar.gz -C /usr/share/ghostscript \
            && fc-cache -v /usr/share/ghostscript/fonts/ \
            && mkdir /usr/include/ghostscript/ \
            && chmod go-w /usr/include/ghostscript/ \
            && rm -f /tmp/ghostscript-fonts-std-8.11.tar.gz

# Arguments to label built container
ARG GIT_SHA
ARG GIT_TAG=1.0.0

# Container labels (http://label-schema.org/)
# Container annotations (https://github.com/opencontainers/image-spec)
LABEL maintainer="Daniel McAssey <hello at glokon dot me>" \
      product="GLOKON WebSocket/Guacamole Proxy Server" \
      version=$GIT_TAG \
      org.label-schema.vcs-ref=$GIT_SHA \
      org.label-schema.vcs-url="https://github.com/GLOKON/guacws-next" \
      org.label-schema.name="GLOKON WebSocket/Guacamole Proxy" \
      org.label-schema.description="WebSocket/Guacamole proxy daemon." \
      org.label-schema.url="https://www.qassist.io/" \
      org.label-schema.vendor="GLOKON" \
      org.label-schema.version=$GIT_TAG \
      org.label-schema.schema-version="1.0" \
      org.opencontainers.image.revision=$GIT_SHA \
      org.opencontainers.image.source="https://github.com/GLOKON/guacws-next" \
      org.opencontainers.image.title="GLOKON WebSocket/Guacamole Proxy" \
      org.opencontainers.image.description="WebSocket/Guacamole proxy daemon." \
      org.opencontainers.image.url="https://www.qassist.io/" \
      org.opencontainers.image.vendor="GLOKON" \
      org.opencontainers.image.version=$GIT_TAG \
      org.opencontainers.image.authors="Daniel McAssey <hello at glokon dot me>"

ENV LOG_LEVEL='info'
ENV Logging__LogLevel__Default='Information'
ENV GuacOptions__UserDriveRoot='/user-drives'
ENV Server__SSL__CertificatePath='/certs/certificate.pfx'
EXPOSE 8080
EXPOSE 8081

RUN adduser guacd 
RUN mkdir -p /user-drives && chown -R guacd:guacd /user-drives
RUN mkdir -p /certs && chown -R guacd:guacd /certs

# Specity user drive volume
VOLUME /user-drives
VOLUME /certs

USER guacd
WORKDIR /app

COPY --from=build --chown=guacd:guacd /opt/guacamole /opt/guacamole
COPY --chown=guacd:guacd ./dist/ .
COPY --chown=guacd:guacd ./docker/ .

CMD ["supervisord", "-c", "supervisor.conf"]
