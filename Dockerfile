FROM guacamole/guacd:1.5.4

USER root
RUN apk update && apk add --no-cache \
        pulseaudio \
        supervisor && \
    sed -i \
        -e 's|#load-module module-native-protocol-tcp|load-module module-native-protocol-tcp auth-anonymous=1|g' \
        /etc/pulse/default.pa

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

ENV Logging__LogLevel__Default='Information'
ENV GuacOptions__UserDriveRoot='/user-drives'
EXPOSE 8080
EXPOSE 8081

RUN mkdir -p /user-drives && chown -R guacd:guacd /user-drives

# Specity user drive volume
VOLUME /user-drives

# Create app directory
WORKDIR /app

COPY ./dist/ .

CMD ["supervisord", "-c", "supervisor.conf"]
